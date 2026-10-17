'use client';

import { useCallback, useEffect, useState } from 'react';
import {
  Database, Plus, HardDrive, Loader2, CheckCircle2, AlertTriangle, Trash2, Undo2, Server, Cable,
} from 'lucide-react';
import { authService } from '../../services/api';
import { useAuthStore } from '../../store/useAuthStore';
import { useAuthModalStore } from '../../store/useAuthModalStore';
import { useProjectHistoryStore } from '../../store/useProjectHistoryStore';
import type { CloudProjectDto } from '../../types/api';
import {
  groundApi, VaultGroundError, daysUntilPurge, formatSize,
  type GroundDatabase, type GroundProvider, type GroundMetrics,
} from '../../services/vaultGroundApi';
import ProjectPicker from '../../components/workspace/ProjectPicker';
import {
  PageShell, PageHeader, Card, Callout, Badge, Button, EmptyState, DefinitionGrid, StatTile, Field, inputClass,
} from '../../components/workspace/WorkspaceKit';

const STATUS: Record<GroundDatabase['status'], { label: string; tone: 'success' | 'neutral' | 'danger' | 'accent' }> = {
  Provisioning: { label: 'Creating…', tone: 'accent' },
  Active: { label: 'Active', tone: 'success' },
  PendingDelete: { label: 'Pending deletion', tone: 'danger' },
  Deleted: { label: 'Deleted', tone: 'neutral' },
  Failed: { label: 'Failed', tone: 'danger' },
};

/**
 * Namines Ground — bağımsız sayfa.
 *
 * Desk'e bağlı değil: kullanıcı şema tasarlamadan ya da Desk'in CRUD/SQL konsolunu
 * açmadan barındırılan bir veritabanı isteyebilir. Doğrudan `/api/ground` ve
 * `/api/auth` ile konuşur — buradaki "proje" Ground'un ihtiyaç duyduğu bir kayıt,
 * şema değil (boş `schemaJson: "{}"` ile açılır).
 */
export default function GroundPage() {
  const isAuthenticated = useAuthStore(s => s.isAuthenticated);
  const openAuthModal = useAuthModalStore(s => s.open);
  const activeProjectId = useProjectHistoryStore(s => s.activeProjectId);

  const [projects, setProjects] = useState<CloudProjectDto[] | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const [showCreate, setShowCreate] = useState(false);
  const [newName, setNewName] = useState('');

  const loadProjects = useCallback(async () => {
    if (!isAuthenticated) return;
    const list = await authService.getCloudProjects();
    setProjects(list);
    // Üst gezinmedeki aktif proje varsa onu seç: kullanıcı zaten onun içinde çalışıyor.
    setSelectedId(prev => prev
      ?? list.find(p => p.id === activeProjectId)?.id
      ?? list[0]?.id
      ?? null);
  }, [isAuthenticated, activeProjectId]);

  useEffect(() => { loadProjects(); }, [loadProjects]);

  async function handleCreate() {
    const name = newName.trim();
    if (!name) return;
    setCreating(true);
    try {
      const id = crypto.randomUUID();
      await authService.syncProjects([{
        id, name, dbType: 'PostgreSQL', schemaJson: '{}', nodePositionsJson: '{}',
      }]);
      setNewName('');
      setShowCreate(false);
      await loadProjects();
      setSelectedId(id);
    } finally {
      setCreating(false);
    }
  }

  if (!isAuthenticated) {
    return (
      <PageShell width="max-w-3xl">
        <EmptyState icon={Database} title="Sign in to use Namines Ground"
          description="You need an account to create and manage a hosted database."
          action={<Button variant="primary" onClick={openAuthModal}>Log in / Sign up</Button>} />
      </PageShell>
    );
  }

  const selected = projects?.find(p => p.id === selectedId) ?? null;

  return (
    <PageShell>
      <PageHeader
        icon={HardDrive}
        title="Ground"
        description="Hosted databases for your projects — create one, see how it is doing, and delete it safely. No Desk required."
      />

      <ProjectPicker
        projects={projects}
        selectedId={selectedId}
        onSelect={setSelectedId}
        describe={p => (p.hasConnection ? p.connectionDbType ?? 'connected' : 'no database yet')}
      >
        {showCreate ? (
          <div className="flex flex-wrap items-end gap-2 flex-1 w-full sm:w-auto sm:min-w-[260px]">
            <div className="flex-1 min-w-[180px]">
              <Field label="New database name" htmlFor="ground-new-name">
                <input
                  id="ground-new-name"
                  autoFocus
                  value={newName}
                  onChange={e => setNewName(e.target.value)}
                  onKeyDown={e => { if (e.key === 'Enter') void handleCreate(); if (e.key === 'Escape') setShowCreate(false); }}
                  placeholder="e.g. blog_platform"
                  className={inputClass}
                />
              </Field>
            </div>
            <Button variant="primary" busy={creating} disabled={!newName.trim()} onClick={handleCreate}>Create</Button>
            <Button variant="ghost" onClick={() => { setShowCreate(false); setNewName(''); }}>Cancel</Button>
          </div>
        ) : (
          <Button icon={Plus} onClick={() => setShowCreate(true)}>New database</Button>
        )}
      </ProjectPicker>

      {selected ? (
        <GroundDetail key={selected.id} projectId={selected.id} projectName={selected.name} />
      ) : !projects ? (
        <Card>
          <div className="flex items-center gap-2 px-5 py-8 text-[13px] text-content-muted">
            <Loader2 className="w-4 h-4 animate-spin" aria-hidden="true" /> Loading your projects…
          </div>
        </Card>
      ) : (
        <Card>
          <EmptyState icon={Database} title={projects.length === 0 ? 'No projects yet' : 'Select a project'}
            description={projects.length === 0
              ? 'Create your first database to get started.'
              : 'Pick a project above, or create a new database.'}
            action={projects.length === 0
              ? <Button variant="primary" icon={Plus} onClick={() => setShowCreate(true)}>New database</Button>
              : undefined} />
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
        try { setMetrics(await groundApi.metrics(projectId)); } catch { setMetrics(null); }
      } else {
        setMetrics(null);
      }
    } catch (err) {
      setError(err instanceof VaultGroundError ? err.message : 'Could not load the managed database.');
      setProviders([]);
    }
  }, [projectId]);

  useEffect(() => { reload(); }, [reload]);

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
    ? daysUntilPurge(database.deleteRequestedAt, database.graceDays) : null;

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
            <StatTile label="Status" value={<Badge tone={status.tone} icon={database.status === 'Active' ? CheckCircle2 : undefined}>{status.label}</Badge>} />
            <StatTile label="Storage used" value={formatSize(metrics?.storageBytes ?? null)} />
            <StatTile label="Active connections" value={metrics?.activeConnections ?? '—'} />
            <StatTile label="Provider" value={database.provider} />
          </div>

          <Card title={projectName} description="Where this database lives and when it was created.">
            <DefinitionGrid
              items={[
                { label: 'Database ID', value: database.providerProjectId ?? '—', mono: true },
                { label: 'Region', value: database.region ?? '—' },
                { label: 'Created', value: new Date(database.createdAt).toLocaleString('en-US', { dateStyle: 'medium', timeStyle: 'short' }) },
                ...(database.error ? [{ label: 'Last error', value: <span className="text-danger-text">{database.error}</span> }] : []),
              ]}
            />
          </Card>
        </>
      )}

      {database?.status === 'PendingDelete' && (
        <Callout
          tone="danger"
          title="This database is scheduled for deletion"
          action={<Button icon={Undo2} busy={busy} onClick={handleCancelDelete}>Undo deletion</Button>}
        >
          {remainingDays === 0
            ? 'The grace window has ended; it will be permanently deleted on the next cleanup pass.'
            : `${remainingDays} day${remainingDays === 1 ? '' : 's'} left before it is permanently deleted. You can still undo it.`}
        </Callout>
      )}

      {database?.status === 'Active' && (
        <section className="rounded-[var(--radius-card)] border border-danger/35 bg-surface-800 overflow-hidden shadow-xs">
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
                  onChange={e => setConfirmText(e.target.value)}
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
                  <Button variant="ghost" onClick={() => { setConfirming(false); setConfirmText(''); }}>
                    Cancel
                  </Button>
                </div>
              </div>
            </div>
          )}
        </section>
      )}

      {(!database || database.status === 'Deleted') && (
        <Card title="Choose a provider" description="Pick where this project's database should be created." flush>
          {!providers ? (
            <div className="flex items-center gap-2 px-5 pb-5 text-[13px] text-content-muted">
              <Loader2 className="w-4 h-4 animate-spin" aria-hidden="true" /> Loading providers…
            </div>
          ) : providers.length === 0 ? (
            <EmptyState icon={Server} title="No providers registered" description="No provider is configured on the server." />
          ) : (
            <ul className="divide-y divide-line border-t border-line">
              {providers.map(p => (
                <li key={p.name} className="flex flex-wrap items-center justify-between gap-4 px-5 py-4 hover:bg-surface-700/30 transition-colors">
                  <div className="min-w-0">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="text-sm font-semibold text-content-primary">{p.name}</span>
                      {p.problem ? (
                        <Badge tone="neutral" icon={AlertTriangle} title={p.problem}>Not configured</Badge>
                      ) : p.liveVerified ? (
                        <Badge tone="success" icon={CheckCircle2}>Ready</Badge>
                      ) : (
                        <Badge tone="neutral">Unverified</Badge>
                      )}
                    </div>
                    <p className="mt-1 text-[12px] leading-relaxed text-content-muted max-w-xl">{p.responsibility}</p>
                    {p.problem && <p className="mt-1 text-[12px] text-content-secondary flex items-center gap-1.5"><Cable className="w-3 h-3" aria-hidden="true" />{p.problem}</p>}
                  </div>
                  <Button variant="primary" disabled={busy || p.problem !== null} busy={busy} onClick={() => handleProvision(p.name)}>
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
