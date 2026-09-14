'use client';

import { useCallback, useEffect, useState } from 'react';
import EmptyState from './EmptyState';
import { type DeskSession } from '../lib/api';
import { formatSize } from '../lib/format';
import {
  vaultApi, VaultError, formatDuration,
  type VaultBackup, type VaultRestore, type VaultSchedule, type VaultHealth,
} from '../lib/vault';
import PageHead from './PageHead';

const DAY_NAMES = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

const HOURS = Array.from({ length: 24 }, (_, hour) => hour);

const KIND_LABELS: Record<VaultBackup['kind'], string> = {
  Manual: 'Manual',
  Scheduled: 'Scheduled',
  PreRestore: 'Pre-restore',
};

/**
 * Namines Vault — backup screen.
 *
 * Vault has no site of its own; this view is its only user-facing surface.
 * Restore is the most destructive action in Desk: it wipes the target
 * database's current objects, so it isn't one click — the user has to type
 * the database name, and the server always takes a mandatory backup right
 * before restoring, which this screen also states plainly.
 *
 * Authorization is decided on the server, not in Desk. `isOwner` here only
 * HIDES destructive buttons — it doesn't replace the server's own check.
 */
export default function Vault({ session, isOwner }: { session: DeskSession; isOwner: boolean }) {
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

  // Confirmation box: which backup, and what the user has typed so far.
  const [confirming, setConfirming] = useState<{ backup: VaultBackup; typed: string } | null>(null);

  const reload = useCallback(async () => {
    try {
      const [list, history, plan, status] = await Promise.all([
        vaultApi.list(session), vaultApi.restores(session),
        vaultApi.schedule(session), vaultApi.health(session),
      ]);
      setBackups(list.backups);
      setStore(list.store);
      setRestores(history);
      setSchedule(plan);
      setHealth(status);
      setError(null);
    } catch (err) {
      setError(err instanceof VaultError ? err.message : 'Could not load backups.');
      setBackups([]);
    }
  }, [session]);

  useEffect(() => { reload(); }, [reload]);

  async function handleCreate() {
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      // The server returns 202 and the job runs in the background (B-35),
      // so it hasn't finished the moment this call returns — waiting for
      // that has to happen by polling `status`.
      const started = await vaultApi.create(session);
      setNotice('Backup started, running…');

      const outcome = await vaultApi.waitForBackup(session, started.backupId);

      if (!outcome.done) {
        // Polling timed out. The job was NOT cancelled — it may still be
        // running server-side, so calling it "failed" would be wrong.
        setNotice('Backup is still running. You can track its status from the list below.');
      } else if (outcome.status === 'Succeeded') {
        setNotice('Backup created.');
      } else {
        setNotice(null);
        setError(outcome.errorMessage ?? 'Backup failed.');
      }

      await reload();
    } catch (err) {
      setError(err instanceof VaultError ? err.message : 'Backup failed.');
    } finally {
      setBusy(false);
    }
  }

  async function handleVerify(backup: VaultBackup) {
    setRowBusyId(backup.id);
    setError(null);
    setNotice(null);
    try {
      await vaultApi.verify(session, backup.id);
      setNotice('Backup verified: restored to a throwaway server and proven to work.');
    } catch (err) {
      // A FAILED verification isn't a UI error, it's a real finding: the
      // backup is broken. So the message isn't suppressed — it's shown.
      setError(err instanceof VaultError ? err.message : 'Verification failed.');
    } finally {
      setRowBusyId(null);
      await reload();
    }
  }

  async function handleSaveSchedule(next: VaultSchedule) {
    setSavingSchedule(true);
    setError(null);
    try {
      setSchedule(await vaultApi.saveSchedule(session, next));
      setNotice('Backup schedule saved.');
    } catch (err) {
      setError(err instanceof VaultError ? err.message : 'Could not save the schedule.');
    } finally {
      setSavingSchedule(false);
    }
  }

  async function handleDownload(backup: VaultBackup) {
    setRowBusyId(backup.id);
    setError(null);
    try {
      const { blob, fileName } = await vaultApi.download(session, backup.id);
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = fileName;
      link.click();
      // Freeing the object URL — otherwise the blob stays in memory until the tab closes.
      URL.revokeObjectURL(url);
    } catch (err) {
      setError(err instanceof VaultError ? err.message : 'Download failed.');
    } finally {
      setRowBusyId(null);
    }
  }

  async function handleDelete(backup: VaultBackup) {
    if (!confirm(`The backup from ${new Date(backup.createdAt).toLocaleString('en-US')} will be permanently deleted. Continue?`)) return;
    setRowBusyId(backup.id);
    setError(null);
    try {
      await vaultApi.remove(session, backup.id);
      await reload();
    } catch (err) {
      setError(err instanceof VaultError ? err.message : 'Delete failed.');
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
      const result = await vaultApi.restore(session, backup.id, typed);
      setConfirming(null);
      setNotice(
        result.preRestoreBackupId
          ? 'Restore complete. The previous state was saved as a separate backup.'
          : 'Restore complete.',
      );
      await reload();
    } catch (err) {
      setError(err instanceof VaultError ? err.message : 'Restore failed.');
    } finally {
      setBusy(false);
    }
  }

  const confirmMatches = confirming?.typed.trim() === confirming?.backup.databaseName;

  return (
    <div className="page">
      <PageHead
        title="Namines Vault"
        desc={<>
          A full copy of the project&apos;s live database. Files are stored <b>encrypted</b> and
          arrive at the browser encrypted too. <b>Restoring overwrites the target&apos;s current data</b> —
          a mandatory backup of the current state is taken automatically before every restore.
        </>}
        actions={
          <button
            className="btn btn-primary"
            disabled={busy || health?.ok === false}
            title={health?.problem ?? undefined}
            onClick={handleCreate}
          >
            {busy ? 'Working…' : 'Back up now'}
          </button>
        }
      />

      {/* Any blocker goes at the TOP: the user shouldn't have to guess why it isn't working. */}
      {health && !health.ok && (
        <div className="notice notice-error">{health.problem}</div>
      )}

      {error && <div className="notice notice-error">{error}</div>}
      {notice && <div className="notice">{notice}</div>}

      {/* WHERE backups sit is not a detail to hide: v1 uses the server's own
          disk, and the user shouldn't trust a backup without knowing that. */}
      {store && <div className="notice">Backups are stored at: <code>{store}</code></div>}

      {/* Which engines can be backed up is stated on screen — otherwise an
          unsupported engine would only be discovered on the first failed attempt.
          The list comes from the server's registered providers, not hardcoded here. */}
      {health && health.engines.length > 0 && (
        <div className="notice">Engines that can be backed up: <code>{health.engines.join(', ')}</code></div>
      )}

      {schedule && (
        <div className="grid-wrap" style={{ padding: 10, display: 'flex', flexWrap: 'wrap', gap: 10, alignItems: 'flex-end' }}>
          <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 13 }}>
            <input
              type="checkbox"
              checked={schedule.enabled}
              disabled={!isOwner || savingSchedule}
              onChange={e => handleSaveSchedule({ ...schedule, enabled: e.target.checked })}
            />
            Automatic backups
          </label>

          <div className="field" style={{ margin: 0 }}>
            <label htmlFor="vault-cadence">Frequency</label>
            <select
              id="vault-cadence"
              value={schedule.cadence}
              disabled={!isOwner || savingSchedule}
              onChange={e => setSchedule({
                ...schedule,
                cadence: e.target.value as typeof schedule.cadence,
                // Switching to weekly requires a day picked — the server
                // already rejects a day-less weekly schedule.
                dayOfWeek: e.target.value === 'Weekly' ? (schedule.dayOfWeek ?? 0) : null,
              })}
            >
              <option value="Daily">Daily</option>
              <option value="Weekly">Weekly</option>
            </select>
          </div>

          {schedule.cadence === 'Weekly' && (
            <div className="field" style={{ margin: 0 }}>
              <label htmlFor="vault-day">Day</label>
              <select
                id="vault-day"
                value={schedule.dayOfWeek ?? 0}
                disabled={!isOwner || savingSchedule}
                onChange={e => setSchedule({ ...schedule, dayOfWeek: Number(e.target.value) })}
              >
                {DAY_NAMES.map((name, index) => <option key={name} value={index}>{name}</option>)}
              </select>
            </div>
          )}

          <div className="field" style={{ margin: 0 }}>
            {/* Hour is UTC so a server timezone change never shifts the schedule. */}
            <label htmlFor="vault-hour">Hour (UTC)</label>
            <select
              id="vault-hour"
              value={schedule.hourUtc}
              disabled={!isOwner || savingSchedule}
              onChange={e => setSchedule({ ...schedule, hourUtc: Number(e.target.value) })}
            >
              {HOURS.map(h => <option key={h} value={h}>{String(h).padStart(2, '0')}:00</option>)}
            </select>
          </div>

          <div className="field" style={{ margin: 0 }}>
            <label htmlFor="vault-retain">Backups to keep</label>
            <input
              id="vault-retain" type="number" min={1} max={60} style={{ width: 80 }}
              value={schedule.retainCount}
              disabled={!isOwner || savingSchedule}
              onChange={e => setSchedule({ ...schedule, retainCount: Number(e.target.value) })}
            />
          </div>

          <button className="btn" disabled={!isOwner || savingSchedule}
                  onClick={() => handleSaveSchedule(schedule)}>
            {savingSchedule ? 'Saving…' : 'Save schedule'}
          </button>

          <span style={{ fontSize: 12.5, opacity: 0.75 }}>
            {/* Retention only prunes automatic backups — the user shouldn't think a manual one will vanish too. */}
            Retention only limits automatic backups; manual ones are never touched.
            {schedule.lastRunAt && ` Last automatic backup: ${new Date(schedule.lastRunAt).toLocaleString('en-US')}.`}
          </span>
        </div>
      )}

      {confirming && (
        <div className="notice notice-error" style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
          <b>This cannot be undone.</b>
          <span>
            Current data in <code>{confirming.backup.databaseName}</code> will be deleted and replaced with
            the backup from {new Date(confirming.backup.createdAt).toLocaleString('en-US')}.
            Type the database name to continue.
          </span>
          <input
            aria-label="Database name to confirm"
            type="text"
            value={confirming.typed}
            placeholder={confirming.backup.databaseName}
            style={{ maxWidth: 280 }}
            onChange={e => setConfirming({ ...confirming, typed: e.target.value })}
          />
          <div className="row-actions">
            <button className="btn btn-danger" disabled={busy || !confirmMatches} onClick={handleRestore}>
              {busy ? 'Restoring…' : 'Restore'}
            </button>
            <button className="btn btn-sm" disabled={busy} onClick={() => setConfirming(null)}>
              Cancel
            </button>
          </div>
        </div>
      )}

      {!backups ? (
        <div className="empty">Loading…</div>
      ) : backups.length === 0 ? (
        <EmptyState
          title="No backups yet"
          description={
            <>
              Backups are stored encrypted, and their restorability is <strong>proven</strong> by actually
              restoring them to a clean server. Create the first one with &quot;Back up now&quot; above,
              or turn on the schedule for regular backups.
            </>
          }
        />
      ) : (
        <div className="grid-wrap">
          <table>
            <thead>
              <tr>
                <th>Date</th><th>Kind</th><th>Engine</th><th>Size</th>
                <th>Duration</th><th>Status</th><th>Verified</th><th style={{ textAlign: 'right' }}>Action</th>
              </tr>
            </thead>
            <tbody>
              {backups.map(b => (
                <tr key={b.id}>
                  <td className="nowrap">{new Date(b.createdAt).toLocaleString('en-US')}</td>
                  <td className="nowrap">{KIND_LABELS[b.kind]}</td>
                  <td className="nowrap">{b.engine}</td>
                  <td className="nowrap">{b.status === 'Succeeded' ? formatSize(b.sizeBytes) : '—'}</td>
                  <td className="nowrap">{formatDuration(b.createdAt, b.completedAt) ?? '—'}</td>
                  <td className="nowrap">
                    {b.status === 'Succeeded' ? 'Done'
                      : b.status === 'Running' ? 'Running…'
                      // The error text lives right in the row: the user shouldn't
                      // have to go to another screen to ask "why did this fail".
                      : <span title={b.error ?? undefined}>Failed</span>}
                  </td>
                  <td className="nowrap">
                    {/* Three distinct states: proven / found broken / never tried.
                        Merging the last two would make a broken backup look merely unverified. */}
                    {b.verifiedAt ? (
                      <span title={`Verified: ${new Date(b.verifiedAt).toLocaleString('en-US')}`}>✓ Verified</span>
                    ) : b.verifyError ? (
                      <span title={b.verifyError}>✗ Broken</span>
                    ) : '—'}
                  </td>
                  <td>
                    <div className="row-actions" style={{ justifyContent: 'flex-end' }}>
                      {/* Verification runs on a throwaway server — it never touches
                          the project's database, so no Owner requirement here. */}
                      {b.status === 'Succeeded' && (
                        <button className="btn btn-sm" disabled={rowBusyId === b.id || busy}
                                onClick={() => handleVerify(b)}>
                          {rowBusyId === b.id ? 'Verifying…' : 'Verify'}
                        </button>
                      )}
                      {b.status === 'Succeeded' && isOwner && (
                        <>
                          <button className="btn btn-sm" disabled={rowBusyId === b.id || busy}
                                  onClick={() => handleDownload(b)}>
                            Download
                          </button>
                          <button className="btn btn-sm btn-danger" disabled={busy}
                                  onClick={() => setConfirming({ backup: b, typed: '' })}>
                            Restore
                          </button>
                          <button className="btn btn-sm" disabled={rowBusyId === b.id || busy}
                                  onClick={() => handleDelete(b)}>
                            Delete
                          </button>
                        </>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {restores.length > 0 && (
        <>
          <PageHead
            title="Restore history"
            desc="Who last restored this database, when, and from which backup."
          />
          <div className="grid-wrap">
            <table>
              <thead>
                <tr><th>Date</th><th>Source backup</th><th>Duration</th><th>Status</th></tr>
              </thead>
              <tbody>
                {restores.map(r => (
                  <tr key={r.id}>
                    <td className="nowrap">{new Date(r.startedAt).toLocaleString('en-US')}</td>
                    <td className="nowrap"><code>{r.backupId.slice(0, 8)}</code></td>
                    <td className="nowrap">{formatDuration(r.startedAt, r.completedAt) ?? '—'}</td>
                    <td className="nowrap">
                      {r.status === 'Succeeded' ? 'Done'
                        : r.status === 'Running' ? 'Running…'
                        : <span title={r.error ?? undefined}>Failed</span>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  );
}
