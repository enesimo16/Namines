'use client';

import { Suspense, useCallback, useEffect, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import Link from 'next/link';
import {
  ShieldCheck,
  Cable,
  Loader2,
  Download,
  RotateCcw,
  Trash2,
  CheckCircle2,
  XCircle,
  Clock,
  DatabaseBackup,
  HardDrive,
  KeyRound,
  Activity,
  Lock,
  ArrowLeft,
  ArrowUpRight,
  Layers,
  Database,
} from 'lucide-react';
import { authService, gatewayKeyService } from '../../services/api';
import { useAuthStore } from '../../store/useAuthStore';
import { useAuthModalStore } from '../../store/useAuthModalStore';
import { useProjectHistoryStore } from '../../store/useProjectHistoryStore';
import { useActiveProjectStore } from '../../store/useActiveProjectStore';
import { ModuleSymbol, ModuleWordmark } from '../../components/layout/ModuleLogo';
import { useQuotaStore } from '../../store/useQuotaStore';
import type { CloudProjectDto } from '../../types/api';
import type { GatewayKey } from '../../types/gatewayKey';
import {
  vaultApi,
  VaultGroundError,
  formatDuration,
  formatSize,
  type VaultBackup,
  type VaultRestore,
  type VaultSchedule,
  type VaultHealth,
} from '../../services/vaultGroundApi';
import GatewayKeyPanel from '../../components/review/GatewayKeyPanel';
import {
  PageShell,
  PageHeader,
  Card,
  Callout,
  Badge,
  Button,
  EmptyState,
  Switch,
  Field,
  inputClass,
  Select,
  DataTable,
  Td,
} from '../../components/workspace/WorkspaceKit';

const DAY_NAMES = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
const HOURS = Array.from({ length: 24 }, (_, hour) => hour);
const KIND_LABELS: Record<VaultBackup['kind'], string> = {
  Manual: 'Manual',
  Scheduled: 'Scheduled',
  PreRestore: 'Pre-restore',
};

const formatWhen = (iso: string) =>
  new Date(iso).toLocaleString('en-US', { dateStyle: 'medium', timeStyle: 'short' });

interface ProjectVaultOverview {
  project: CloudProjectDto;
  backupCount: number;
  latestBackupAt: string | null;
  scheduleEnabled: boolean;
  scheduleCadence: string | null;
  apiKeyCount: number;
}

export default function VaultPage() {
  return (
    <Suspense
      fallback={
        <PageShell>
          <Card>
            <div className="flex items-center gap-2 px-5 py-8 text-[13px] text-content-muted">
              <Loader2 className="w-4 h-4 animate-spin" aria-hidden="true" /> Loading Vault…
            </div>
          </Card>
        </PageShell>
      }
    >
      <VaultPageContent />
    </Suspense>
  );
}

/**
 * Faz 5 — Namines Vault:
 * 1. `/vault?project=<id>` (Projeye Kilitli Oturum):
 *    Ekmek kırıntısı: `vault / {owner} / {projectName}` formatındadır.
 *    `vault` linkine tıklandığında genel konsola dönülür.
 * 2. `/vault` (Parametresiz Genel Yönetim Düzlemi):
 *    Tüm projelerin yedekleme durumu, merkezi API anahtarı yönetimi ve kota/güvenlik analitiği.
 */
function VaultPageContent() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const projectParam = searchParams.get('project');

  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);
  const currentUser = useAuthStore((s) => s.user);
  const openAuthModal = useAuthModalStore((s) => s.open);
  const localProjects = useProjectHistoryStore((s) => s.projects);
  const { scopedProjectId, scopedProjectName, scopedHubRepo, setScopedProject, clearProjectSession } =
    useActiveProjectStore();
  const { dailyLimit, remaining, plan } = useQuotaStore();

  const [projects, setProjects] = useState<CloudProjectDto[] | null>(null);
  const [health, setHealth] = useState<VaultHealth | null>(null);
  const [overviews, setOverviews] = useState<Record<string, ProjectVaultOverview>>({});
  const [globalTab, setGlobalTab] = useState<'backups' | 'apikeys' | 'analytics'>('backups');
  const [scopedTab, setScopedTab] = useState<'backups' | 'apikeys'>('backups');
  const [apiKeyProjectId, setApiKeyProjectId] = useState<string | null>(null);

  const loadProjects = useCallback(async () => {
    if (!isAuthenticated) return;
    try {
      const [list, status] = await Promise.all([
        authService.getCloudProjects(),
        vaultApi.health().catch(() => null),
      ]);
      setProjects(list);
      setHealth(status);
      if (list.length > 0 && !apiKeyProjectId) {
        setApiKeyProjectId(list[0].id);
      }

      // Her proje için özet yedek ve API anahtarı sayılarını paralel topla
      const summaryEntries = await Promise.all(
        list.map(async (proj) => {
          let backupCount = 0;
          let latestBackupAt: string | null = null;
          let scheduleEnabled = false;
          let scheduleCadence: string | null = null;
          let apiKeyCount = 0;

          if (proj.hasConnection) {
            try {
              const [bList, sched] = await Promise.all([
                vaultApi.list(proj.id).catch(() => ({ backups: [], store: '' })),
                vaultApi.schedule(proj.id).catch(() => null),
              ]);
              const succeeded = bList.backups.filter((b) => b.status === 'Succeeded');
              backupCount = succeeded.length;
              latestBackupAt = succeeded[0]?.createdAt ?? null;
              if (sched) {
                scheduleEnabled = sched.enabled;
                scheduleCadence = sched.cadence;
              }
            } catch {
              // ignore per-project fetch failure
            }
          }

          try {
            const keys: GatewayKey[] = await gatewayKeyService.list(proj.id).catch(() => []);
            apiKeyCount = keys.filter((k) => !k.revokedAt).length;
          } catch {
            // ignore
          }

          const item: ProjectVaultOverview = {
            project: proj,
            backupCount,
            latestBackupAt,
            scheduleEnabled,
            scheduleCadence,
            apiKeyCount,
          };
          return [proj.id, item] as const;
        }),
      );

      setOverviews(Object.fromEntries(summaryEntries));
    } catch {
      setProjects([]);
    }
  }, [isAuthenticated, apiKeyProjectId]);

  useEffect(() => {
    loadProjects();
  }, [loadProjects]);

  // URL ?project=<id> senkronizasyonu
  useEffect(() => {
    if (projectParam) {
      const cloudMatch = projects?.find((p) => p.id === projectParam);
      const localMatch = localProjects.find((p) => p.id === projectParam);
      const resolvedName =
        cloudMatch?.name || localMatch?.name || scopedProjectName || scopedHubRepo?.name || 'Project Workspace';
      // activeProjectId BİLEREK değiştirilmiyor: şema store'u başka projeye ait olabilir ve
      // canvas autosave'i o şemayı bu projenin üzerine yazardı.
      setScopedProject(projectParam, resolvedName);
    } else {
      clearProjectSession();
    }
  }, [projectParam, projects, localProjects, scopedProjectName, scopedHubRepo, setScopedProject, clearProjectSession]);

  if (!isAuthenticated) {
    return (
      <PageShell width="max-w-3xl">
        <EmptyState
          icon={ShieldCheck}
          title="Sign in to use Namines Vault"
          description="You need an account to back up and restore your databases and manage Gateway API keys."
          action={
            <Button variant="primary" onClick={openAuthModal}>
              Log in / Sign up
            </Button>
          }
        />
      </PageShell>
    );
  }

  // ─────────────────────────────────────────────────────────────────────────
  // MODE A: PROJECT-SCOPED VAULT (/vault?project=<id>)
  // ─────────────────────────────────────────────────────────────────────────
  if (projectParam) {
    const lockedCloud = projects?.find((p) => p.id === projectParam) ?? null;
    const lockedLocal = localProjects.find((p) => p.id === projectParam) ?? null;
    const projectName =
      scopedProjectName || lockedCloud?.name || lockedLocal?.name || scopedHubRepo?.name || projectParam;
    const resolvedOwner = scopedHubRepo?.owner || currentUser?.username || 'workspace';

    return (
      <PageShell>
        {/* Top Breadcrumb (Matching Hub format: vault / owner / ProjectName) */}
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 pb-3 border-b border-line">
          <div className="flex items-center flex-wrap gap-2 text-sm sm:text-base">
            <ModuleSymbol module="vault" size={20} />
            <Link
              href="/vault"
              onClick={() => clearProjectSession()}
              className="group inline-flex items-center text-content-muted hover:text-content-primary transition-colors cursor-pointer"
            >
              <ModuleWordmark module="vault" height={13} className="text-content-muted group-hover:text-content-primary transition-colors" />
            </Link>
            <span className="text-content-muted">/</span>
            <span className="text-content-secondary font-mono">{resolvedOwner}</span>
            <span className="text-content-muted">/</span>
            <h1 className="font-sans font-bold text-content-primary tracking-tight text-base sm:text-lg">
              {projectName}
            </h1>
          </div>

          {/* Project-Scoped Sub-Tabs */}
          <div className="inline-flex items-center rounded-[var(--radius-control)] bg-surface-800 border border-line p-0.5 self-start sm:self-auto">
            <button
              type="button"
              onClick={() => setScopedTab('backups')}
              className={`inline-flex items-center gap-1.5 px-3 py-1 rounded-[var(--radius-control)] text-xs font-semibold transition-colors cursor-pointer ${
                scopedTab === 'backups'
                  ? 'bg-surface-700 text-content-primary border border-line-strong shadow-xs'
                  : 'text-content-muted hover:text-content-primary'
              }`}
            >
              <DatabaseBackup className="w-3.5 h-3.5 text-accent-text" />
              <span>PITR Backups &amp; Schedule</span>
            </button>
            <button
              type="button"
              onClick={() => setScopedTab('apikeys')}
              className={`inline-flex items-center gap-1.5 px-3 py-1 rounded-[var(--radius-control)] text-xs font-semibold transition-colors cursor-pointer ${
                scopedTab === 'apikeys'
                  ? 'bg-surface-700 text-content-primary border border-line-strong shadow-xs'
                  : 'text-content-muted hover:text-content-primary'
              }`}
            >
              <KeyRound className="w-3.5 h-3.5 text-accent-text" />
              <span>Gateway API Keys</span>
            </button>
          </div>
        </div>

        {scopedTab === 'apikeys' ? (
          <Card
            title={`Gateway API Keys — ${projectName}`}
            description="Issue and revoke API keys scoped strictly to this project's REST and MCP Gateway endpoints."
          >
            <GatewayKeyPanel projectId={projectParam} />
          </Card>
        ) : (
          <VaultDetail key={projectParam} projectId={projectParam} projectName={projectName} />
        )}
      </PageShell>
    );
  }

  // ─────────────────────────────────────────────────────────────────────────
  // MODE B: GLOBAL VAULT & SECURITY CONSOLE (/vault without ?project=)
  // ─────────────────────────────────────────────────────────────────────────
  const totalProjects = projects?.length ?? 0;
  const connectedProjects = projects?.filter((p) => p.hasConnection).length ?? 0;
  const totalBackups = Object.values(overviews).reduce((acc, o) => acc + o.backupCount, 0);
  const totalApiKeys = Object.values(overviews).reduce((acc, o) => acc + o.apiKeyCount, 0);

  return (
    <PageShell>
      <div id="vault-header" className="flex flex-wrap items-start justify-between gap-4">
        <PageHeader
          icon={ShieldCheck}
          title="Global Vault & Security Console"
          description="Organization-wide encrypted database backups (PITR), Gateway API key governance, and usage analytics across all projects."
        />

        {/* Global Console Navigation Tabs */}
        <div className="inline-flex items-center rounded-[var(--radius-control)] bg-surface-800 border border-line p-1 self-center">
          <button
            type="button"
            onClick={() => setGlobalTab('backups')}
            className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-[var(--radius-control)] text-xs font-semibold transition-colors cursor-pointer ${
              globalTab === 'backups'
                ? 'bg-surface-700 text-content-primary border border-line-strong shadow-xs'
                : 'text-content-muted hover:text-content-primary'
            }`}
          >
            <DatabaseBackup className="w-3.5 h-3.5 text-accent-text" />
            <span>All Project Backups</span>
          </button>
          <button
            type="button"
            onClick={() => setGlobalTab('apikeys')}
            className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-[var(--radius-control)] text-xs font-semibold transition-colors cursor-pointer ${
              globalTab === 'apikeys'
                ? 'bg-surface-700 text-content-primary border border-line-strong shadow-xs'
                : 'text-content-muted hover:text-content-primary'
            }`}
          >
            <KeyRound className="w-3.5 h-3.5 text-accent-text" />
            <span>Global API Keys ({totalApiKeys})</span>
          </button>
          <button
            type="button"
            onClick={() => setGlobalTab('analytics')}
            className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-[var(--radius-control)] text-xs font-semibold transition-colors cursor-pointer ${
              globalTab === 'analytics'
                ? 'bg-surface-700 text-content-primary border border-line-strong shadow-xs'
                : 'text-content-muted hover:text-content-primary'
            }`}
          >
            <Activity className="w-3.5 h-3.5 text-accent-text" />
            <span>Usage &amp; Security</span>
          </button>
        </div>
      </div>

      {globalTab === 'backups' && (
        <Card
          title="All Projects — Backup & Recovery Fleet"
          description="Select a project to open its dedicated Vault workspace, configure retention schedules, or trigger an immediate snapshot."
          flush
        >
          {!projects ? (
            <div className="flex items-center gap-2 px-5 py-8 text-[13px] text-content-muted">
              <Loader2 className="w-4 h-4 animate-spin" aria-hidden="true" /> Loading projects…
            </div>
          ) : projects.length === 0 ? (
            <EmptyState
              icon={ShieldCheck}
              title="No cloud projects yet"
              description="Create a database architecture in the Canvas or provision a database in Ground."
              action={
                <Button variant="primary" onClick={() => router.push('/canvas')}>
                  Open Diagram Canvas
                </Button>
              }
            />
          ) : (
            <DataTable
              caption="Organization project backups overview"
              columns={[
                { label: 'Project' },
                { label: 'Database Status' },
                { label: 'Schedule' },
                { label: 'Snapshots' },
                { label: 'Latest Backup' },
                { label: 'Gateway Keys' },
                { label: 'Actions', align: 'right' },
              ]}
            >
              {projects.map((p) => {
                const ov = overviews[p.id];
                return (
                  <tr key={p.id} className="hover:bg-surface-700/30 transition-colors">
                    <Td>
                      <div className="flex items-center gap-2">
                        <Database className="w-3.5 h-3.5 text-accent-text shrink-0" />
                        <span className="font-semibold text-content-primary">{p.name}</span>
                      </div>
                    </Td>
                    <Td>
                      {p.hasConnection ? (
                        <Badge tone="success" icon={CheckCircle2}>
                          {p.connectionDbType ?? 'Connected'}
                        </Badge>
                      ) : (
                        <Badge tone="neutral">Not connected</Badge>
                      )}
                    </Td>
                    <Td>
                      {ov?.scheduleEnabled ? (
                        <Badge tone="accent" icon={Clock}>
                          {ov.scheduleCadence ?? 'Daily'}
                        </Badge>
                      ) : (
                        <span className="text-content-muted text-xs">Manual only</span>
                      )}
                    </Td>
                    <Td mono>{ov ? ov.backupCount : '0'}</Td>
                    <Td>{ov?.latestBackupAt ? formatWhen(ov.latestBackupAt) : 'None yet'}</Td>
                    <Td mono>{ov ? `${ov.apiKeyCount} active` : '0 active'}</Td>
                    <Td align="right">
                      <Button
                        size="sm"
                        variant="secondary"
                        icon={ArrowUpRight}
                        onClick={() => {
                          setScopedProject(p.id, p.name);
                          router.push(`/vault?project=${encodeURIComponent(p.id)}`);
                        }}
                      >
                        Open Project Vault
                      </Button>
                    </Td>
                  </tr>
                );
              })}
            </DataTable>
          )}
        </Card>
      )}

      {globalTab === 'apikeys' && (
        <div className="space-y-4">
          <Card
            title="Global Gateway API Keys & Table Access"
            description="Select any project below to issue, inspect, or revoke its REST & MCP Gateway API keys and table-level permissions."
          >
            {projects && projects.length > 0 ? (
              <div className="space-y-5">
                <div className="flex flex-wrap items-center gap-2 pb-4 border-b border-line">
                  <span className="text-xs font-mono uppercase tracking-wider text-content-muted mr-2">
                    Select Project:
                  </span>
                  {projects.map((p) => {
                    const active = apiKeyProjectId === p.id;
                    const count = overviews[p.id]?.apiKeyCount ?? 0;
                    return (
                      <button
                        key={p.id}
                        type="button"
                        onClick={() => setApiKeyProjectId(p.id)}
                        className={`inline-flex items-center gap-2 px-3 py-1.5 rounded-[var(--radius-control)] text-xs font-medium border transition-colors cursor-pointer ${
                          active
                            ? 'bg-surface-700 text-content-primary border-line-strong font-semibold shadow-xs'
                            : 'bg-surface-850 text-content-secondary border-line hover:border-line-strong hover:text-content-primary'
                        }`}
                      >
                        <span>{p.name}</span>
                        <span className="px-1.5 py-0.5 rounded-[var(--radius-badge)] bg-surface-800 border border-line text-[10px] font-mono text-content-muted">
                          {count}
                        </span>
                      </button>
                    );
                  })}
                </div>

                {apiKeyProjectId && <GatewayKeyPanel key={apiKeyProjectId} projectId={apiKeyProjectId} />}
              </div>
            ) : (
              <EmptyState
                icon={KeyRound}
                title="No projects available"
                description="Create a project first to generate Gateway API keys."
              />
            )}
          </Card>
        </div>
      )}

      {globalTab === 'analytics' && (
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          <Card
            title="AI & Workspace Quota Analytics"
            description="Current billing tier, daily AI operation allowance, and workspace capacity."
          >
            <div className="space-y-3 text-xs">
              <div className="flex items-center justify-between py-2 border-b border-line">
                <span className="text-content-muted">Active Plan</span>
                <span className="font-mono font-semibold text-content-primary uppercase">{plan || 'Free'}</span>
              </div>
              <div className="flex items-center justify-between py-2 border-b border-line">
                <span className="text-content-muted">Remaining Daily AI Credits</span>
                <span className="font-mono font-semibold text-content-primary">
                  {remaining} / {dailyLimit || 'Unlimited'}
                </span>
              </div>
              <div className="flex items-center justify-between py-2 border-b border-line">
                <span className="text-content-muted">Total Cloud Repositories</span>
                <span className="font-mono font-semibold text-content-primary">{totalProjects}</span>
              </div>
              <div className="flex items-center justify-between py-2">
                <span className="text-content-muted">Local IndexedDB Snapshots</span>
                <span className="font-mono font-semibold text-content-primary">{localProjects.length}</span>
              </div>
            </div>
          </Card>

          <Card
            title="Security & Encryption Posture"
            description="Platform-level security guarantees enforced across Vault, Ground, and Gateway."
          >
            <div className="space-y-3 text-xs">
              <div className="flex items-center justify-between py-2 border-b border-line">
                <span className="text-content-muted">Backup Encryption</span>
                <Badge tone="success" icon={ShieldCheck}>
                  AES-256-GCM Authenticated
                </Badge>
              </div>
              <div className="flex items-center justify-between py-2 border-b border-line">
                <span className="text-content-muted">Gateway Default Access Policy</span>
                <Badge tone="accent" icon={Lock}>
                  Deny-by-Default per Table
                </Badge>
              </div>
              <div className="flex items-center justify-between py-2 border-b border-line">
                <span className="text-content-muted">SSRF &amp; Private Network Guard</span>
                <Badge tone="success" icon={CheckCircle2}>
                  Enforced on Introspection &amp; Webhooks
                </Badge>
              </div>
              <div className="flex items-center justify-between py-2">
                <span className="text-content-muted">Pre-Restore Safety Snapshot</span>
                <Badge tone="success" icon={CheckCircle2}>
                  Automatic before every rollback
                </Badge>
              </div>
            </div>
          </Card>
        </div>
      )}
    </PageShell>
  );
}

function VaultDetail({ projectId, projectName }: { projectId: string; projectName: string }) {
  const [backups, setBackups] = useState<VaultBackup[] | null>(null);
  const [restores, setRestores] = useState<VaultRestore[]>([]);
  const [store, setStore] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [rowBusyId, setRowBusyId] = useState<string | null>(null);
  const [schedule, setSchedule] = useState<VaultSchedule | null>(null);
  const [savingSchedule, setSavingSchedule] = useState(false);
  const [health, setHealth] = useState<VaultHealth | null>(null);
  const [confirming, setConfirming] = useState<{ backup: VaultBackup; typed: string } | null>(null);
  const [deletingId, setDeletingId] = useState<string | null>(null);

  const reload = useCallback(async () => {
    try {
      const [list, history, plan, status] = await Promise.all([
        vaultApi.list(projectId),
        vaultApi.restores(projectId),
        vaultApi.schedule(projectId),
        vaultApi.health(),
      ]);
      setBackups(list.backups);
      setStore(list.store);
      setRestores(history);
      setSchedule(plan);
      setHealth(status);
      setError(null);
    } catch (err) {
      setError(err instanceof VaultGroundError ? err.message : 'Could not load backups.');
      setBackups([]);
    }
  }, [projectId]);

  useEffect(() => {
    reload();
  }, [reload]);

  async function handleCreate() {
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      const started = await vaultApi.create(projectId);
      setNotice('Backup started, running…');
      const outcome = await vaultApi.waitForBackup(projectId, started.backupId);
      if (!outcome.done) {
        setNotice('Backup is still running. You can track its status in the list below.');
      } else if (outcome.status === 'Succeeded') {
        setNotice('Backup created.');
      } else {
        setNotice(null);
        setError(outcome.errorMessage ?? 'Backup failed.');
      }
      await reload();
    } catch (err) {
      setError(err instanceof VaultGroundError ? err.message : 'Backup failed.');
    } finally {
      setBusy(false);
    }
  }

  async function handleVerify(backup: VaultBackup) {
    setRowBusyId(backup.id);
    setError(null);
    setNotice(null);
    try {
      await vaultApi.verify(projectId, backup.id);
      setNotice('Backup verified: restored to a throwaway server and proven to work.');
    } catch (err) {
      setError(err instanceof VaultGroundError ? err.message : 'Verification failed.');
    } finally {
      setRowBusyId(null);
      await reload();
    }
  }

  async function handleSaveSchedule(next: VaultSchedule) {
    setSavingSchedule(true);
    setError(null);
    try {
      setSchedule(await vaultApi.saveSchedule(projectId, next));
      setNotice('Backup schedule saved.');
    } catch (err) {
      setError(err instanceof VaultGroundError ? err.message : 'Could not save the schedule.');
    } finally {
      setSavingSchedule(false);
    }
  }

  async function handleDownload(backup: VaultBackup) {
    setRowBusyId(backup.id);
    setError(null);
    try {
      const { blob, fileName } = await vaultApi.download(projectId, backup.id);
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = fileName;
      link.click();
      URL.revokeObjectURL(url);
    } catch (err) {
      setError(err instanceof VaultGroundError ? err.message : 'Download failed.');
    } finally {
      setRowBusyId(null);
    }
  }

  async function handleDelete(backup: VaultBackup) {
    setRowBusyId(backup.id);
    setError(null);
    try {
      await vaultApi.remove(projectId, backup.id);
      setDeletingId(null);
      await reload();
    } catch (err) {
      setError(err instanceof VaultGroundError ? err.message : 'Delete failed.');
    } finally {
      setRowBusyId(null);
    }
  }

  async function handleRestore() {
    if (!confirming) return;
    const { backup, typed } = confirming;
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      const result = await vaultApi.restore(projectId, backup.id, typed);
      setConfirming(null);
      setNotice(
        result.preRestoreBackupId
          ? 'Restore complete. The previous state was saved as a separate backup.'
          : 'Restore complete.',
      );
      await reload();
    } catch (err) {
      setError(err instanceof VaultGroundError ? err.message : 'Restore failed.');
    } finally {
      setBusy(false);
    }
  }

  const confirmMatches = confirming?.typed.trim() === confirming?.backup.databaseName;
  const succeeded = (backups ?? []).filter((b) => b.status === 'Succeeded');
  const latest = succeeded[0];

  return (
    <div className="space-y-5">
      {health && !health.ok && (
        <Callout tone="danger" title="Backups are unavailable">
          {health.problem}
        </Callout>
      )}
      {error && <Callout tone="danger">{error}</Callout>}
      {notice && <Callout tone="success">{notice}</Callout>}

      {schedule && (
        <Card
          id="vault-schedule-card"
          title="Automatic backups"
          description="Retention only limits automatic backups; manual ones are never touched."
          actions={
            <div className="flex items-center gap-2.5 text-[13px] text-content-secondary">
              <Switch
                checked={schedule.enabled}
                disabled={savingSchedule}
                label="Automatic backups"
                onChange={(enabled) => handleSaveSchedule({ ...schedule, enabled })}
              />
              {schedule.enabled ? 'On' : 'Off'}
            </div>
          }
        >
          <div className="flex flex-wrap items-end gap-4">
            <Field label="Frequency" htmlFor="vault-frequency">
              <div className="w-36">
                <Select
                  id="vault-frequency"
                  value={schedule.cadence}
                  disabled={savingSchedule}
                  onChange={(e) =>
                    setSchedule({
                      ...schedule,
                      cadence: e.target.value as typeof schedule.cadence,
                      dayOfWeek: e.target.value === 'Weekly' ? (schedule.dayOfWeek ?? 0) : null,
                    })
                  }
                >
                  <option value="Daily">Daily</option>
                  <option value="Weekly">Weekly</option>
                </Select>
              </div>
            </Field>
            {schedule.cadence === 'Weekly' && (
              <Field label="Day" htmlFor="vault-day">
                <div className="w-40">
                  <Select
                    id="vault-day"
                    value={schedule.dayOfWeek ?? 0}
                    disabled={savingSchedule}
                    onChange={(e) => setSchedule({ ...schedule, dayOfWeek: Number(e.target.value) })}
                  >
                    {DAY_NAMES.map((name, index) => (
                      <option key={name} value={index}>
                        {name}
                      </option>
                    ))}
                  </Select>
                </div>
              </Field>
            )}
            <Field label="Hour (UTC)" htmlFor="vault-hour">
              <div className="w-32">
                <Select
                  id="vault-hour"
                  value={schedule.hourUtc}
                  disabled={savingSchedule}
                  onChange={(e) => setSchedule({ ...schedule, hourUtc: Number(e.target.value) })}
                >
                  {HOURS.map((h) => (
                    <option key={h} value={h}>
                      {String(h).padStart(2, '0')}:00
                    </option>
                  ))}
                </Select>
              </div>
            </Field>
            <Field label="Backups to keep" htmlFor="vault-retain">
              <input
                id="vault-retain"
                type="number"
                min={1}
                max={60}
                value={schedule.retainCount}
                disabled={savingSchedule}
                onChange={(e) => setSchedule({ ...schedule, retainCount: Number(e.target.value) })}
                className={`${inputClass} w-28`}
              />
            </Field>
            <Button variant="primary" busy={savingSchedule} onClick={() => handleSaveSchedule(schedule)}>
              Save schedule
            </Button>
          </div>
          {schedule.lastRunAt && (
            <p className="mt-3 text-[12px] text-content-muted">
              Last automatic backup: {formatWhen(schedule.lastRunAt)}.
            </p>
          )}
        </Card>
      )}

      {confirming && (
        <Card
          tone="danger"
          title="Restore this backup?"
          description={`Current data in ${confirming.backup.databaseName} will be deleted and replaced with the backup from ${formatWhen(confirming.backup.createdAt)}. This cannot be undone — a safety backup of the current state is taken first.`}
        >
          <div className="space-y-3 max-w-md">
            <Field
              label={`Type the database name to continue: ${confirming.backup.databaseName}`}
              htmlFor="vault-confirm-name"
            >
              <input
                id="vault-confirm-name"
                autoFocus
                value={confirming.typed}
                placeholder={confirming.backup.databaseName}
                onChange={(e) => setConfirming({ ...confirming, typed: e.target.value })}
                className={inputClass}
              />
            </Field>
            <div className="flex gap-2">
              <Button
                variant="danger"
                icon={RotateCcw}
                busy={busy}
                disabled={!confirmMatches}
                onClick={handleRestore}
              >
                {busy ? 'Restoring…' : 'Restore'}
              </Button>
              <Button variant="ghost" disabled={busy} onClick={() => setConfirming(null)}>
                Cancel
              </Button>
            </div>
          </div>
        </Card>
      )}

      <Card
        id="vault-backups-table"
        title="Backups"
        description={`All backups of ${projectName}, newest first.${store ? ` Stored at ${store.replace(/^Local disk \((.*)\)$/, '$1')}.` : ''}`}
        actions={
          <Button
            id="vault-backup-now-btn"
            variant="primary"
            icon={DatabaseBackup}
            busy={busy}
            disabled={health?.ok === false}
            onClick={handleCreate}
          >
            {busy ? 'Working…' : 'Back up now'}
          </Button>
        }
        flush
      >
        {!backups ? (
          <div className="flex items-center gap-2 px-5 pb-5 text-[13px] text-content-muted">
            <Loader2 className="w-4 h-4 animate-spin" aria-hidden="true" /> Loading…
          </div>
        ) : backups.length === 0 ? (
          <EmptyState
            icon={ShieldCheck}
            title="No backups yet"
            description="Create the first one with “Back up now”."
          />
        ) : (
          <DataTable
            caption={`Backups of ${projectName}`}
            columns={[
              { label: 'Date' },
              { label: 'Kind' },
              { label: 'Engine' },
              { label: 'Size' },
              { label: 'Duration' },
              { label: 'Status' },
              { label: 'Verified' },
              { label: 'Actions', align: 'right' },
            ]}
          >
            {backups.map((b) => (
              <tr key={b.id} className="hover:bg-surface-700/30 transition-colors">
                <Td>{formatWhen(b.createdAt)}</Td>
                <Td>{KIND_LABELS[b.kind]}</Td>
                <Td>{b.engine}</Td>
                <Td mono>{b.status === 'Succeeded' ? formatSize(b.sizeBytes) : '—'}</Td>
                <Td mono>{formatDuration(b.createdAt, b.completedAt) ?? '—'}</Td>
                <Td>
                  {b.status === 'Succeeded' ? (
                    <Badge tone="success" icon={CheckCircle2}>
                      Done
                    </Badge>
                  ) : b.status === 'Running' ? (
                    <Badge tone="accent" icon={Clock}>
                      Running…
                    </Badge>
                  ) : (
                    <Badge tone="danger" icon={XCircle} title={b.error ?? undefined}>
                      Failed
                    </Badge>
                  )}
                </Td>
                <Td>
                  {b.verifiedAt ? (
                    <Badge tone="success" icon={ShieldCheck} title={`Verified ${formatWhen(b.verifiedAt)}`}>
                      Verified
                    </Badge>
                  ) : b.verifyError ? (
                    <Badge tone="danger" icon={XCircle} title={b.verifyError}>
                      Broken
                    </Badge>
                  ) : (
                    <span className="text-content-subtle">Not verified</span>
                  )}
                </Td>
                <Td align="right">
                  {b.status === 'Succeeded' &&
                    (deletingId === b.id ? (
                      <div className="inline-flex items-center gap-1.5">
                        <span className="text-[12px] text-content-secondary">Delete permanently?</span>
                        <Button
                          size="sm"
                          variant="danger"
                          busy={rowBusyId === b.id}
                          onClick={() => handleDelete(b)}
                        >
                          Delete
                        </Button>
                        <Button size="sm" variant="ghost" onClick={() => setDeletingId(null)}>
                          Cancel
                        </Button>
                      </div>
                    ) : (
                      <div className="inline-flex items-center gap-1">
                        <Button
                          size="sm"
                          variant="ghost"
                          icon={ShieldCheck}
                          aria-label={`Verify the backup from ${formatWhen(b.createdAt)}`}
                          title="Verify: restore into a throwaway server"
                          busy={rowBusyId === b.id}
                          disabled={busy}
                          onClick={() => handleVerify(b)}
                        />
                        <Button
                          size="sm"
                          variant="ghost"
                          icon={Download}
                          aria-label={`Download the backup from ${formatWhen(b.createdAt)}`}
                          title="Download (encrypted)"
                          disabled={busy || rowBusyId === b.id}
                          onClick={() => handleDownload(b)}
                        />
                        <Button
                          size="sm"
                          variant="ghost"
                          icon={RotateCcw}
                          aria-label={`Restore the backup from ${formatWhen(b.createdAt)}`}
                          title="Restore"
                          disabled={busy}
                          onClick={() => setConfirming({ backup: b, typed: '' })}
                        />
                        <Button
                          size="sm"
                          variant="ghost"
                          icon={Trash2}
                          aria-label={`Delete the backup from ${formatWhen(b.createdAt)}`}
                          title="Delete"
                          disabled={busy}
                          onClick={() => setDeletingId(b.id)}
                        />
                      </div>
                    ))}
                </Td>
              </tr>
            ))}
          </DataTable>
        )}
      </Card>

      {restores.length > 0 && (
        <Card title="Restore history" flush>
          <DataTable
            caption="Restore history"
            columns={[
              { label: 'Date' },
              { label: 'Source backup' },
              { label: 'Duration' },
              { label: 'Status' },
            ]}
          >
            {restores.map((r) => (
              <tr key={r.id} className="hover:bg-surface-700/30 transition-colors">
                <Td>{formatWhen(r.startedAt)}</Td>
                <Td mono>{r.backupId.slice(0, 8)}</Td>
                <Td mono>{formatDuration(r.startedAt, r.completedAt) ?? '—'}</Td>
                <Td>
                  {r.status === 'Succeeded' ? (
                    <Badge tone="success" icon={CheckCircle2}>
                      Done
                    </Badge>
                  ) : r.status === 'Running' ? (
                    <Badge tone="accent" icon={Clock}>
                      Running…
                    </Badge>
                  ) : (
                    <Badge tone="danger" icon={XCircle} title={r.error ?? undefined}>
                      Failed
                    </Badge>
                  )}
                </Td>
              </tr>
            ))}
          </DataTable>
        </Card>
      )}
    </div>
  );
}
