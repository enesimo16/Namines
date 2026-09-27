'use client';

import { useCallback, useEffect, useState } from 'react';
import EmptyState from './EmptyState';
import { type DeskSession } from '../lib/api';
import { apiKeysApi, ApiKeysError, type GatewayApiKeySummary } from '../lib/apiKeys';
import PageHead from './PageHead';
import { useI18n } from '../lib/i18n';
import { rich } from '../lib/rich';

/**
 * Namines Desk v2 §E1.1 — API anahtarı yönetim ekranı.
 *
 * Backend zaten tamamen hazır (GatewayKeyController: List/Create/Revoke,
 * Admin ve üstü yetkili) — burası tamamen bir arayüz işi. Desk şu an
 * Owner'dan ayrı bir "Admin" sinyali taşımıyor (yalnızca isOwner), bu yüzden
 * bu sekme de SQL sekmesiyle aynı prensiple isOwner'a gösteriliyor; sunucu
 * zaten kendi CanManageAsync (Admin+) kontrolünü ayrıca yapıyor.
 *
 * Ham anahtar yalnızca oluşturma anında BİR KEZ gösterilir — sunucu onu
 * bir daha asla döndürmez (yalnızca prefix saklanır), bu yüzden burada da
 * geri getirilemez; kullanıcı kopyalamazsa anahtar kaybolur.
 */
export default function ApiKeys({ session, isOwner }: { session: DeskSession; isOwner: boolean }) {
  const { t, dt } = useI18n();
  const [keys, setKeys] = useState<GatewayApiKeySummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [newName, setNewName] = useState('');
  const [newCanWrite, setNewCanWrite] = useState(false);
  // Disa aktarim AYRI bir izin (F-08): okuma yetkisi olan bir anahtarin
  // tablonun tamamini indirebilmesi, veri sizintisinin en sessiz yolu.
  const [newCanExport, setNewCanExport] = useState(false);
  const [creating, setCreating] = useState(false);
  const [revealedKey, setRevealedKey] = useState<{ name: string; key: string; warning: string } | null>(null);
  const [revokingId, setRevokingId] = useState<string | null>(null);

  const reload = useCallback(async () => {
    try {
      const list = await apiKeysApi.list(session);
      setKeys(list);
      setError(null);
    } catch (err) {
      setError(err instanceof ApiKeysError ? err.message : t('apiKeys.loadFailed'));
    }
  }, [session, t]);

  useEffect(() => { reload(); }, [reload]);

  if (!isOwner) {
    return (
      <div className="notice" style={{ margin: 18 }}>
        {t('apiKeys.ownerOnly')}
      </div>
    );
  }

  async function handleCreate() {
    if (!newName.trim()) return;
    setCreating(true);
    setError(null);
    try {
      const created = await apiKeysApi.create(session, newName.trim(), newCanWrite, newCanExport);
      setRevealedKey({ name: created.name, key: created.key, warning: created.warning });
      setNewName('');
      setNewCanWrite(false);
      setNewCanExport(false);
      await reload();
    } catch (err) {
      setError(err instanceof ApiKeysError ? err.message : t('apiKeys.createFailed'));
    } finally {
      setCreating(false);
    }
  }

  async function handleRevoke(k: GatewayApiKeySummary) {
    if (!confirm(t('apiKeys.confirmRevoke', { name: k.name }))) return;
    setRevokingId(k.id);
    try {
      await apiKeysApi.revoke(session, k.id);
      await reload();
    } catch (err) {
      setError(err instanceof ApiKeysError ? err.message : t('apiKeys.revokeFailed'));
    } finally {
      setRevokingId(null);
    }
  }

  return (
    <div className="page">
      <PageHead
        title={t('nav.views.apikeys')}
        desc={rich(t('apiKeys.desc'))}
      />

      {error && <div className="notice notice-error">{error}</div>}

      {revealedKey && (
        <div className="notice" style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
          <b>{t('apiKeys.createdTitle', { name: revealedKey.name })}</b>
          <code style={{
            display: 'block', padding: 8, background: 'var(--surface-900)',
            border: '1px solid var(--line-strong)', borderRadius: 'var(--radius-control)',
            fontSize: 12.5, wordBreak: 'break-all',
          }}>
            {revealedKey.key}
          </code>
          <span>{revealedKey.warning}</span>
          <button className="btn btn-sm" style={{ alignSelf: 'flex-start' }} onClick={() => setRevealedKey(null)}>
            {t('apiKeys.gotIt')}
          </button>
        </div>
      )}

      <div className="grid-wrap" style={{ padding: 10, display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'flex-end' }}>
        <div className="field" style={{ margin: 0 }}>
          <label htmlFor="new-key-name">{t('apiKeys.name')}</label>
          <input id="new-key-name" type="text" value={newName} style={{ width: 200 }}
                 placeholder={t('apiKeys.namePlaceholder')} onChange={e => setNewName(e.target.value)} />
        </div>
        <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 13 }}>
          <input type="checkbox" checked={newCanWrite} onChange={e => setNewCanWrite(e.target.checked)} />
          {t('apiKeys.writeAccess')}
        </label>
        <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 13 }}
               title={t('apiKeys.exportTitle')}>
          <input type="checkbox" checked={newCanExport} onChange={e => setNewCanExport(e.target.checked)} />
          {t('apiKeys.exportAccess')}
        </label>
        <button className="btn btn-primary" disabled={creating || !newName.trim()} onClick={handleCreate}>
          {creating ? t('apiKeys.creating') : t('apiKeys.create')}
        </button>
      </div>

      {!keys ? (
        <div className="empty">{t('common.loading')}</div>
      ) : keys.length === 0 ? (
        <EmptyState
          title={t('apiKeys.emptyTitle')}
          description={t('apiKeys.emptyDesc')}
        />
      ) : (
        <div className="grid-wrap">
          <table>
            <thead>
              <tr>
                <th>{t('apiKeys.name')}</th><th>{t('apiKeys.prefix')}</th><th>{t('apiKeys.write')}</th><th>{t('apiKeys.export')}</th><th>{t('apiKeys.rateLimit')}</th>
                <th>{t('common.created')}</th><th>{t('apiKeys.lastUsed')}</th><th>{t('common.status')}</th><th style={{ textAlign: 'right' }}>{t('common.action')}</th>
              </tr>
            </thead>
            <tbody>
              {keys.map(k => (
                <tr key={k.id}>
                  <td className="nowrap">{k.name}</td>
                  <td className="nowrap"><code>{k.prefix}…</code></td>
                  <td className="nowrap">{k.canWrite ? '✓' : '✗'}</td>
                  <td className="nowrap">{k.canExport ? '✓' : '✗'}</td>
                  <td className="nowrap">{t('apiKeys.perMinute', { count: k.rateLimitPerMinute })}</td>
                  <td className="nowrap">{dt(k.createdAt)}</td>
                  <td className="nowrap">{k.lastUsedAt ? dt(k.lastUsedAt) : '—'}</td>
                  <td className="nowrap">{k.revokedAt ? t('apiKeys.revoked') : (k.expiresAt && new Date(k.expiresAt) < new Date()) ? t('apiKeys.expired') : t('common.active')}</td>
                  <td>
                    <div className="row-actions" style={{ justifyContent: 'flex-end' }}>
                      {!k.revokedAt && (
                        <button className="btn btn-sm btn-danger" disabled={revokingId === k.id} onClick={() => handleRevoke(k)}>
                          {revokingId === k.id ? t('apiKeys.revoking') : t('apiKeys.revoke')}
                        </button>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
