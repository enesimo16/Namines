'use client';

import { useCallback, useEffect, useState, type ReactNode } from 'react';
import { useRouter } from 'next/navigation';
import { ArrowLeft, ShieldCheck, Cable, Loader2, Download, RotateCcw, Trash2, CheckCircle2 } from 'lucide-react';
import { authService } from '../../services/api';
import { useAuthStore } from '../../store/useAuthStore';
import { useAuthModalStore } from '../../store/useAuthModalStore';
import type { CloudProjectDto } from '../../types/api';
import {
  vaultApi, VaultGroundError, formatDuration, formatSize,
  type VaultBackup, type VaultRestore, type VaultSchedule, type VaultHealth,
} from '../../services/vaultGroundApi';
import { Panel, PanelBar, ActionButton, IconButton, PanelEmpty } from '../../components/compile/PanelKit';

const DAY_NAMES = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
const HOURS = Array.from({ length: 24 }, (_, hour) => hour);
const KIND_LABELS: Record<VaultBackup['kind'], string> = {
  Manual: 'Manual', Scheduled: 'Scheduled', PreRestore: 'Pre-restore',
};

/**
 * Namines Vault — standalone page.
 *
 * Not tied to Desk: backing up needs a database connection, not Desk's
 * CRUD/SQL console. Talks directly to `/api/vault` and `/api/auth`.
 */
export default function VaultPage() {
  const router = useRouter();
  const { isAuthenticated } = useAuthStore();
  const openAuthModal = useAuthModalStore(s => s.open);

  const [projects, setProjects] = useState<CloudProjectDto[] | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const loadProjects = useCallback(async () => {
    if (!isAuthenticated) return;
    const list = await authService.getCloudProjects();
    setProjects(list);
    setSelectedId(prev => prev ?? list.find(p => p.hasConnection)?.id ?? list[0]?.id ?? null);
  }, [isAuthenticated]);

  useEffect(() => { loadProjects(); }, [loadProjects]);

  if (!isAuthenticated) {
    return (
      <div className="h-[calc(100vh-56px)] bg-surface-900 flex items-center justify-center">
        <PanelEmpty icon={ShieldCheck} title="Sign in to use Namines Vault"
          hint="You need an account to back up and restore your database.">
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
            <ShieldCheck className="w-3.5 h-3.5 text-accent-text" /> Namines Vault
          </h1>
          <p className="text-[10.5px] text-content-muted mt-1 leading-snug">
            Backup and restore — no Desk required.
          </p>
        </div>

        <div className="flex-1 min-h-0 overflow-y-auto p-2 space-y-0.5">
          {!projects ? (
            <div className="px-2 py-3 text-[11px] text-content-muted">Loading…</div>
          ) : projects.length === 0 ? (
            <div className="px-2 py-3 text-[11px] text-content-muted">
              No projects yet. Create a database in{' '}
              <button className="underline hover:text-accent-text" onClick={() => router.push('/ground')}>
                Namines Ground
              </button>{' '}
              first, or add a connection in Desk.
            </div>
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
                  {p.hasConnection ? p.connectionDbType ?? 'Connected' : 'Not connected — cannot back up'}
                </div>
              </button>
            ))
          )}
        </div>
      </aside>

      <div className="flex-1 min-w-0 min-h-0 p-3">
        {selected ? (
          selected.hasConnection ? (
            <VaultDetail key={selected.id} projectId={selected.id} projectName={selected.name} />
          ) : (
            <Panel>
              <PanelEmpty icon={Cable} title="This project has no live database connection"
                hint="Vault needs a connection to back up. Create one in Namines Ground, or connect an existing server in Desk.">
                <ActionButton tone="primary" onClick={() => router.push('/ground')}>Open Namines Ground</ActionButton>
              </PanelEmpty>
            </Panel>
          )
        ) : (
          <Panel>
            <PanelEmpty icon={ShieldCheck} title="Select a project" hint="Pick which project to back up on the left." />
          </Panel>
        )}
      </div>
    </div>
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

  const reload = useCallback(async () => {
    try {
      const [list, history, plan, status] = await Promise.all([
        vaultApi.list(projectId), vaultApi.restores(projectId),
        vaultApi.schedule(projectId), vaultApi.health(),
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

  useEffect(() => { reload(); }, [reload]);

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
    setError(null); setNotice(null);
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
      link.href = url; link.download = fileName; link.click();
      URL.revokeObjectURL(url);
    } catch (err) {
      setError(err instanceof VaultGroundError ? err.message : 'Download failed.');
    } finally {
      setRowBusyId(null);
    }
  }

  async function handleDelete(backup: VaultBackup) {
    if (!confirm(`The backup from ${new Date(backup.createdAt).toLocaleString('en-US')} will be permanently deleted. Continue?`)) return;
    setRowBusyId(backup.id);
    setError(null);
    try {
      await vaultApi.remove(projectId, backup.id);
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
    setError(null); setNotice(null);
    try {
      const result = await vaultApi.restore(projectId, backup.id, typed);
      setConfirming(null);
      setNotice(result.preRestoreBackupId
        ? 'Restore complete. The previous state was saved as a separate backup.'
        : 'Restore complete.');
      await reload();
    } catch (err) {
      setError(err instanceof VaultGroundError ? err.message : 'Restore failed.');
    } finally {
      setBusy(false);
    }
  }

  const confirmMatches = confirming?.typed.trim() === confirming?.backup.databaseName;

  return (
    <Panel scroll className="h-full">
      <PanelBar
        left={<span className="text-[12px] font-semibold text-content-primary">{projectName}</span>}
      >
        <ActionButton tone="primary" busy={busy} disabled={health?.ok === false} onClick={handleCreate}>
          {busy ? 'Working…' : 'Back up now'}
        </ActionButton>
      </PanelBar>

      <div className="p-3 space-y-3">
        {health && !health.ok && (
          <div className="px-3 py-2 rounded-[var(--radius-control)] bg-[var(--color-danger-subtle)] text-[var(--color-danger)] text-[11px]">{health.problem}</div>
        )}
        {error && <div className="px-3 py-2 rounded-[var(--radius-control)] bg-[var(--color-danger-subtle)] text-[var(--color-danger)] text-[11px]">{error}</div>}
        {notice && <div className="px-3 py-2 rounded-[var(--radius-control)] bg-accent-subtle text-accent-text text-[11px]">{notice}</div>}
        {store && (
          <div className="px-3 py-2 rounded-[var(--radius-control)] bg-surface-600 text-content-secondary text-[11px]">
            Backups are stored at: <code>{store}</code>
          </div>
        )}
        {health && health.engines.length > 0 && (
          <div className="px-3 py-2 rounded-[var(--radius-control)] bg-surface-600 text-content-secondary text-[11px]">
            Engines that can be backed up: <code>{health.engines.join(', ')}</code>
          </div>
        )}

        {schedule && (
          <div className="flex flex-wrap items-end gap-3 p-3 rounded-[var(--radius-control)] bg-surface-600 border border-surface-500">
            <label className="flex items-center gap-1.5 text-[11px] text-content-secondary">
              <input type="checkbox" checked={schedule.enabled} disabled={savingSchedule}
                onChange={e => handleSaveSchedule({ ...schedule, enabled: e.target.checked })} />
              Automatic backups
            </label>
            <Field label="Frequency">
              <select value={schedule.cadence} disabled={savingSchedule}
                onChange={e => setSchedule({
                  ...schedule, cadence: e.target.value as typeof schedule.cadence,
                  dayOfWeek: e.target.value === 'Weekly' ? (schedule.dayOfWeek ?? 0) : null,
                })}
                className="bg-surface-700 border border-surface-500 rounded-[var(--radius-control)] px-2 py-1 text-[11px] text-content-primary">
                <option value="Daily">Daily</option>
                <option value="Weekly">Weekly</option>
              </select>
            </Field>
            {schedule.cadence === 'Weekly' && (
              <Field label="Day">
                <select value={schedule.dayOfWeek ?? 0} disabled={savingSchedule}
                  onChange={e => setSchedule({ ...schedule, dayOfWeek: Number(e.target.value) })}
                  className="bg-surface-700 border border-surface-500 rounded-[var(--radius-control)] px-2 py-1 text-[11px] text-content-primary">
                  {DAY_NAMES.map((name, index) => <option key={name} value={index}>{name}</option>)}
                </select>
              </Field>
            )}
            <Field label="Hour (UTC)">
              <select value={schedule.hourUtc} disabled={savingSchedule}
                onChange={e => setSchedule({ ...schedule, hourUtc: Number(e.target.value) })}
                className="bg-surface-700 border border-surface-500 rounded-[var(--radius-control)] px-2 py-1 text-[11px] text-content-primary">
                {HOURS.map(h => <option key={h} value={h}>{String(h).padStart(2, '0')}:00</option>)}
              </select>
            </Field>
            <Field label="Backups to keep">
              <input type="number" min={1} max={60} value={schedule.retainCount} disabled={savingSchedule}
                onChange={e => setSchedule({ ...schedule, retainCount: Number(e.target.value) })}
                className="w-16 bg-surface-700 border border-surface-500 rounded-[var(--radius-control)] px-2 py-1 text-[11px] text-content-primary" />
            </Field>
            <ActionButton tone="primary" busy={savingSchedule} onClick={() => handleSaveSchedule(schedule)}>Save schedule</ActionButton>
            <span className="text-[10.5px] text-content-muted basis-full">
              Retention only limits automatic backups; manual ones are never touched.
              {schedule.lastRunAt && ` Last automatic backup: ${new Date(schedule.lastRunAt).toLocaleString('en-US')}.`}
            </span>
          </div>
        )}

        {confirming && (
          <div className="p-3 rounded-[var(--radius-control)] bg-[var(--color-danger-subtle)] space-y-2">
            <p className="text-[11px] font-semibold text-[var(--color-danger)]">This cannot be undone.</p>
            <p className="text-[11px] text-content-secondary">
              Current data in <code>{confirming.backup.databaseName}</code> will be deleted and replaced with
              the backup from {new Date(confirming.backup.createdAt).toLocaleString('en-US')}.
              Type the database name to continue.
            </p>
            <input value={confirming.typed} placeholder={confirming.backup.databaseName}
              onChange={e => setConfirming({ ...confirming, typed: e.target.value })}
              className="w-full max-w-[280px] bg-surface-700 border border-surface-500 rounded-[var(--radius-control)] px-2.5 py-1.5 text-[11px] text-content-primary" />
            <div className="flex gap-2">
              <ActionButton tone="danger" busy={busy} disabled={!confirmMatches} onClick={handleRestore}>
                {busy ? 'Restoring…' : 'Restore'}
              </ActionButton>
              <ActionButton disabled={busy} onClick={() => setConfirming(null)}>Cancel</ActionButton>
            </div>
          </div>
        )}

        {!backups ? (
          <div className="flex items-center gap-2 text-[11px] text-content-muted"><Loader2 className="w-3.5 h-3.5 animate-spin" /> Loading…</div>
        ) : backups.length === 0 ? (
          <PanelEmpty icon={ShieldCheck} title="No backups yet"
            hint="Create the first one with “Back up now” above." />
        ) : (
          <div className="rounded-[var(--radius-control)] border border-surface-500 overflow-x-auto">
            <table className="w-full text-[11px] whitespace-nowrap">
              <thead>
                <tr className="bg-surface-800 text-content-muted">
                  <Th>Date</Th><Th>Kind</Th><Th>Engine</Th><Th>Size</Th><Th>Duration</Th><Th>Status</Th><Th>Verified</Th><Th align="right">Action</Th>
                </tr>
              </thead>
              <tbody>
                {backups.map(b => (
                  <tr key={b.id} className="border-t border-surface-500">
                    <Td>{new Date(b.createdAt).toLocaleString('en-US')}</Td>
                    <Td>{KIND_LABELS[b.kind]}</Td>
                    <Td>{b.engine}</Td>
                    <Td>{b.status === 'Succeeded' ? formatSize(b.sizeBytes) : '—'}</Td>
                    <Td>{formatDuration(b.createdAt, b.completedAt) ?? '—'}</Td>
                    <Td>
                      {b.status === 'Succeeded' ? 'Done'
                        : b.status === 'Running' ? 'Running…'
                        : <span title={b.error ?? undefined}>Failed</span>}
                    </Td>
                    <Td>
                      {b.verifiedAt ? (
                        <span className="inline-flex items-center gap-1 text-accent-text" title={`Verified: ${new Date(b.verifiedAt).toLocaleString('en-US')}`}>
                          <CheckCircle2 className="w-3 h-3" /> Verified
                        </span>
                      ) : b.verifyError ? (
                        <span className="text-[var(--color-danger)]" title={b.verifyError}>✗ Broken</span>
                      ) : '—'}
                    </Td>
                    <Td align="right">
                      <div className="flex items-center justify-end gap-1">
                        {b.status === 'Succeeded' && (
                          <IconButton icon={CheckCircle2} label="Verify" onClick={() => handleVerify(b)} busy={rowBusyId === b.id} disabled={busy} />
                        )}
                        {b.status === 'Succeeded' && (
                          <>
                            <IconButton icon={Download} label="Download" onClick={() => handleDownload(b)} busy={rowBusyId === b.id} disabled={busy} />
                            <IconButton icon={RotateCcw} label="Restore" disabled={busy} onClick={() => setConfirming({ backup: b, typed: '' })} />
                            <IconButton icon={Trash2} label="Delete" onClick={() => handleDelete(b)} busy={rowBusyId === b.id} disabled={busy} />
                          </>
                        )}
                      </div>
                    </Td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {restores.length > 0 && (
          <>
            <p className="text-[11px] font-semibold text-content-muted uppercase tracking-wider mt-4">Restore history</p>
            <div className="rounded-[var(--radius-control)] border border-surface-500 overflow-x-auto">
              <table className="w-full text-[11px] whitespace-nowrap">
                <thead>
                  <tr className="bg-surface-800 text-content-muted"><Th>Date</Th><Th>Source backup</Th><Th>Duration</Th><Th>Status</Th></tr>
                </thead>
                <tbody>
                  {restores.map(r => (
                    <tr key={r.id} className="border-t border-surface-500">
                      <Td>{new Date(r.startedAt).toLocaleString('en-US')}</Td>
                      <Td><code>{r.backupId.slice(0, 8)}</code></Td>
                      <Td>{formatDuration(r.startedAt, r.completedAt) ?? '—'}</Td>
                      <Td>
                        {r.status === 'Succeeded' ? 'Done'
                          : r.status === 'Running' ? 'Running…'
                          : <span title={r.error ?? undefined}>Failed</span>}
                      </Td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        )}
      </div>
    </Panel>
  );
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex flex-col gap-1">
      <span className="text-[10px] text-content-muted uppercase tracking-wide">{label}</span>
      {children}
    </div>
  );
}

function Th({ children, align }: { children: ReactNode; align?: 'right' }) {
  return <th className={`px-3 py-1.5 font-medium ${align === 'right' ? 'text-right' : 'text-left'}`}>{children}</th>;
}

function Td({ children, align }: { children: ReactNode; align?: 'right' }) {
  return <td className={`px-3 py-1.5 text-content-secondary ${align === 'right' ? 'text-right' : 'text-left'}`}>{children}</td>;
}
