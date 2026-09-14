'use client';

import { useCallback, useEffect, useState, type ReactNode } from 'react';
import { useRouter } from 'next/navigation';
import { ArrowLeft, Database, Plus, Loader2, HardDrive, Cable } from 'lucide-react';
import { authService } from '../../services/api';
import { useAuthStore } from '../../store/useAuthStore';
import { useAuthModalStore } from '../../store/useAuthModalStore';
import type { CloudProjectDto } from '../../types/api';
import {
  groundApi, VaultGroundError, daysUntilPurge, formatSize,
  type GroundDatabase, type GroundProvider, type GroundMetrics,
} from '../../services/vaultGroundApi';
import { Panel, PanelBar, ActionButton, PanelEmpty } from '../../components/compile/PanelKit';

const STATUS_LABELS: Record<GroundDatabase['status'], string> = {
  Provisioning: 'Creating…',
  Active: 'Active',
  PendingDelete: 'Pending deletion',
  Deleted: 'Deleted',
  Failed: 'Failed',
};

/**
 * Namines Ground — standalone page.
 *
 * Not tied to Desk: a user may want a hosted database without ever
 * designing a schema or opening Desk's CRUD/SQL console. Talks directly to
 * `/api/ground` and `/api/auth` — "project" here is just a record Ground
 * needs, not a schema (opened with an empty `schemaJson: "{}"`).
 */
export default function GroundPage() {
  const router = useRouter();
  const { isAuthenticated } = useAuthStore();
  const openAuthModal = useAuthModalStore(s => s.open);

  const [projects, setProjects] = useState<CloudProjectDto[] | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const [newName, setNewName] = useState('');

  const loadProjects = useCallback(async () => {
    if (!isAuthenticated) return;
    const list = await authService.getCloudProjects();
    setProjects(list);
    setSelectedId(prev => prev ?? list[0]?.id ?? null);
  }, [isAuthenticated]);

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
      await loadProjects();
      setSelectedId(id);
    } finally {
      setCreating(false);
    }
  }

  if (!isAuthenticated) {
    return (
      <div className="h-[calc(100vh-56px)] bg-surface-900 flex items-center justify-center">
        <PanelEmpty icon={Database} title="Sign in to use Namines Ground"
          hint="You need an account to create and manage a hosted database.">
          <ActionButton tone="primary" onClick={openAuthModal}>Log in / Sign up</ActionButton>
        </PanelEmpty>
      </div>
    );
  }

  const selected = projects?.find(p => p.id === selectedId) ?? null;

  return (
    <div className="h-[calc(100vh-56px)] bg-surface-900 text-content-primary flex flex-col lg:flex-row font-sans overflow-hidden">
      <aside className="shrink-0 bg-surface-800 border-b lg:border-b-0 lg:border-r border-surface-500 flex flex-col lg:w-64">
        <div className="p-2.5">
          <button
            onClick={() => router.push('/')}
            className="flex items-center gap-1.5 w-full px-2 py-1.5 rounded-[var(--radius-control)] text-content-muted hover:text-content-primary hover:bg-white/[0.06] transition-colors cursor-pointer text-[11px] font-medium"
          >
            <ArrowLeft className="w-3 h-3" />
            <span>Back</span>
          </button>
        </div>
        <div className="px-3 pb-2.5 border-b border-surface-500">
          <h1 className="text-[13px] font-bold text-content-primary leading-tight flex items-center gap-1.5">
            <HardDrive className="w-3.5 h-3.5 text-accent-text" /> Namines Ground
          </h1>
          <p className="text-[10.5px] text-content-muted mt-1 leading-snug">
            Hosted databases — no Desk required.
          </p>
        </div>

        <div className="flex-1 min-h-0 overflow-y-auto p-2 space-y-0.5">
          {!projects ? (
            <div className="px-2 py-3 text-[11px] text-content-muted">Loading…</div>
          ) : projects.length === 0 ? (
            <div className="px-2 py-3 text-[11px] text-content-muted">No projects yet. Create a database below.</div>
          ) : (
            projects.map(p => (
              <button
                key={p.id}
                onClick={() => setSelectedId(p.id)}
                className={`w-full text-left px-2.5 py-2 rounded-[var(--radius-control)] text-[11px] transition-colors cursor-pointer ${
                  p.id === selectedId ? 'bg-accent-subtle text-accent-text' : 'text-content-secondary hover:bg-white/[0.04]'
                }`}
              >
                <div className="font-medium truncate">{p.name}</div>
                <div className="flex items-center gap-1 text-[10px] text-content-muted mt-0.5">
                  <Cable className="w-2.5 h-2.5" />
                  {p.hasConnection ? p.connectionDbType ?? 'Connected' : 'Not connected'}
                </div>
              </button>
            ))
          )}
        </div>

        <div className="p-2 border-t border-surface-500 space-y-1.5">
          <input
            value={newName}
            onChange={e => setNewName(e.target.value)}
            onKeyDown={e => e.key === 'Enter' && handleCreate()}
            placeholder="New database name…"
            className="w-full bg-surface-600 border border-surface-500 rounded-[var(--radius-control)] px-2.5 py-1.5 text-[11px] text-content-primary placeholder:text-content-muted focus:outline-none focus-visible:ring-1 focus-visible:ring-[var(--color-focus-ring)]"
          />
          <ActionButton icon={creating ? undefined : Plus} tone="primary" full busy={creating}
            disabled={!newName.trim()} onClick={handleCreate}>
            {creating ? 'Creating…' : 'New database'}
          </ActionButton>
        </div>
      </aside>

      <div className="flex-1 min-w-0 min-h-0 p-3">
        {selected ? (
          <GroundDetail key={selected.id} projectId={selected.id} projectName={selected.name} />
        ) : (
          <Panel>
            <PanelEmpty icon={Database} title="Select a project"
              hint="Pick a project on the left, or create a new database." />
          </Panel>
        )}
      </div>
    </div>
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

  return (
    <Panel scroll className="h-full">
      <PanelBar left={<span className="text-[12px] font-semibold text-content-primary">{projectName}</span>} />
      <div className="p-3 space-y-3">
        {error && <div className="px-3 py-2 rounded-[var(--radius-control)] bg-[var(--color-danger-subtle)] text-[var(--color-danger)] text-[11px]">{error}</div>}
        {notice && <div className="px-3 py-2 rounded-[var(--radius-control)] bg-accent-subtle text-accent-text text-[11px]">{notice}</div>}
        {metrics?.storageWarning && (
          <div className="px-3 py-2 rounded-[var(--radius-control)] bg-surface-600 text-content-secondary text-[11px]">{metrics.storageWarning}</div>
        )}

        {database && database.status !== 'Deleted' && (
          <div className="rounded-[var(--radius-control)] border border-surface-500 overflow-hidden">
            <table className="w-full text-[11px]">
              <tbody>
                <Row label="Status" value={STATUS_LABELS[database.status]} />
                <Row label="Provider" value={database.provider} />
                <Row label="Provider ID" value={<code>{database.providerProjectId ?? '—'}</code>} />
                <Row label="Region" value={database.region ?? '—'} />
                <Row label="Created" value={new Date(database.createdAt).toLocaleString('en-US')} />
                <Row label="Storage used" value={formatSize(metrics?.storageBytes ?? null)} />
                <Row label="Active connections" value={metrics?.activeConnections ?? '—'} />
                {database.error && <Row label="Error" value={database.error} />}
              </tbody>
            </table>
          </div>
        )}

        {database?.status === 'PendingDelete' && (
          <div className="p-3 rounded-[var(--radius-control)] bg-[var(--color-danger-subtle)] space-y-2">
            <p className="text-[11px] font-semibold text-[var(--color-danger)]">This database is scheduled for deletion.</p>
            <p className="text-[11px] text-content-secondary">
              {remainingDays === 0
                ? 'The grace window has ended; it will be permanently deleted on the next cleanup pass.'
                : `${remainingDays} day${remainingDays === 1 ? '' : 's'} left before it's permanently deleted. You can still undo it.`}
            </p>
            <ActionButton busy={busy} onClick={handleCancelDelete}>Undo deletion</ActionButton>
          </div>
        )}

        {database?.status === 'Active' && (
          confirming ? (
            <div className="p-3 rounded-[var(--radius-control)] bg-[var(--color-danger-subtle)] space-y-2">
              <p className="text-[11px] font-semibold text-[var(--color-danger)]">This database and everything in it will be deleted.</p>
              <p className="text-[11px] text-content-secondary">Type the project name to continue: <code>{projectName}</code></p>
              <input value={confirmText} onChange={e => setConfirmText(e.target.value)}
                className="w-full max-w-[280px] bg-surface-600 border border-surface-500 rounded-[var(--radius-control)] px-2.5 py-1.5 text-[11px] text-content-primary" />
              <div className="flex gap-2">
                <ActionButton tone="danger" disabled={!confirmText.trim()} busy={busy} onClick={handleDelete}>Start deletion</ActionButton>
                <ActionButton onClick={() => { setConfirming(false); setConfirmText(''); }}>Cancel</ActionButton>
              </div>
            </div>
          ) : (
            <ActionButton tone="danger" onClick={() => setConfirming(true)}>Delete database</ActionButton>
          )
        )}

        {(!database || database.status === 'Deleted') && (
          <>
            <p className="text-[11px] font-semibold text-content-muted uppercase tracking-wider">Providers</p>
            {!providers ? (
              <div className="flex items-center gap-2 text-[11px] text-content-muted"><Loader2 className="w-3.5 h-3.5 animate-spin" /> Loading…</div>
            ) : providers.length === 0 ? (
              <PanelEmpty icon={Database} title="No providers registered"
                hint="No provider is configured on the server." />
            ) : (
              <div className="space-y-2">
                {providers.map(p => (
                  <div key={p.name} className="flex items-center justify-between gap-3 p-3 rounded-[var(--radius-control)] bg-surface-600 border border-surface-500">
                    <div className="min-w-0">
                      <div className="flex items-center gap-2">
                        <span className="text-[12px] font-semibold text-content-primary">{p.name}</span>
                        <span className="text-[10px] font-bold uppercase tracking-wider px-1.5 py-0.5 rounded-full"
                          style={{
                            color: p.problem ? 'var(--color-content-muted)' : p.liveVerified ? 'var(--color-accent-text)' : 'var(--color-content-muted)',
                            background: p.problem ? 'transparent' : p.liveVerified ? 'var(--color-accent-subtle)' : 'transparent',
                          }}
                          title={p.problem ?? undefined}>
                          {p.problem ? 'Not configured' : p.liveVerified ? 'Ready' : 'Unverified'}
                        </span>
                      </div>
                      <p className="text-[10.5px] text-content-muted mt-0.5 leading-snug">{p.responsibility}</p>
                    </div>
                    <ActionButton tone="primary" disabled={busy || p.problem !== null} busy={busy}
                      onClick={() => handleProvision(p.name)}>
                      Use this provider
                    </ActionButton>
                  </div>
                ))}
              </div>
            )}
          </>
        )}
      </div>
    </Panel>
  );
}

function Row({ label, value }: { label: string; value: ReactNode }) {
  return (
    <tr className="border-b border-surface-500 last:border-0">
      <th className="text-left font-medium text-content-muted px-3 py-1.5 w-40 bg-surface-800">{label}</th>
      <td className="px-3 py-1.5 text-content-secondary">{value}</td>
    </tr>
  );
}
