'use client';

import { useEffect, useState } from 'react';
import { type DeskSession } from '../lib/api';
import PageHead from './PageHead';
import { fetchAuditLog, LogsAccessError, type AuditLogEntry, type GatewayWriteKind } from '../lib/logs';

const PAGE_SIZE = 50;
const ALL_KINDS: GatewayWriteKind[] = ['create', 'update', 'delete', 'import', 'rpc', 'sql', 'read'];

/**
 * D6 — Logs (namines_desk/06-LOGS.md). ⚠️ Ekranın adı bilerek "Yazma işlemleri"
 * diyor, "tüm istekler" demiyor — `GatewayAuditEntry` yalnızca yazmaları
 * kaydediyor, okuma hiç kaydedilmiyor (§1). Bu ekran bunu gizlemez.
 */
export default function Logs({ session }: { session: DeskSession }) {
  const [kinds, setKinds] = useState<Set<GatewayWriteKind>>(new Set());
  const [tableName, setTableName] = useState('');
  const [onlyFailed, setOnlyFailed] = useState(false);
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [page, setPage] = useState(1);

  const [entries, setEntries] = useState<AuditLogEntry[] | null>(null);
  const [totalCount, setTotalCount] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [forbidden, setForbidden] = useState(false);
  const [selected, setSelected] = useState<AuditLogEntry | null>(null);

  useEffect(() => {
    let cancelled = false;
    setError(null);
    setForbidden(false);
    fetchAuditLog(session, session.projectId, page, PAGE_SIZE, {
      kinds: kinds.size > 0 ? Array.from(kinds) : undefined,
      tableName: tableName.trim() || undefined,
      succeeded: onlyFailed ? false : undefined,
      from: from || undefined,
      to: to || undefined,
    })
      .then(res => { if (!cancelled) { setEntries(res.entries); setTotalCount(res.totalCount); } })
      .catch(err => {
        if (cancelled) return;
        if (err instanceof LogsAccessError && err.status === 403) { setForbidden(true); setEntries([]); return; }
        setError(err instanceof Error ? err.message : 'Kayıtlar okunamadı.');
      });
    return () => { cancelled = true; };
  }, [session, page, kinds, tableName, onlyFailed, from, to]);

  function toggleKind(k: GatewayWriteKind) {
    setPage(1);
    setKinds(prev => {
      const next = new Set(prev);
      if (next.has(k)) next.delete(k); else next.add(k);
      return next;
    });
  }

  // Erken donusler de basligi TASIR — basliksiz bir ekran "yanlis yere mi
  // geldim?" sorusunu birakiyordu.
  const head = (
    <PageHead
      title="Kayıtlar"
      desc={<>
        Bu projedeki <b>yazma işlemlerinin</b> denetim kaydı — kim, ne zaman, hangi
        tabloda ne değiştirdi. Sıradan okumalar burada YOK; tek istisna
        <b> maskeli kolon içeren tablolardan yapılan okumalar</b>: maskeleme
        konulmuş bir kolona kimin eriştiği kaydediliyor. Onları <i>READ</i>
        türüyle filtreleyebilirsiniz.
      </>}
    />
  );

  if (forbidden) {
    return (
      <div className="page">
        {head}
        <div className="notice">
          Bu bölüm için yönetici (Admin) yetkisi gerekiyor. Denetim kaydı, projenin
          tüm veri hareketlerini gösterir — bu yüzden bir yönetim yetkisi.
        </div>
      </div>
    );
  }
  if (error) return <div className="page">{head}<div className="notice notice-error">{error}</div></div>;

  const lastPage = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));

  return (
    <div>
      <div style={{ padding: '22px 24px 0', maxWidth: 1320 }}>{head}</div>
      <div className="split-col" style={{ display: 'flex', minHeight: 0 }}>
      <aside style={{ width: 200, flexShrink: 0, borderRight: '1px solid var(--line)', padding: 14, overflowY: 'auto' }}>
        <div style={{ fontSize: 11, fontWeight: 700, textTransform: 'uppercase', color: 'var(--content-subtle)', marginBottom: 8 }}>
          Tür
        </div>
        {ALL_KINDS.map(k => (
          <label key={k} style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 12.5, marginBottom: 4, textTransform: 'capitalize' }}>
            <input type="checkbox" checked={kinds.has(k)} onChange={() => toggleKind(k)} />
            {k}
          </label>
        ))}

        <div style={{ fontSize: 11, fontWeight: 700, textTransform: 'uppercase', color: 'var(--content-subtle)', margin: '14px 0 8px' }}>
          Tablo
        </div>
        <input type="text" value={tableName} placeholder="ör. customers"
               onChange={e => { setPage(1); setTableName(e.target.value); }} style={{ width: '100%' }} />

        <div style={{ fontSize: 11, fontWeight: 700, textTransform: 'uppercase', color: 'var(--content-subtle)', margin: '14px 0 8px' }}>
          Zaman aralığı
        </div>
        <input type="datetime-local" value={from} onChange={e => { setPage(1); setFrom(e.target.value); }}
               style={{ width: '100%', marginBottom: 6 }} />
        <input type="datetime-local" value={to} onChange={e => { setPage(1); setTo(e.target.value); }} style={{ width: '100%' }} />

        <div style={{ fontSize: 11, fontWeight: 700, textTransform: 'uppercase', color: 'var(--content-subtle)', margin: '14px 0 8px' }}>
          Sonuç
        </div>
        <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 12.5 }}>
          <input type="checkbox" checked={onlyFailed} onChange={e => { setPage(1); setOnlyFailed(e.target.checked); }} />
          Yalnızca başarısız
        </label>
      </aside>

      <div style={{ flex: 1, minWidth: 0, overflow: 'auto', padding: '0 24px 24px' }}>

        {!entries ? (
          <div className="empty">Yükleniyor…</div>
        ) : entries.length === 0 ? (
          <div className="empty">Bu filtrelerle eşleşen yazma işlemi yok.</div>
        ) : (
          <>
            <div className="grid-wrap">
              <table>
                <thead>
                  <tr><th>Zaman</th><th>Tür</th><th>Tablo</th><th>Satır</th><th>Kaynak</th><th>Sonuç</th></tr>
                </thead>
                <tbody>
                  {entries.map(e => (
                    <tr key={e.id} style={{ cursor: 'pointer' }} onClick={() => setSelected(e)}>
                      <td>{new Date(e.createdAt).toLocaleString('tr-TR')}</td>
                      <td style={{ textTransform: 'uppercase' }}>{e.kind}</td>
                      <td>{e.tableName ?? '—'}</td>
                      <td>{e.rowKey ?? '—'}</td>
                      <td>{e.apiKeyPrefix ? `🔑 ${e.apiKeyPrefix}` : e.actorUserId ? '👤 kullanıcı' : '—'}</td>
                      <td>{e.succeeded ? `✓ ${e.affectedRows}` : '✗ 0'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <div className="pager">
              <button className="btn btn-sm" disabled={page <= 1} onClick={() => setPage(p => p - 1)}>Önceki</button>
              <span>{page} / {lastPage} · {totalCount} kayıt</span>
              <button className="btn btn-sm" disabled={page >= lastPage} onClick={() => setPage(p => p + 1)}>Sonraki</button>
            </div>
          </>
        )}
      </div>

      {selected && (
        <div className="overlay" onMouseDown={e => { if (e.target === e.currentTarget) setSelected(null); }}>
          <div className="dialog">
            <h2>{selected.kind.toUpperCase()} · {selected.tableName ?? '—'}</h2>
            <p className="hint">{new Date(selected.createdAt).toLocaleString('tr-TR')}</p>
            <div style={{ fontSize: 12.5, display: 'flex', flexDirection: 'column', gap: 6 }}>
              <div><b>Satır anahtarı:</b> {selected.rowKey ?? '—'}</div>
              <div><b>Kolonlar:</b> {selected.columns ?? '—'}</div>
              <div><b>Etkilenen satır:</b> {selected.affectedRows}</div>
              <div><b>Sonuç:</b> {selected.succeeded ? 'Başarılı' : 'Başarısız'}</div>
              <div><b>Anahtar öneki:</b> {selected.apiKeyPrefix ?? '— (oturum)'}</div>
              <div><b>Kullanıcı:</b> {selected.actorUserId ?? '—'}</div>
            </div>
            <div className="dialog-actions">
              <button className="btn" onClick={() => setSelected(null)}>Kapat</button>
            </div>
          </div>
        </div>
      )}
      </div>
    </div>
  );
}
