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
import { useI18n, type MessageKey } from '../lib/i18n';
import { rich } from '../lib/rich';

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
  const { t, dt } = useI18n();
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
      setError(err instanceof GroundError ? err.message : t('ground.loadFailed'));
      setProviders([]);
    }
  }, [session, t]);

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
      setError(err instanceof GroundError ? err.message : t('ground.actionFailed'));
    } finally {
      setBusy(false);
    }
  }

  const handleProvision = (provider: string) =>
    run(() => groundApi.provision(session, provider), t('ground.provisioned'));

  const handleDelete = () =>
    run(async () => {
      await groundApi.requestDelete(session, confirmText);
      setConfirming(false);
      setConfirmText('');
    }, t('ground.deleteRequested'));

  const handleCancelDelete = () =>
    run(() => groundApi.cancelDelete(session), t('ground.deleteCancelled'));

  const remainingDays = database?.deleteRequestedAt
    ? daysUntilPurge(database.deleteRequestedAt, database.graceDays)
    : null;

  return (
    <div className="page">
      <PageHead
        title="Namines Ground"
        desc={t('ground.desc')}
      />

      {error && <div className="notice notice-error">{error}</div>}
      {notice && <div className="notice">{notice}</div>}

      {!isOwner && (
        <div className="notice">{t('ground.ownerOnly')}</div>
      )}

      {/* WARNING only — the database is never throttled or shut off. */}
      {metrics?.storageWarning && (
        <div className="notice">{metrics.storageWarning}</div>
      )}

      {database && database.status !== 'Deleted' ? (
        <div className="grid-wrap">
          <table>
            <tbody>
              <tr><th style={{ width: 160 }}>{t('common.status')}</th><td className="nowrap">{t(STATUS_KEYS[database.status])}</td></tr>
              <tr><th>{t('ground.provider')}</th><td className="nowrap">{database.provider}</td></tr>
              <tr>
                <th>{t('ground.providerId')}</th>
                {/* So the resource can be found in the provider's own console. */}
                <td><code>{database.providerProjectId ?? '—'}</code></td>
              </tr>
              <tr><th>{t('ground.region')}</th><td className="nowrap">{database.region ?? '—'}</td></tr>
              <tr><th>{t('common.created')}</th><td className="nowrap">{dt(database.createdAt)}</td></tr>
              {/* null = unknown, not zero. "0 B" would look like an empty database. */}
              <tr>
                <th>{t('ground.storageUsed')}</th>
                <td className="nowrap">{metrics?.storageBytes != null ? formatSize(metrics.storageBytes) : '—'}</td>
              </tr>
              <tr>
                <th>{t('ground.activeConnections')}</th>
                <td className="nowrap">{metrics?.activeConnections ?? '—'}</td>
              </tr>
              {database.error && (
                <tr><th>{t('ground.error')}</th><td>{database.error}</td></tr>
              )}
            </tbody>
          </table>
        </div>
      ) : null}

      {database?.status === 'PendingDelete' && (
        <div className="notice notice-error" style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
          <b>{t('ground.scheduled')}</b>
          <span>
            {remainingDays === 0
              ? t('ground.graceEnded')
              : t(remainingDays === 1 ? 'ground.daysLeftOne' : 'ground.daysLeftOther', { count: remainingDays ?? 0 })}
          </span>
          {isOwner && (
            <button className="btn" style={{ alignSelf: 'flex-start' }} disabled={busy}
                    onClick={handleCancelDelete}>
              {busy ? t('ground.undoing') : t('ground.undo')}
            </button>
          )}
        </div>
      )}

      {database?.status === 'Active' && isOwner && (
        <div
          style={{
            border: '1px solid color-mix(in srgb, var(--danger) 40%, var(--line))',
            borderRadius: 'var(--r)',
            background: 'var(--bg-elevated)',
            overflow: 'hidden',
          }}
        >
          <div
            style={{
              display: 'flex',
              flexWrap: 'wrap',
              alignItems: 'center',
              justifyContent: 'space-between',
              gap: 12,
              padding: '14px 16px',
              background: 'color-mix(in srgb, var(--danger-bg) 55%, var(--bg-elevated))',
            }}
          >
            <div style={{ minWidth: 0, flex: '1 1 240px' }}>
              <div style={{ fontSize: 12, fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.04em', color: 'var(--ink)' }}>
                {t('ground.deleteDatabase')}
              </div>
              <div style={{ fontSize: 12, color: 'var(--ink-2)', marginTop: 4 }}>
                {t('ground.deleteNote')}
              </div>
            </div>
            {!confirming && (
              <button className="btn btn-danger btn-sm" onClick={() => setConfirming(true)}>
                {t('ground.deleteDatabase')}
              </button>
            )}
          </div>
          {confirming && (
            <div style={{ borderTop: '1px solid color-mix(in srgb, var(--danger) 25%, var(--line))', padding: '14px 16px', display: 'flex', flexDirection: 'column', gap: 10 }}>
              <b style={{ fontSize: 12 }}>{t('ground.willDelete')}</b>
              <input
                type="text"
                aria-label={t('ground.confirmLabel')}
                value={confirmText}
                style={{ maxWidth: 320 }}
                onChange={e => setConfirmText(e.target.value)}
              />
              <div className="row-actions">
                <button className="btn btn-danger" disabled={busy || !confirmText.trim()} onClick={handleDelete}>
                  {busy ? t('ground.requesting') : t('ground.startDeletion')}
                </button>
                <button className="btn btn-sm" disabled={busy} onClick={() => { setConfirming(false); setConfirmText(''); }}>
                  {t('common.cancel')}
                </button>
              </div>
            </div>
          )}
        </div>
      )}

      {!database || database.status === 'Deleted' ? (
        <>
          <PageHead
            title={t('ground.providers')}
            desc={t('ground.providersDesc')}
          />

          {!providers ? (
            <div className="empty">{t('common.loading')}</div>
          ) : providers.length === 0 ? (
            <EmptyState
              title={t('ground.noProvidersTitle')}
              description={rich(t('ground.noProvidersDesc'))}
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
                    <th>{t('ground.provider')}</th><th>{t('common.status')}</th><th>{t('ground.responsibility')}</th>
                    <th style={{ textAlign: 'right' }}>{t('common.action')}</th>
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
                          ? <span title={p.problem}>{t('ground.notConfigured')}</span>
                          : p.liveVerified
                            ? t('ground.ready')
                            : <span title={t('ground.unverifiedTitle')}>
                                {t('ground.unverified')}
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
                            {busy ? t('ground.creating') : t('ground.useProvider')}
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

const STATUS_KEYS: Record<GroundDatabase['status'], MessageKey> = {
  Provisioning: 'ground.statuses.Provisioning',
  Active: 'ground.statuses.Active',
  PendingDelete: 'ground.statuses.PendingDelete',
  Deleted: 'ground.statuses.Deleted',
  Failed: 'ground.statuses.Failed',
};
