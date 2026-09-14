'use client';

import { useCallback, useEffect, useState } from 'react';
import EmptyState from './EmptyState';
import { type DeskSession } from '../lib/api';
import { formatSize } from '../lib/format';
import {
  groundApi, GroundError, daysUntilPurge,
  type GroundDatabase, type GroundProvider, type GroundMetrics,
} from '../lib/ground';
import PageHead from './PageHead';

/**
 * Namines Ground — managed database screen.
 *
 * Ground has no site of its own; this view is its only user-facing surface
 * (see `namines-ground/02-V1-KARARLARI.md`). Deletion is the second most
 * destructive action in Desk (after Vault's restore): it wipes the whole
 * database, so the project name has to be typed by hand and deletion goes
 * through a recoverable grace window instead of happening instantly.
 */
export default function Ground({ session, isOwner }: { session: DeskSession; isOwner: boolean }) {
  const [providers, setProviders] = useState<GroundProvider[] | null>(null);
  const [database, setDatabase] = useState<GroundDatabase | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [confirmText, setConfirmText] = useState('');
  const [confirming, setConfirming] = useState(false);
  const [metrics, setMetrics] = useState<GroundMetrics | null>(null);

  const reload = useCallback(async () => {
    try {
      const [list, record] = await Promise.all([
        groundApi.providers(session), groundApi.get(session),
      ]);
      setProviders(list);
      setDatabase(record);
      setError(null);

      // Metrics is a SEPARATE call whose failure is swallowed: it hits the
      // provider with a real query, and a hiccup there shouldn't take down
      // the whole screen.
      if (record && record.status !== 'Deleted') {
        try {
          setMetrics(await groundApi.metrics(session));
        } catch {
          setMetrics(null);
        }
      } else {
        setMetrics(null);
      }
    } catch (err) {
      setError(err instanceof GroundError ? err.message : 'Could not load the managed database.');
      setProviders([]);
    }
  }, [session]);

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
      setError(err instanceof GroundError ? err.message : 'The action failed.');
    } finally {
      setBusy(false);
    }
  }

  const handleProvision = (provider: string) =>
    run(() => groundApi.provision(session, provider), 'Managed database created.');

  const handleDelete = () =>
    run(async () => {
      await groundApi.requestDelete(session, confirmText);
      setConfirming(false);
      setConfirmText('');
    }, 'Deletion requested. You can still undo it during the grace window.');

  const handleCancelDelete = () =>
    run(() => groundApi.cancelDelete(session), 'Deletion cancelled — the database is back in use.');

  const remainingDays = database?.deleteRequestedAt
    ? daysUntilPurge(database.deleteRequestedAt, database.graceDays)
    : null;

  return (
    <div className="page">
      <PageHead
        title="Namines Ground"
        desc="A managed PostgreSQL database for this project. The connection stays encrypted on the server and never reaches the browser."
      />

      {error && <div className="notice notice-error">{error}</div>}
      {notice && <div className="notice">{notice}</div>}

      {!isOwner && (
        <div className="notice">Only the project Owner can create or delete a managed database.</div>
      )}

      {/* WARNING only — the database is never throttled or shut off. */}
      {metrics?.storageWarning && (
        <div className="notice">{metrics.storageWarning}</div>
      )}

      {database && database.status !== 'Deleted' ? (
        <div className="grid-wrap">
          <table>
            <tbody>
              <tr><th style={{ width: 160 }}>Status</th><td className="nowrap">{STATUS_LABELS[database.status]}</td></tr>
              <tr><th>Provider</th><td className="nowrap">{database.provider}</td></tr>
              <tr>
                <th>Provider ID</th>
                {/* So the resource can be found in the provider's own console. */}
                <td><code>{database.providerProjectId ?? '—'}</code></td>
              </tr>
              <tr><th>Region</th><td className="nowrap">{database.region ?? '—'}</td></tr>
              <tr><th>Created</th><td className="nowrap">{new Date(database.createdAt).toLocaleString('en-US')}</td></tr>
              {/* null = unknown, not zero. "0 B" would look like an empty database. */}
              <tr>
                <th>Storage used</th>
                <td className="nowrap">{metrics?.storageBytes != null ? formatSize(metrics.storageBytes) : '—'}</td>
              </tr>
              <tr>
                <th>Active connections</th>
                <td className="nowrap">{metrics?.activeConnections ?? '—'}</td>
              </tr>
              {database.error && (
                <tr><th>Error</th><td>{database.error}</td></tr>
              )}
            </tbody>
          </table>
        </div>
      ) : null}

      {database?.status === 'PendingDelete' && (
        <div className="notice notice-error" style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
          <b>This database is scheduled for deletion.</b>
          <span>
            {remainingDays === 0
              ? 'The grace window has ended; it will be permanently deleted on the next cleanup pass.'
              : `${remainingDays} day${remainingDays === 1 ? '' : 's'} left before it's permanently deleted. You can still undo it.`}
          </span>
          {isOwner && (
            <button className="btn" style={{ alignSelf: 'flex-start' }} disabled={busy}
                    onClick={handleCancelDelete}>
              {busy ? 'Undoing…' : 'Undo deletion'}
            </button>
          )}
        </div>
      )}

      {database?.status === 'Active' && isOwner && (
        confirming ? (
          <div className="notice notice-error" style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
            <b>This database and everything in it will be deleted.</b>
            <span>Deletion isn&apos;t instant — you can undo it during the grace window. Type the project name to continue.</span>
            <input type="text" aria-label="Project name to confirm" value={confirmText} style={{ maxWidth: 280 }}
                   onChange={e => setConfirmText(e.target.value)} />
            <div className="row-actions">
              <button className="btn btn-danger" disabled={busy || !confirmText.trim()}
                      onClick={handleDelete}>
                {busy ? 'Requesting…' : 'Start deletion'}
              </button>
              <button className="btn btn-sm" disabled={busy}
                      onClick={() => { setConfirming(false); setConfirmText(''); }}>
                Cancel
              </button>
            </div>
          </div>
        ) : (
          <div className="row-actions">
            <button className="btn btn-danger btn-sm" onClick={() => setConfirming(true)}>
              Delete database
            </button>
          </div>
        )
      )}

      {!database || database.status === 'Deleted' ? (
        <>
          <PageHead
            title="Providers"
            desc="Choose where the database lives — each provider carries different responsibilities."
          />

          {!providers ? (
            <div className="empty">Loading…</div>
          ) : providers.length === 0 ? (
            <EmptyState
              title="No providers registered"
              description={<>No provider is configured on the server yet — an admin needs to set the <code>Ground__*</code> settings.</>}
            />
          ) : (
            <div className="grid-wrap">
              {/* `minWidth` yerine sıfırdan güvenmek: dar bir görünüm alanında sabit
                  sütun genişlikleri (140+130+140) "Responsibility" sütununu birkaç
                  piksele sıkıştırıp her kelimeyi kendi satırına düşürüyordu — üst
                  üste binmekten farklı ama aynı derecede okunamaz bir bozulma. Tablo
                  artık kendi minimum genişliğini taşıyor; `.grid-wrap`'in zaten
                  sahip olduğu `overflow: auto` dar ekranda yatay kaydırmayı
                  üstleniyor, sütunlar hiç sıkışmıyor. */}
              <table style={{ tableLayout: 'fixed', minWidth: 640 }}>
                <colgroup>
                  <col style={{ width: 140 }} />
                  <col style={{ width: 130 }} />
                  <col />
                  <col style={{ width: 140 }} />
                </colgroup>
                <thead>
                  <tr>
                    <th>Provider</th><th>Status</th><th>Responsibility</th>
                    <th style={{ textAlign: 'right' }}>Action</th>
                  </tr>
                </thead>
                <tbody>
                  {providers.map(p => (
                    <tr key={p.name}>
                      <td>{p.name}</td>
                      <td>
                        {/* Three distinct states: ready / not configured / unverified.
                            Hiding "not live-verified" would let a user trust an
                            untested path without knowing it. NOT `.nowrap`: a narrow
                            fixed column plus a forced single line pushed text past the
                            cell boundary and over the next column instead of wrapping. */}
                        {p.problem
                          ? <span title={p.problem}>Not configured</span>
                          : p.liveVerified
                            ? 'Ready'
                            : <span title="This provider has never been tested against a real account.">
                                ⚠ Unverified
                              </span>}
                      </td>
                      <td style={{ fontSize: 12.5 }}>{p.responsibility}</td>
                      <td>
                        <div className="row-actions" style={{ justifyContent: 'flex-end' }}>
                          <button
                            className="btn btn-sm"
                            disabled={busy || !isOwner || p.problem !== null}
                            title={p.problem ?? undefined}
                            onClick={() => handleProvision(p.name)}
                          >
                            {busy ? 'Creating…' : 'Use this provider'}
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </>
      ) : null}
    </div>
  );
}

const STATUS_LABELS: Record<GroundDatabase['status'], string> = {
  Provisioning: 'Creating…',
  Active: 'Active',
  PendingDelete: 'Pending deletion',
  Deleted: 'Deleted',
  Failed: 'Failed',
};
