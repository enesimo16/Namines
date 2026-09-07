'use client';

import { useCallback, useEffect, useState } from 'react';
import { type DeskSession } from '../lib/api';
import { apiKeysApi, ApiKeysError, type GatewayApiKeySummary } from '../lib/apiKeys';
import PageHead from './PageHead';

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
  const [keys, setKeys] = useState<GatewayApiKeySummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [newName, setNewName] = useState('');
  const [newCanWrite, setNewCanWrite] = useState(false);
  const [creating, setCreating] = useState(false);
  const [revealedKey, setRevealedKey] = useState<{ name: string; key: string; warning: string } | null>(null);
  const [revokingId, setRevokingId] = useState<string | null>(null);

  const reload = useCallback(async () => {
    try {
      const list = await apiKeysApi.list(session);
      setKeys(list);
      setError(null);
    } catch (err) {
      setError(err instanceof ApiKeysError ? err.message : 'API anahtarları okunamadı.');
    }
  }, [session]);

  useEffect(() => { reload(); }, [reload]);

  if (!isOwner) {
    return (
      <div className="notice" style={{ margin: 18 }}>
        API anahtarları yalnızca proje sahibi (Owner) tarafından yönetilebilir.
      </div>
    );
  }

  async function handleCreate() {
    if (!newName.trim()) return;
    setCreating(true);
    setError(null);
    try {
      const created = await apiKeysApi.create(session, newName.trim(), newCanWrite);
      setRevealedKey({ name: created.name, key: created.key, warning: created.warning });
      setNewName('');
      setNewCanWrite(false);
      await reload();
    } catch (err) {
      setError(err instanceof ApiKeysError ? err.message : 'Oluşturulamadı.');
    } finally {
      setCreating(false);
    }
  }

  async function handleRevoke(k: GatewayApiKeySummary) {
    if (!confirm(`"${k.name}" anahtarı iptal edilecek. Bu anahtarla yapılan istekler artık reddedilir. Emin misiniz?`)) return;
    setRevokingId(k.id);
    try {
      await apiKeysApi.revoke(session, k.id);
      await reload();
    } catch (err) {
      setError(err instanceof ApiKeysError ? err.message : 'İptal edilemedi.');
    } finally {
      setRevokingId(null);
    }
  }

  return (
    <div className="page">
      <PageHead
        title="API anahtarları"
        desc={<>
          Dış uygulamaların Gateway API&apos;ye doğrudan bağlanması için — Desk&apos;in kendi
          oturumundan bağımsız. <b>Ham anahtar yalnızca oluşturulduğu anda gösterilir;</b>
          sunucu yalnızca bir hash saklar ve onu bir daha döndürmez.
        </>}
      />

      {error && <div className="notice notice-error">{error}</div>}

      {revealedKey && (
        <div className="notice" style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
          <b>&quot;{revealedKey.name}&quot; oluşturuldu.</b>
          <code style={{
            display: 'block', padding: 8, background: 'var(--surface-900)',
            border: '1px solid var(--line-strong)', borderRadius: 'var(--radius-control)',
            fontSize: 12.5, wordBreak: 'break-all',
          }}>
            {revealedKey.key}
          </code>
          <span>{revealedKey.warning}</span>
          <button className="btn btn-sm" style={{ alignSelf: 'flex-start' }} onClick={() => setRevealedKey(null)}>
            Anladım, kapat
          </button>
        </div>
      )}

      <div className="grid-wrap" style={{ padding: 10, display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'flex-end' }}>
        <div className="field" style={{ margin: 0 }}>
          <label htmlFor="new-key-name">Ad</label>
          <input id="new-key-name" type="text" value={newName} style={{ width: 200 }}
                 placeholder="ör. CI pipeline" onChange={e => setNewName(e.target.value)} />
        </div>
        <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 13 }}>
          <input type="checkbox" checked={newCanWrite} onChange={e => setNewCanWrite(e.target.checked)} />
          Yazma izni
        </label>
        <button className="btn btn-primary" disabled={creating || !newName.trim()} onClick={handleCreate}>
          {creating ? 'Oluşturuluyor…' : 'Yeni anahtar oluştur'}
        </button>
      </div>

      {!keys ? (
        <div className="empty">Yükleniyor…</div>
      ) : keys.length === 0 ? (
        <div className="empty">Bu projede henüz bir API anahtarı yok.</div>
      ) : (
        <div className="grid-wrap">
          <table>
            <thead>
              <tr>
                <th>Ad</th><th>Önek</th><th>Yazma</th><th>Oran sınırı</th>
                <th>Oluşturulma</th><th>Son kullanım</th><th>Durum</th><th style={{ textAlign: 'right' }}>İşlem</th>
              </tr>
            </thead>
            <tbody>
              {keys.map(k => (
                <tr key={k.id}>
                  <td>{k.name}</td>
                  <td><code>{k.prefix}…</code></td>
                  <td>{k.canWrite ? '✓' : '✗'}</td>
                  <td>{k.rateLimitPerMinute}/dk</td>
                  <td>{new Date(k.createdAt).toLocaleString('tr-TR')}</td>
                  <td>{k.lastUsedAt ? new Date(k.lastUsedAt).toLocaleString('tr-TR') : '—'}</td>
                  <td>{k.revokedAt ? 'İptal edildi' : (k.expiresAt && new Date(k.expiresAt) < new Date()) ? 'Süresi doldu' : 'Etkin'}</td>
                  <td>
                    <div className="row-actions" style={{ justifyContent: 'flex-end' }}>
                      {!k.revokedAt && (
                        <button className="btn btn-sm btn-danger" disabled={revokingId === k.id} onClick={() => handleRevoke(k)}>
                          {revokingId === k.id ? 'İptal ediliyor…' : 'İptal et'}
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
