'use client';

import { useCallback, useEffect, useState } from 'react';
import EmptyState from './EmptyState';
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
      setError(err instanceof ApiKeysError ? err.message : 'Could not load API keys.');
    }
  }, [session]);

  useEffect(() => { reload(); }, [reload]);

  if (!isOwner) {
    return (
      <div className="notice" style={{ margin: 18 }}>
        API keys can only be managed by the project Owner.
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
      setError(err instanceof ApiKeysError ? err.message : 'Could not create the key.');
    } finally {
      setCreating(false);
    }
  }

  async function handleRevoke(k: GatewayApiKeySummary) {
    if (!confirm(`The key "${k.name}" will be revoked. Requests using it will be rejected. Continue?`)) return;
    setRevokingId(k.id);
    try {
      await apiKeysApi.revoke(session, k.id);
      await reload();
    } catch (err) {
      setError(err instanceof ApiKeysError ? err.message : 'Could not revoke the key.');
    } finally {
      setRevokingId(null);
    }
  }

  return (
    <div className="page">
      <PageHead
        title="API keys"
        desc={<>
          For external apps connecting straight to the Gateway API, independent of Desk&apos;s own
          session. <b>The raw key is shown only once, at creation</b> — the server keeps just a
          hash and never returns it again.
        </>}
      />

      {error && <div className="notice notice-error">{error}</div>}

      {revealedKey && (
        <div className="notice" style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
          <b>&quot;{revealedKey.name}&quot; created.</b>
          <code style={{
            display: 'block', padding: 8, background: 'var(--surface-900)',
            border: '1px solid var(--line-strong)', borderRadius: 'var(--radius-control)',
            fontSize: 12.5, wordBreak: 'break-all',
          }}>
            {revealedKey.key}
          </code>
          <span>{revealedKey.warning}</span>
          <button className="btn btn-sm" style={{ alignSelf: 'flex-start' }} onClick={() => setRevealedKey(null)}>
            Got it, close
          </button>
        </div>
      )}

      <div className="grid-wrap" style={{ padding: 10, display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'flex-end' }}>
        <div className="field" style={{ margin: 0 }}>
          <label htmlFor="new-key-name">Name</label>
          <input id="new-key-name" type="text" value={newName} style={{ width: 200 }}
                 placeholder="e.g. CI pipeline" onChange={e => setNewName(e.target.value)} />
        </div>
        <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 13 }}>
          <input type="checkbox" checked={newCanWrite} onChange={e => setNewCanWrite(e.target.checked)} />
          Write access
        </label>
        <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 13 }}
               title="Download an entire table as CSV/JSON. Separate from read access, and must also be enabled per table.">
          <input type="checkbox" checked={newCanExport} onChange={e => setNewCanExport(e.target.checked)} />
          Export access
        </label>
        <button className="btn btn-primary" disabled={creating || !newName.trim()} onClick={handleCreate}>
          {creating ? 'Creating…' : 'Create new key'}
        </button>
      </div>

      {!keys ? (
        <div className="empty">Loading…</div>
      ) : keys.length === 0 ? (
        <EmptyState
          title="No API keys yet"
          description="An API key lets your applications access this project's data — each key carries per-table read/write access, and every write it makes is logged. Create the first one above."
        />
      ) : (
        <div className="grid-wrap">
          <table>
            <thead>
              <tr>
                <th>Name</th><th>Prefix</th><th>Write</th><th>Export</th><th>Rate limit</th>
                <th>Created</th><th>Last used</th><th>Status</th><th style={{ textAlign: 'right' }}>Action</th>
              </tr>
            </thead>
            <tbody>
              {keys.map(k => (
                <tr key={k.id}>
                  <td className="nowrap">{k.name}</td>
                  <td className="nowrap"><code>{k.prefix}…</code></td>
                  <td className="nowrap">{k.canWrite ? '✓' : '✗'}</td>
                  <td className="nowrap">{k.canExport ? '✓' : '✗'}</td>
                  <td className="nowrap">{k.rateLimitPerMinute}/min</td>
                  <td className="nowrap">{new Date(k.createdAt).toLocaleString('en-US')}</td>
                  <td className="nowrap">{k.lastUsedAt ? new Date(k.lastUsedAt).toLocaleString('en-US') : '—'}</td>
                  <td className="nowrap">{k.revokedAt ? 'Revoked' : (k.expiresAt && new Date(k.expiresAt) < new Date()) ? 'Expired' : 'Active'}</td>
                  <td>
                    <div className="row-actions" style={{ justifyContent: 'flex-end' }}>
                      {!k.revokedAt && (
                        <button className="btn btn-sm btn-danger" disabled={revokingId === k.id} onClick={() => handleRevoke(k)}>
                          {revokingId === k.id ? 'Revoking…' : 'Revoke'}
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
