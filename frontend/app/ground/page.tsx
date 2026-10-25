'use client';

import { Suspense, useCallback, useEffect, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import Link from 'next/link';
import {
  Database,
  Plus,
  HardDrive,
  Loader2,
  CheckCircle2,
  AlertTriangle,
  Trash2,
  Undo2,
  Server,
  Cable,
  Lock,
  ArrowLeft,
  ArrowUpRight,
} from 'lucide-react';
import { authService } from '../../services/api';
import { useAuthStore } from '../../store/useAuthStore';
import { useAuthModalStore } from '../../store/useAuthModalStore';
import { useProjectHistoryStore } from '../../store/useProjectHistoryStore';
import { useActiveProjectStore } from '../../store/useActiveProjectStore';
import { ModuleSymbol, ModuleWordmark } from '../../components/layout/ModuleLogo';
import type { CloudProjectDto } from '../../types/api';
import {
  groundApi,
  VaultGroundError,
  daysUntilPurge,
  formatSize,
  type GroundDatabase,
  type GroundProvider,
  type GroundMetrics,
} from '../../services/vaultGroundApi';
import {
  PageShell,
  PageHeader,
  Card,
  Callout,
  Badge,
  Button,
  EmptyState,
  DefinitionGrid,
  StatTile,
  Field,
  inputClass,
  DataTable,
  Td,
} from '../../components/workspace/WorkspaceKit';

const STATUS: Record<
  GroundDatabase['status'],
  { label: string; tone: 'success' | 'neutral' | 'danger' | 'accent' }
> = {
  Provisioning: { label: 'Creating…', tone: 'accent' },
  Active: { label: 'Active', tone: 'success' },
  PendingDelete: { label: 'Pending deletion', tone: 'danger' },
  Deleted: { label: 'Deleted', tone: 'neutral' },
  Failed: { label: 'Failed', tone: 'danger' },
};

export default function GroundPage() {
  return (
    <Suspense
      fallback={
        <PageShell>
          <Card>
            <div className="flex items-center gap-2 px-5 py-8 text-[13px] text-content-muted">
              <Loader2 className="w-4 h-4 animate-spin" aria-hidden="true" /> Loading Ground…
            </div>
          </Card>
        </PageShell>
      }
    >
      <GroundPageContent />
    </Suspense>
  );
}

/**
 * Faz 5 — Namines Ground:
 * 1. `/ground?project=<id>` (Projeye Kilitli Oturum):
 *    Ekmek kırıntısı: `ground / {owner} / {projectName}` formatındadır.
 *    `ground` linkine tıklandığında genel konsola dönülür.
 * 2. `/ground` (Parametresiz Genel Yönetim Düzlemi):
 *    Tüm projelerin veritabanı filosu, sağlayıcılar ve yeni DB oluşturma merkezi.
 */
function GroundPageContent() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const projectParam = searchParams.get('project');

  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);
  const currentUser = useAuthStore((s) => s.user);
  const openAuthModal = useAuthModalStore((s) => s.open);
  const localProjects = useProjectHistoryStore((s) => s.projects);
  const { scopedProjectId, scopedProjectName, scopedHubRepo, setScopedProject, clearProjectSession } =
    useActiveProjectStore();

  const [projects, setProjects] = useState<CloudProjectDto[] | null>(null);
  const [providers, setProviders] = useState<GroundProvider[] | null>(null);
  const [fleetDbs, setFleetDbs] = useState<Record<string, GroundDatabase | null>>({});
  const [creating, setCreating] = useState(false);
  const [showCreate, setShowCreate] = useState(false);
  const [newName, setNewName] = useState('');

  const loadProjects = useCallback(async () => {
    if (!isAuthenticated) return;
    try {
      const [list, provList] = await Promise.all([
        authService.getCloudProjects(),
        groundApi.providers().catch(() => []),
      ]);
      setProjects(list);
      setProviders(provList);

      const dbEntries = await Promise.all(
        list.map(async (p) => {
          try {
            const rec = await groundApi.get(p.id);
            return [p.id, rec] as const;
          } catch {
            return [p.id, null] as const;
          }
        }),
      );
      setFleetDbs(Object.fromEntries(dbEntries));
    } catch {
      setProjects([]);
      setProviders([]);
    }
  }, [isAuthenticated]);

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
      setScopedProject(projectParam, resolvedName);
      useProjectHistoryStore.getState().setActiveProjectId(projectParam);
    } else {
      clearProjectSession();
    }
  }, [projectParam, projects, localProjects, scopedProjectName, scopedHubRepo, setScopedProject, clearProjectSession]);

  async function handleCreate() {
    const name = newName.trim();
    if (!name) return;
    setCreating(true);
    try {
      const id = crypto.randomUUID();
      await authService.syncProjects([
        {
          id,
          name,
          dbType: 'PostgreSQL',
          schemaJson: '{}',
          nodePositionsJson: '{}',
        },
      ]);
      setNewName('');
      setShowCreate(false);
      await loadProjects();
      setScopedProject(id, name);
      router.push(`/ground?project=${encodeURIComponent(id)}`);
    } finally {
      setCreating(false);
    }
  }

  if (!isAuthenticated) {
    return (
      <PageShell width="max-w-3xl">
        <EmptyState
          icon={Database}
          title="Sign in to use Namines Ground"
          description="You need an account to create and manage hosted databases."
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
  // MODE A: PROJECT-SCOPED GROUND (/ground?project=<id>)
  // ─────────────────────────────────────────────────────────────────────────
  if (projectParam) {
    const lockedCloud = projects?.find((p) => p.id === projectParam) ?? null;
    const lockedLocal = localProjects.find((p) => p.id === projectParam) ?? null;
    const projectName =
      scopedProjectName || lockedCloud?.name || lockedLocal?.name || scopedHubRepo?.name || projectParam;
    const resolvedOwner = scopedHubRepo?.owner || currentUser?.username || 'workspace';

    return (
      <PageShell>
        {/* Top Breadcrumb (Matching Hub format: ground / owner / ProjectName) */}
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 pb-3 border-b border-line">
          <div className="flex items-center flex-wrap gap-2 text-sm sm:text-base">
            <ModuleSymbol module="ground" size={20} />
            <Link
              href="/ground"
              onClick={() => clearProjectSession()}
              className="group inline-flex items-center text-content-muted hover:text-content-primary transition-colors cursor-pointer"
            >
              <ModuleWordmark module="ground" height={13} className="text-content-muted group-hover:text-content-primary transition-colors" />
            </Link>
            <span className="text-content-muted">/</span>
            <span className="text-content-secondary font-mono">{resolvedOwner}</span>
            <span className="text-content-muted">/</span>
            <h1 className="font-sans font-bold text-content-primary tracking-tight text-base sm:text-lg">
              {projectName}
            </h1>
          </div>
        </div>

        <GroundDetail key={projectParam} projectId={projectParam} projectName={projectName} />
      </PageShell>
    );
  }

  // ─────────────────────────────────────────────────────────────────────────
  // MODE B: GLOBAL GROUND FLEET CONSOLE (/ground without ?project=)
  // ─────────────────────────────────────────────────────────────────────────
  const totalProjects = projects?.length ?? 0;
  const activeDbs = Object.values(fleetDbs).filter(
    (d) => d && d.status !== 'Deleted',
  ).length;
  const readyProviders = (providers ?? []).filter((p) => !p.problem && p.liveVerified).length;

  return (
    <PageShell>
      <div id="ground-header" className="flex flex-wrap items-start justify-between gap-4">
        <PageHeader
          icon={HardDrive}
          title="Global Ground — Managed Database Fleet"
          description="Organization-wide view of all hosted databases, infrastructure providers, and sandbox instances across your projects."
        />

        <div className="self-center">
          {showCreate ? (
            <div className="flex flex-wrap items-end gap-2 bg-surface-800 border border-line p-3 rounded-[var(--radius-card)]">
              <div className="min-w-[200px]">
                <Field label="New database project name" htmlFor="ground-new-name">
                  <input
                    id="ground-new-name"
                    autoFocus
                    value={newName}
                    onChange={(e) => setNewName(e.target.value)}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter') void handleCreate();
                      if (e.key === 'Escape') setShowCreate(false);
                    }}
                    placeholder="e.g. billing_ledger"
                    className={inputClass}
                  />
                </Field>
              </div>
              <Button variant="primary" busy={creating} disabled={!newName.trim()} onClick={handleCreate}>
                Create
              </Button>
              <Button
                variant="ghost"
                onClick={() => {
                  setShowCreate(false);
                  setNewName('');
                }}
              >
                Cancel
              </Button>
            </div>
          ) : (
            <Button id="ground-new-db-btn" variant="primary" icon={Plus} onClick={() => setShowCreate(true)}>
              New database
            </Button>
          )}
        </div>
      </div>

      {/* Global Fleet Metrics */}
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-3">
        <StatTile
          label="Total Projects"
          value={projects ? totalProjects : '—'}
          hint="Cloud workspace records"
        />
        <StatTile
          label="Active Managed DBs"
          value={projects ? activeDbs : '—'}
          hint={`${projects?.filter((p) => p.hasConnection).length ?? 0} total connected`}
        />
        <StatTile
          label="Registered Providers"
          value={providers ? providers.length : '—'}
          hint={`${readyProviders} verified ready`}
        />
        <StatTile label="Deletion Safety" value="7-day grace" hint="Automatic undo window" />
      </div>

      <Card
        title="All Projects — Database Fleet"
        description="Select any project to provision a managed database, inspect storage and connection metrics, or manage its lifecycle."
        flush
      >
        {!projects ? (
          <div className="flex items-center gap-2 px-5 py-8 text-[13px] text-content-muted">
            <Loader2 className="w-4 h-4 animate-spin" aria-hidden="true" /> Loading database fleet…
          </div>
        ) : projects.length === 0 ? (
          <EmptyState
            icon={Database}
            title="No projects yet"
            description="Create your first database project to provision a hosted instance."
            action={
              <Button variant="primary" icon={Plus} onClick={() => setShowCreate(true)}>
                New database
              </Button>
            }
          />
        ) : (
          <DataTable
            caption="All project databases in Ground"
            columns={[
              { label: 'Project' },
              { label: 'Ground Status' },
              { label: 'Provider' },
              { label: 'Region' },
              { label: 'Created' },
              { label: 'Actions', align: 'right' },
            ]}
          >
            {projects.map((p) => {
              const db = fleetDbs[p.id];
              const hasActiveDb = db && db.status !== 'Deleted';
              const st = db ? STATUS[db.status] : null;

              return (
                <tr key={p.id} className="hover:bg-surface-700/30 transition-colors">
                  <Td>
                    <div className="flex items-center gap-2">
                      <HardDrive className="w-3.5 h-3.5 text-accent-text shrink-0" />
                      <span className="font-semibold text-content-primary">{p.name}</span>
                    </div>
                  </Td>
                  <Td>
                    {hasActiveDb && st ? (
                      <Badge tone={st.tone} icon={db.status === 'Active' ? CheckCircle2 : undefined}>
                        {st.label}
                      </Badge>
                    ) : p.hasConnection ? (
                      <Badge tone="success" icon={CheckCircle2}>
                        External ({p.connectionDbType ?? 'DB'})
                      </Badge>
                    ) : (
                      <Badge tone="neutral">Not provisioned</Badge>
                    )}
                  </Td>
                  <Td mono>{hasActiveDb ? db.provider : '—'}</Td>
                  <Td>{hasActiveDb ? (db.region ?? 'Default') : '—'}</Td>
                  <Td>
                    {hasActiveDb
                      ? new Date(db.createdAt).toLocaleString('en-US', {
                          dateStyle: 'medium',
                          timeStyle: 'short',
                        })
                      : '—'}
                  </Td>
                  <Td align="right">
                    <Button
                      size="sm"
                      variant="secondary"
                      icon={ArrowUpRight}
                      onClick={() => {
                        setScopedProject(p.id, p.name);
                        router.push(`/ground?project=${encodeURIComponent(p.id)}`);
                      }}
                    >
                      {hasActiveDb ? 'Manage Database' : 'Provision Database'}
                    </Button>
                  </Td>
                </tr>
              );
            })}
          </DataTable>
        )}
      </Card>

      {providers && providers.length > 0 && (
        <Card
          id="ground-providers-card"
          title="Infrastructure Providers"
          description="Registered database provisioning backends available across your organization."
          flush
        >
          <ul className="divide-y divide-line border-t border-line">
            {providers.map((p) => (
              <li
                key={p.name}
                className="flex flex-wrap items-center justify-between gap-4 px-5 py-3.5 hover:bg-surface-700/30 transition-colors"
              >
                <div className="min-w-0">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="text-xs font-semibold text-content-primary">{p.name}</span>
                    {p.problem ? (
                      <Badge tone="neutral" icon={AlertTriangle} title={p.problem}>
                        Not configured
                      </Badge>
                    ) : p.liveVerified ? (
                      <Badge tone="success" icon={CheckCircle2}>
                        Ready
                      </Badge>
                    ) : (
                      <Badge tone="neutral">Unverified</Badge>
                    )}
                  </div>
                  <p className="mt-1 text-xs leading-relaxed text-content-muted max-w-xl">
                    {p.responsibility}
                  </p>
                </div>
              </li>
            ))}
          </ul>
        </Card>
      )}
    </PageShell>
  );
}

function GroundDetail({ projectId, projectName }: { projectId: string; projectName: string }) {
  const [providers, setProviders] = useState<GroundProvider[] | null>(null);
  const [database, setDatabase] = useState<GroundDatabase | null>(null);
  const [metrics, setMetrics] = useState<GroundMetrics | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [confirmText, setConfirmText] = useState('');

  const reload = useCallback(async () => {
    try {
      const [list, record] = await Promise.all([groundApi.providers(), groundApi.get(projectId)]);
      setProviders(list);
      setDatabase(record);
      setError(null);
      if (record && record.status !== 'Deleted') {
        try {
          setMetrics(await groundApi.metrics(projectId));
        } catch {
          setMetrics(null);
        }
      } else {
        setMetrics(null);
      }
    } catch (err) {
      setError(err instanceof VaultGroundError ? err.message : 'Could not load the managed database.');
      setProviders([]);
    }
  }, [projectId]);

  useEffect(() => {
    reload();
  }, [reload]);

  async function run(action: () => Promise<unknown>, success: string) {
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      await action();
      setNotice(success);
      await reload();
    } catch (err) {
      setError(err instanceof VaultGroundError ? err.message : 'The action failed.');
    } finally {
      setBusy(false);
    }
  }

  const handleProvision = (provider: string) =>
    run(() => groundApi.provision(projectId, provider), 'Managed database created.');
  const handleDelete = () =>
    run(async () => {
      await groundApi.requestDelete(projectId, confirmText);
      setConfirming(false);
      setConfirmText('');
    }, 'Deletion requested. You can still undo it during the grace window.');
  const handleCancelDelete = () =>
    run(() => groundApi.cancelDelete(projectId), 'Deletion cancelled — the database is back in use.');

  const remainingDays = database?.deleteRequestedAt
    ? daysUntilPurge(database.deleteRequestedAt, database.graceDays)
    : null;

  const hasDatabase = !!database && database.status !== 'Deleted';
  const status = database ? STATUS[database.status] : null;

  return (
    <div className="space-y-5">
      {error && <Callout tone="danger">{error}</Callout>}
      {notice && <Callout tone="success">{notice}</Callout>}
      {metrics?.storageWarning && <Callout tone="neutral">{metrics.storageWarning}</Callout>}

      {hasDatabase && database && status && (
        <>
          <div className="grid grid-cols-2 lg:grid-cols-4 gap-3">
            <StatTile
              label="Status"
              value={
                <Badge tone={status.tone} icon={database.status === 'Active' ? CheckCircle2 : undefined}>
                  {status.label}
                </Badge>
              }
            />
            <StatTile label="Storage used" value={formatSize(metrics?.storageBytes ?? null)} />
            <StatTile label="Active connections" value={metrics?.activeConnections ?? '—'} />
            <StatTile label="Provider" value={database.provider} />
          </div>

          <Card
            id="ground-database-card"
            title={projectName}
            description="Where this database lives and when it was created."
          >
            <DefinitionGrid
              items={[
                { label: 'Database ID', value: database.providerProjectId ?? '—', mono: true },
                { label: 'Region', value: database.region ?? '—' },
                {
                  label: 'Created',
                  value: new Date(database.createdAt).toLocaleString('en-US', {
                    dateStyle: 'medium',
                    timeStyle: 'short',
                  }),
                },
                ...(database.error
                  ? [{ label: 'Last error', value: <span className="text-danger-text">{database.error}</span> }]
                  : []),
              ]}
            />
          </Card>
        </>
      )}

      {database?.status === 'PendingDelete' && (
        <Callout
          tone="danger"
          title="This database is scheduled for deletion"
          action={
            <Button icon={Undo2} busy={busy} onClick={handleCancelDelete}>
              Undo deletion
            </Button>
          }
        >
          {remainingDays === 0
            ? 'The grace window has ended; it will be permanently deleted on the next cleanup pass.'
            : `${remainingDays} day${remainingDays === 1 ? '' : 's'} left before it is permanently deleted. You can still undo it.`}
        </Callout>
      )}

      {database?.status === 'Active' && (
        <section
          id="ground-danger-zone"
          className="rounded-[var(--radius-card)] border border-danger/35 bg-surface-800 overflow-hidden shadow-xs"
        >
          <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 px-5 py-4 bg-danger-subtle/15">
            <div className="flex items-start gap-3.5 min-w-0">
              <div className="w-9 h-9 rounded-[var(--radius-control)] bg-danger-subtle border border-danger/30 flex items-center justify-center text-danger-text shrink-0 mt-0.5">
                <Trash2 className="w-4 h-4" aria-hidden="true" />
              </div>
              <div className="min-w-0">
                <div className="flex flex-wrap items-center gap-2">
                  <h2 className="text-xs font-bold uppercase tracking-wider text-content-primary">
                    Delete this database
                  </h2>
                  <span className="inline-flex items-center px-2 py-0.5 rounded-full text-[10px] font-mono font-semibold bg-danger-subtle text-danger-text border border-danger/30">
                    {database.graceDays || 7}-day grace window
                  </span>
                </div>
                <p className="mt-1 text-xs leading-relaxed text-content-secondary max-w-xl">
                  Removes the database and everything in it after a grace period. Take a Vault backup first if you may need the data.
                </p>
              </div>
            </div>
            {!confirming && (
              <div className="shrink-0 self-start sm:self-center">
                <Button variant="danger" icon={Trash2} onClick={() => setConfirming(true)}>
                  Delete database
                </Button>
              </div>
            )}
          </div>

          {confirming && (
            <div className="border-t border-danger/25 bg-surface-900/70 px-5 py-4">
              <div className="max-w-lg space-y-3.5">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <label htmlFor="ground-confirm-name" className="text-xs font-semibold text-content-primary">
                    Type the project name to confirm deletion:
                  </label>
                  <button
                    type="button"
                    onClick={() => setConfirmText(projectName)}
                    title="Click to fill project name"
                    className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-[var(--radius-control)] bg-surface-800 hover:bg-surface-700 border border-line-strong text-[11px] font-mono text-content-primary cursor-pointer transition-colors"
                  >
                    <span>{projectName}</span>
                  </button>
                </div>
                <input
                  id="ground-confirm-name"
                  autoFocus
                  value={confirmText}
                  onChange={(e) => setConfirmText(e.target.value)}
                  className={inputClass}
                  placeholder={projectName}
                />
                <div className="flex flex-wrap items-center gap-2 pt-0.5">
                  <Button
                    variant="danger"
                    icon={Trash2}
                    disabled={confirmText.trim() !== projectName}
                    busy={busy}
                    onClick={handleDelete}
                  >
                    Start deletion
                  </Button>
                  <Button
                    variant="ghost"
                    onClick={() => {
                      setConfirming(false);
                      setConfirmText('');
                    }}
                  >
                    Cancel
                  </Button>
                </div>
              </div>
            </div>
          )}
        </section>
      )}

      {(!database || database.status === 'Deleted') && (
        <Card
          id="ground-providers-card"
          title="Choose a provider"
          description="Pick where this project's database should be created."
          flush
        >
          {!providers ? (
            <div className="flex items-center gap-2 px-5 pb-5 text-[13px] text-content-muted">
              <Loader2 className="w-4 h-4 animate-spin" aria-hidden="true" /> Loading providers…
            </div>
          ) : providers.length === 0 ? (
            <EmptyState
              icon={Server}
              title="No providers registered"
              description="No provider is configured on the server."
            />
          ) : (
            <ul className="divide-y divide-line border-t border-line">
              {providers.map((p) => (
                <li
                  key={p.name}
                  className="flex flex-wrap items-center justify-between gap-4 px-5 py-4 hover:bg-surface-700/30 transition-colors"
                >
                  <div className="min-w-0">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="text-sm font-semibold text-content-primary">{p.name}</span>
                      {p.problem ? (
                        <Badge tone="neutral" icon={AlertTriangle} title={p.problem}>
                          Not configured
                        </Badge>
                      ) : p.liveVerified ? (
                        <Badge tone="success" icon={CheckCircle2}>
                          Ready
                        </Badge>
                      ) : (
                        <Badge tone="neutral">Unverified</Badge>
                      )}
                    </div>
                    <p className="mt-1 text-[12px] leading-relaxed text-content-muted max-w-xl">
                      {p.responsibility}
                    </p>
                    {p.problem && (
                      <p className="mt-1 text-[12px] text-content-secondary flex items-center gap-1.5">
                        <Cable className="w-3 h-3" aria-hidden="true" />
                        {p.problem}
                      </p>
                    )}
                  </div>
                  <Button
                    variant="primary"
                    disabled={busy || p.problem !== null}
                    busy={busy}
                    onClick={() => handleProvision(p.name)}
                  >
                    Use this provider
                  </Button>
                </li>
              ))}
            </ul>
          )}
        </Card>
      )}
    </div>
  );
}
