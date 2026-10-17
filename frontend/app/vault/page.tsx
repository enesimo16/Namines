'use client';

import { useCallback, useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import {
  ShieldCheck, Cable, Loader2, Download, RotateCcw, Trash2, CheckCircle2, XCircle, Clock, DatabaseBackup, HardDrive,
} from 'lucide-react';
import { authService } from '../../services/api';
import { useAuthStore } from '../../store/useAuthStore';
import { useAuthModalStore } from '../../store/useAuthModalStore';
import { useProjectHistoryStore } from '../../store/useProjectHistoryStore';
import type { CloudProjectDto } from '../../types/api';
import {
  vaultApi, VaultGroundError, formatDuration, formatSize,
  type VaultBackup, type VaultRestore, type VaultSchedule, type VaultHealth,
} from '../../services/vaultGroundApi';
import ProjectPicker from '../../components/workspace/ProjectPicker';
import {
  PageShell, PageHeader, Card, Callout, Badge, Button, EmptyState, StatTile, Switch, Field, inputClass, Select,
  DataTable, Td,
} from '../../components/workspace/WorkspaceKit';

const DAY_NAMES = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
const HOURS = Array.from({ length: 24 }, (_, hour) => hour);
const KIND_LABELS: Record<VaultBackup['kind'], string> = {
  Manual: 'Manual', Scheduled: 'Scheduled', PreRestore: 'Pre-restore',
};

const formatWhen = (iso: string) =>
  new Date(iso).toLocaleString('en-US', { dateStyle: 'medium', timeStyle: 'short' });

/**
 * Namines Vault — bağımsız sayfa.
 *
 * Desk'e bağlı değil: yedeklemek bir veritabanı bağlantısı ister, Desk'in CRUD/SQL
 * konsolunu değil. Doğrudan `/api/vault` ve `/api/auth` ile konuşur.
 */
export default function VaultPage() {
  const router = useRouter();
  const isAuthenticated = useAuthStore(s => s.isAuthenticated);
  const openAuthModal = useAuthModalStore(s => s.open);
  const activeProjectId = useProjectHistoryStore(s => s.activeProjectId);

  const [projects, setProjects] = useState<CloudProjectDto[] | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const loadProjects = useCallback(async () => {
    if (!isAuthenticated) return;
    const list = await authService.getCloudProjects();
    setProjects(list);
    setSelectedId(prev => prev
      ?? list.find(p => p.id === activeProjectId && p.hasConnection)?.id
      ?? list.find(p => p.hasConnection)?.id
      ?? list[0]?.id
      ?? null);
  }, [isAuthenticated, activeProjectId]);

  useEffect(() => { loadProjects(); }, [loadProjects]);

  if (!isAuthenticated) {
    return (
      <PageShell width="max-w-3xl">
        <EmptyState icon={ShieldCheck} title="Sign in to use Namines Vault"
          description="You need an account to back up and restore your database."
          action={<Button variant="primary" onClick={openAuthModal}>Log in / Sign up</Button>} />
      </PageShell>
    );
  }

  const selected = projects?.find(p => p.id === selectedId) ?? null;

  return (
    <PageShell>
      <PageHeader
        icon={ShieldCheck}
        title="Vault"
        description="Encrypted backups of your database — take one now, schedule them, verify they really restore, and roll back when you need to."
      />

      <ProjectPicker
        projects={projects}
        selectedId={selectedId}
        onSelect={setSelectedId}
        describe={p => (p.hasConnection ? p.connectionDbType ?? 'connected' : 'not connected')}
      />

      {selected ? (
        selected.hasConnection ? (
          <VaultDetail key={selected.id} projectId={selected.id} projectName={selected.name} />
        ) : (
          <Card>
            <EmptyState icon={Cable} title="This project has no live database connection"
              description="Vault needs a connection to back up. Create a database in Ground, or connect an existing server in Desk."
              action={<Button variant="primary" icon={HardDrive} onClick={() => router.push('/ground')}>Open Ground</Button>} />
          </Card>
        )
      ) : !projects ? (
        <Card>
          <div className="flex items-center gap-2 px-5 py-8 text-[13px] text-content-muted">
            <Loader2 className="w-4 h-4 animate-spin" aria-hidden="true" /> Loading your projects…
          </div>
        </Card>
      ) : (
        <Card>
          <EmptyState icon={ShieldCheck}
            title={projects.length === 0 ? 'No projects yet' : 'Select a project'}
            description={projects.length === 0
              ? 'Create a database in Ground first, or add a connection in Desk.'
              : 'Pick which project to back up above.'} />
        </Card>
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
  const succeeded = (backups ?? []).filter(b => b.status === 'Succeeded');
  const latest = succeeded[0];

  return (
    <div className="space-y-5">
      {health && !health.ok && <Callout tone="danger" title="Backups are unavailable">{health.problem}</Callout>}
      {error && <Callout tone="danger">{error}</Callout>}
      {notice && <Callout tone="success">{notice}</Callout>}

      <div className="grid grid-cols-2 lg:grid-cols-4 gap-3">
        <StatTile label="Backups" value={backups ? succeeded.length : '—'} hint={backups ? `${backups.length} total` : undefined} />
        <StatTile label="Latest backup" value={latest ? formatWhen(latest.createdAt) : 'None yet'} />
        <StatTile label="Engines" value={health?.engines.length ? health.engines.join(', ') : '—'} />
        <StatTile label="Stored at" value={store ? (store.startsWith('Local disk') ? 'Local disk' : store) : '—'} />
      </div>

      {schedule && (
        <Card
          title="Automatic backups"
          description="Retention only limits automatic backups; manual ones are never touched."
          actions={
            <div className="flex items-center gap-2.5 text-[13px] text-content-secondary">
              <Switch checked={schedule.enabled} disabled={savingSchedule} label="Automatic backups"
                onChange={enabled => handleSaveSchedule({ ...schedule, enabled })} />
              {schedule.enabled ? 'On' : 'Off'}
            </div>
          }
        >
          <div className="flex flex-wrap items-end gap-4">
            <Field label="Frequency" htmlFor="vault-frequency">
              <Select id="vault-frequency" value={schedule.cadence} disabled={savingSchedule}
                onChange={e => setSchedule({
                  ...schedule, cadence: e.target.value as typeof schedule.cadence,
                  dayOfWeek: e.target.value === 'Weekly' ? (schedule.dayOfWeek ?? 0) : null,
                })}
                className="w-32">
                <option value="Daily">Daily</option>
                <option value="Weekly">Weekly</option>
              </Select>
            </Field>
            {schedule.cadence === 'Weekly' && (
              <Field label="Day" htmlFor="vault-day">
                <Select id="vault-day" value={schedule.dayOfWeek ?? 0} disabled={savingSchedule}
                  onChange={e => setSchedule({ ...schedule, dayOfWeek: Number(e.target.value) })}
                  className="w-36">
                  {DAY_NAMES.map((name, index) => <option key={name} value={index}>{name}</option>)}
                </Select>
              </Field>
            )}
            <Field label="Hour (UTC)" htmlFor="vault-hour">
              <Select id="vault-hour" value={schedule.hourUtc} disabled={savingSchedule}
                onChange={e => setSchedule({ ...schedule, hourUtc: Number(e.target.value) })}
                className="w-28">
                {HOURS.map(h => <option key={h} value={h}>{String(h).padStart(2, '0')}:00</option>)}
              </Select>
            </Field>
            <Field label="Backups to keep" htmlFor="vault-retain">
              <input id="vault-retain" type="number" min={1} max={60} value={schedule.retainCount} disabled={savingSchedule}
                onChange={e => setSchedule({ ...schedule, retainCount: Number(e.target.value) })}
                className={`${inputClass} w-28`} />
            </Field>
            <Button variant="primary" busy={savingSchedule} onClick={() => handleSaveSchedule(schedule)}>Save schedule</Button>
          </div>
          {schedule.lastRunAt && (
            <p className="mt-3 text-[12px] text-content-muted">Last automatic backup: {formatWhen(schedule.lastRunAt)}.</p>
          )}
        </Card>
      )}

      {confirming && (
        <Card tone="danger" title="Restore this backup?"
          description={`Current data in ${confirming.backup.databaseName} will be deleted and replaced with the backup from ${formatWhen(confirming.backup.createdAt)}. This cannot be undone — a safety backup of the current state is taken first.`}>
          <div className="space-y-3 max-w-md">
            <Field label={`Type the database name to continue: ${confirming.backup.databaseName}`} htmlFor="vault-confirm-name">
              <input id="vault-confirm-name" autoFocus value={confirming.typed} placeholder={confirming.backup.databaseName}
                onChange={e => setConfirming({ ...confirming, typed: e.target.value })} className={inputClass} />
            </Field>
            <div className="flex gap-2">
              <Button variant="danger" icon={RotateCcw} busy={busy} disabled={!confirmMatches} onClick={handleRestore}>
                {busy ? 'Restoring…' : 'Restore'}
              </Button>
              <Button variant="ghost" disabled={busy} onClick={() => setConfirming(null)}>Cancel</Button>
            </div>
          </div>
        </Card>
      )}

      <Card
        title="Backups"
        description={`All backups of ${projectName}, newest first.${store ? ` Stored at ${store.replace(/^Local disk \((.*)\)$/, '$1')}.` : ''}`}
        actions={<Button variant="primary" icon={DatabaseBackup} busy={busy} disabled={health?.ok === false} onClick={handleCreate}>
          {busy ? 'Working…' : 'Back up now'}
        </Button>}
        flush
      >
        {!backups ? (
          <div className="flex items-center gap-2 px-5 pb-5 text-[13px] text-content-muted">
            <Loader2 className="w-4 h-4 animate-spin" aria-hidden="true" /> Loading…
          </div>
        ) : backups.length === 0 ? (
          <EmptyState icon={ShieldCheck} title="No backups yet"
            description="Create the first one with “Back up now”." />
        ) : (
          <DataTable
            caption={`Backups of ${projectName}`}
            columns={[
              { label: 'Date' }, { label: 'Kind' }, { label: 'Engine' }, { label: 'Size' },
              { label: 'Duration' }, { label: 'Status' }, { label: 'Verified' }, { label: 'Actions', align: 'right' },
            ]}
          >
            {backups.map(b => (
              <tr key={b.id} className="hover:bg-surface-700/30 transition-colors">
                <Td>{formatWhen(b.createdAt)}</Td>
                <Td>{KIND_LABELS[b.kind]}</Td>
                <Td>{b.engine}</Td>
                <Td mono>{b.status === 'Succeeded' ? formatSize(b.sizeBytes) : '—'}</Td>
                <Td mono>{formatDuration(b.createdAt, b.completedAt) ?? '—'}</Td>
                <Td>
                  {b.status === 'Succeeded' ? <Badge tone="success" icon={CheckCircle2}>Done</Badge>
                    : b.status === 'Running' ? <Badge tone="accent" icon={Clock}>Running…</Badge>
                    : <Badge tone="danger" icon={XCircle} title={b.error ?? undefined}>Failed</Badge>}
                </Td>
                <Td>
                  {b.verifiedAt ? (
                    <Badge tone="success" icon={ShieldCheck} title={`Verified ${formatWhen(b.verifiedAt)}`}>Verified</Badge>
                  ) : b.verifyError ? (
                    <Badge tone="danger" icon={XCircle} title={b.verifyError}>Broken</Badge>
                  ) : <span className="text-content-subtle">Not verified</span>}
                </Td>
                <Td align="right">
                  {b.status === 'Succeeded' && (deletingId === b.id ? (
                    <div className="inline-flex items-center gap-1.5">
                      <span className="text-[12px] text-content-secondary">Delete permanently?</span>
                      <Button size="sm" variant="danger" busy={rowBusyId === b.id} onClick={() => handleDelete(b)}>Delete</Button>
                      <Button size="sm" variant="ghost" onClick={() => setDeletingId(null)}>Cancel</Button>
                    </div>
                  ) : (
                    <div className="inline-flex items-center gap-1">
                      <Button size="sm" variant="ghost" icon={ShieldCheck} aria-label={`Verify the backup from ${formatWhen(b.createdAt)}`}
                        title="Verify: restore into a throwaway server" busy={rowBusyId === b.id} disabled={busy} onClick={() => handleVerify(b)} />
                      <Button size="sm" variant="ghost" icon={Download} aria-label={`Download the backup from ${formatWhen(b.createdAt)}`}
                        title="Download (encrypted)" disabled={busy || rowBusyId === b.id} onClick={() => handleDownload(b)} />
                      <Button size="sm" variant="ghost" icon={RotateCcw} aria-label={`Restore the backup from ${formatWhen(b.createdAt)}`}
                        title="Restore" disabled={busy} onClick={() => setConfirming({ backup: b, typed: '' })} />
                      <Button size="sm" variant="ghost" icon={Trash2} aria-label={`Delete the backup from ${formatWhen(b.createdAt)}`}
                        title="Delete" disabled={busy} onClick={() => setDeletingId(b.id)} />
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
            columns={[{ label: 'Date' }, { label: 'Source backup' }, { label: 'Duration' }, { label: 'Status' }]}
          >
            {restores.map(r => (
              <tr key={r.id} className="hover:bg-surface-700/30 transition-colors">
                <Td>{formatWhen(r.startedAt)}</Td>
                <Td mono>{r.backupId.slice(0, 8)}</Td>
                <Td mono>{formatDuration(r.startedAt, r.completedAt) ?? '—'}</Td>
                <Td>
                  {r.status === 'Succeeded' ? <Badge tone="success" icon={CheckCircle2}>Done</Badge>
                    : r.status === 'Running' ? <Badge tone="accent" icon={Clock}>Running…</Badge>
                    : <Badge tone="danger" icon={XCircle} title={r.error ?? undefined}>Failed</Badge>}
                </Td>
              </tr>
            ))}
          </DataTable>
        </Card>
      )}
    </div>
  );
}
