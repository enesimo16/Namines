'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import EmptyState from './EmptyState';
import { type DeskSession } from '../lib/api';
import { formatSize } from '../lib/format';
import {
  vaultApi, VaultError, formatDuration,
  type VaultBackup, type VaultRestore, type VaultSchedule, type VaultHealth,
} from '../lib/vault';
import PageHead from './PageHead';
import { useI18n, type MessageKey } from '../lib/i18n';
import { rich } from '../lib/rich';

// Sıra sunucunun `dayOfWeek`'iyle aynı: 0 = Pazar (.NET DayOfWeek).
const DAY_KEYS: MessageKey[] = [
  'vault.days.d0', 'vault.days.d1', 'vault.days.d2', 'vault.days.d3',
  'vault.days.d4', 'vault.days.d5', 'vault.days.d6',
];

const HOURS = Array.from({ length: 24 }, (_, hour) => hour);

const KIND_KEYS: Record<VaultBackup['kind'], MessageKey> = {
  Manual: 'vault.kinds.Manual',
  Scheduled: 'vault.kinds.Scheduled',
  PreRestore: 'vault.kinds.PreRestore',
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
  const { t, dt } = useI18n();
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
      setError(err instanceof VaultError ? err.message : t('vault.loadFailed'));
      setBackups([]);
    }
  }, [session, t]);

  useEffect(() => { reload(); }, [reload]);

  // `handleCreate` awaits `waitForBackup`, which polls every couple of
  // seconds for up to 10 minutes. If the user navigates away from Vault
  // while that's in flight, the component unmounts but the poll loop (and
  // this function) kept running, later calling setState on a component that
  // no longer exists. This aborts the poll and skips further state updates
  // once the component is gone.
  const unmountAbortRef = useRef<AbortController | null>(null);
  useEffect(() => {
    return () => { unmountAbortRef.current?.abort(); };
  }, []);

  async function handleCreate() {
    setBusy(true);
    setError(null);
    setNotice(null);
    const controller = new AbortController();
    unmountAbortRef.current = controller;
    try {
      // The server returns 202 and the job runs in the background (B-35),
      // so it hasn't finished the moment this call returns — waiting for
      // that has to happen by polling `status`.
      const started = await vaultApi.create(session);
      if (controller.signal.aborted) return;
      setNotice(t('vault.started'));

      const outcome = await vaultApi.waitForBackup(session, started.backupId, { signal: controller.signal });
      if (controller.signal.aborted) return;

      if (!outcome.done) {
        // Polling timed out. The job was NOT cancelled — it may still be
        // running server-side, so calling it "failed" would be wrong.
        setNotice(t('vault.stillRunning'));
      } else if (outcome.status === 'Succeeded') {
        setNotice(t('vault.created'));
      } else {
        setNotice(null);
        setError(outcome.errorMessage ?? t('vault.backupFailed'));
      }

      await reload();
    } catch (err) {
      if (!controller.signal.aborted) {
        setError(err instanceof VaultError ? err.message : t('vault.backupFailed'));
      }
    } finally {
      if (!controller.signal.aborted) setBusy(false);
    }
  }

  async function handleVerify(backup: VaultBackup) {
    setRowBusyId(backup.id);
    setError(null);
    setNotice(null);
    try {
      await vaultApi.verify(session, backup.id);
      setNotice(t('vault.verified'));
    } catch (err) {
      // A FAILED verification isn't a UI error, it's a real finding: the
      // backup is broken. So the message isn't suppressed — it's shown.
      setError(err instanceof VaultError ? err.message : t('vault.verifyFailed'));
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
      setNotice(t('vault.scheduleSaved'));
    } catch (err) {
      setError(err instanceof VaultError ? err.message : t('vault.scheduleFailed'));
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
      setError(err instanceof VaultError ? err.message : t('vault.downloadFailed'));
    } finally {
      setRowBusyId(null);
    }
  }

  async function handleDelete(backup: VaultBackup) {
    if (!confirm(t('vault.confirmDelete', { date: dt(backup.createdAt) }))) return;
    setRowBusyId(backup.id);
    setError(null);
    try {
      await vaultApi.remove(session, backup.id);
      await reload();
    } catch (err) {
      setError(err instanceof VaultError ? err.message : t('common.deleteFailed'));
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
          ? t('vault.restoreDoneSaved')
          : t('vault.restoreDone'),
      );
      await reload();
    } catch (err) {
      setError(err instanceof VaultError ? err.message : t('vault.restoreFailed'));
    } finally {
      setBusy(false);
    }
  }

  const confirmMatches = confirming?.typed.trim() === confirming?.backup.databaseName;

  return (
    <div className="page">
      <PageHead
        title="Namines Vault"
        desc={rich(t('vault.desc'))}
        actions={
          <button
            className="btn btn-primary"
            disabled={busy || health?.ok === false}
            title={health?.problem ?? undefined}
            onClick={handleCreate}
          >
            {busy ? t('vault.working') : t('vault.backUpNow')}
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
      {store && <div className="notice">{rich(t('vault.storedAt', { store }))}</div>}

      {/* Which engines can be backed up is stated on screen — otherwise an
          unsupported engine would only be discovered on the first failed attempt.
          The list comes from the server's registered providers, not hardcoded here. */}
      {health && health.engines.length > 0 && (
        <div className="notice">{rich(t('vault.engines', { engines: health.engines.join(', ') }))}</div>
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
            {t('vault.automatic')}
          </label>

          <div className="field" style={{ margin: 0 }}>
            <label htmlFor="vault-cadence">{t('vault.frequency')}</label>
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
              <option value="Daily">{t('vault.daily')}</option>
              <option value="Weekly">{t('vault.weekly')}</option>
            </select>
          </div>

          {schedule.cadence === 'Weekly' && (
            <div className="field" style={{ margin: 0 }}>
              <label htmlFor="vault-day">{t('vault.day')}</label>
              <select
                id="vault-day"
                value={schedule.dayOfWeek ?? 0}
                disabled={!isOwner || savingSchedule}
                onChange={e => setSchedule({ ...schedule, dayOfWeek: Number(e.target.value) })}
              >
                {DAY_KEYS.map((key, index) => <option key={key} value={index}>{t(key)}</option>)}
              </select>
            </div>
          )}

          <div className="field" style={{ margin: 0 }}>
            {/* Hour is UTC so a server timezone change never shifts the schedule. */}
            <label htmlFor="vault-hour">{t('vault.hourUtc')}</label>
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
            <label htmlFor="vault-retain">{t('vault.retain')}</label>
            <input
              id="vault-retain" type="number" min={1} max={60} style={{ width: 80 }}
              value={schedule.retainCount}
              disabled={!isOwner || savingSchedule}
              onChange={e => setSchedule({ ...schedule, retainCount: Number(e.target.value) })}
            />
          </div>

          <button className="btn" disabled={!isOwner || savingSchedule}
                  onClick={() => handleSaveSchedule(schedule)}>
            {savingSchedule ? t('common.saving') : t('vault.saveSchedule')}
          </button>

          <span style={{ fontSize: 12.5, opacity: 0.75 }}>
            {/* Retention only prunes automatic backups — the user shouldn't think a manual one will vanish too. */}
            {t('vault.retentionNote')}
            {schedule.lastRunAt && t('vault.lastRun', { date: dt(schedule.lastRunAt) })}
          </span>
        </div>
      )}

      {confirming && (
        <div className="notice notice-error" style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
          <b>{t('vault.cannotUndo')}</b>
          <span>
            {rich(t('vault.restoreWarning', { db: confirming.backup.databaseName, date: dt(confirming.backup.createdAt) }))}
          </span>
          <input
            aria-label={t('vault.confirmDbLabel')}
            type="text"
            value={confirming.typed}
            placeholder={confirming.backup.databaseName}
            style={{ maxWidth: 280 }}
            onChange={e => setConfirming({ ...confirming, typed: e.target.value })}
          />
          <div className="row-actions">
            <button className="btn btn-danger" disabled={busy || !confirmMatches} onClick={handleRestore}>
              {busy ? t('vault.restoring') : t('vault.restore')}
            </button>
            <button className="btn btn-sm" disabled={busy} onClick={() => setConfirming(null)}>
              {t('common.cancel')}
            </button>
          </div>
        </div>
      )}

      {!backups ? (
        <div className="empty">{t('common.loading')}</div>
      ) : backups.length === 0 ? (
        <EmptyState
          title={t('vault.emptyTitle')}
          description={rich(t('vault.emptyDesc'))}
        />
      ) : (
        <div className="grid-wrap">
          <table>
            <thead>
              <tr>
                <th>{t('common.date')}</th><th>{t('vault.kind')}</th><th>{t('vault.engine')}</th><th>{t('vault.size')}</th>
                <th>{t('common.duration')}</th><th>{t('common.status')}</th><th>{t('vault.verifiedCol')}</th><th style={{ textAlign: 'right' }}>{t('common.action')}</th>
              </tr>
            </thead>
            <tbody>
              {backups.map(b => (
                <tr key={b.id}>
                  <td className="nowrap">{dt(b.createdAt)}</td>
                  <td className="nowrap">{t(KIND_KEYS[b.kind])}</td>
                  <td className="nowrap">{b.engine}</td>
                  <td className="nowrap">{b.status === 'Succeeded' ? formatSize(b.sizeBytes) : '—'}</td>
                  <td className="nowrap">{formatDuration(b.createdAt, b.completedAt) ?? '—'}</td>
                  <td className="nowrap">
                    {b.status === 'Succeeded' ? t('common.done')
                      : b.status === 'Running' ? t('common.running')
                      // The error text lives right in the row: the user shouldn't
                      // have to go to another screen to ask "why did this fail".
                      : <span title={b.error ?? undefined}>{t('common.failed')}</span>}
                  </td>
                  <td className="nowrap">
                    {/* Three distinct states: proven / found broken / never tried.
                        Merging the last two would make a broken backup look merely unverified. */}
                    {b.verifiedAt ? (
                      <span title={t('vault.verifiedAt', { date: dt(b.verifiedAt) })}>{t('vault.verifiedBadge')}</span>
                    ) : b.verifyError ? (
                      <span title={b.verifyError}>{t('vault.broken')}</span>
                    ) : '—'}
                  </td>
                  <td>
                    <div className="row-actions" style={{ justifyContent: 'flex-end' }}>
                      {/* Verification runs on a throwaway server — it never touches
                          the project's database, so no Owner requirement here. */}
                      {b.status === 'Succeeded' && (
                        <button className="btn btn-sm" disabled={rowBusyId === b.id || busy}
                                onClick={() => handleVerify(b)}>
                          {rowBusyId === b.id ? t('vault.verifying') : t('vault.verify')}
                        </button>
                      )}
                      {b.status === 'Succeeded' && isOwner && (
                        <>
                          <button className="btn btn-sm" disabled={rowBusyId === b.id || busy}
                                  onClick={() => handleDownload(b)}>
                            {t('vault.download')}
                          </button>
                          <button className="btn btn-sm btn-danger" disabled={busy}
                                  onClick={() => setConfirming({ backup: b, typed: '' })}>
                            {t('vault.restore')}
                          </button>
                          <button className="btn btn-sm" disabled={rowBusyId === b.id || busy}
                                  onClick={() => handleDelete(b)}>
                            {t('common.delete')}
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
            title={t('vault.historyTitle')}
            desc={t('vault.historyDesc')}
          />
          <div className="grid-wrap">
            <table>
              <thead>
                <tr><th>{t('common.date')}</th><th>{t('vault.sourceBackup')}</th><th>{t('common.duration')}</th><th>{t('common.status')}</th></tr>
              </thead>
              <tbody>
                {restores.map(r => (
                  <tr key={r.id}>
                    <td className="nowrap">{dt(r.startedAt)}</td>
                    <td className="nowrap"><code>{r.backupId.slice(0, 8)}</code></td>
                    <td className="nowrap">{formatDuration(r.startedAt, r.completedAt) ?? '—'}</td>
                    <td className="nowrap">
                      {r.status === 'Succeeded' ? t('common.done')
                        : r.status === 'Running' ? t('common.running')
                        : <span title={r.error ?? undefined}>{t('common.failed')}</span>}
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
