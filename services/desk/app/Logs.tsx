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
        setError(err instanceof Error ? err.message : 'Could not load logs.');
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
      title="Logs"
      desc={<>
        Audit log of <b>write operations</b> on this project — who changed what, where, and when.
        Ordinary reads are NOT logged; the one exception is <b>reads from tables with a masked
        column</b>, which are recorded. Filter for those with the <i>read</i> kind.
      </>}
    />
  );

  if (forbidden) {
    return (
      <div className="page">
        {head}
        <div className="notice">
          This section requires Admin access — the audit log shows all data activity on the
          project, so it's gated behind an admin-level role.
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
          Kind
        </div>
        {ALL_KINDS.map(k => (
          <label key={k} style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 12.5, marginBottom: 4, textTransform: 'capitalize' }}>
            <input type="checkbox" checked={kinds.has(k)} onChange={() => toggleKind(k)} />
            {k}
          </label>
        ))}

        <div style={{ fontSize: 11, fontWeight: 700, textTransform: 'uppercase', color: 'var(--content-subtle)', margin: '14px 0 8px' }}>
          Table
        </div>
        <input aria-label="Table" type="text" value={tableName} placeholder="e.g. customers"
               onChange={e => { setPage(1); setTableName(e.target.value); }} style={{ width: '100%' }} />

        <div style={{ fontSize: 11, fontWeight: 700, textTransform: 'uppercase', color: 'var(--content-subtle)', margin: '14px 0 8px' }}>
          Time range
        </div>
        <input type="datetime-local" aria-label="Time range start" value={from} onChange={e => { setPage(1); setFrom(e.target.value); }}
               style={{ width: '100%', marginBottom: 6 }} />
        <input type="datetime-local" aria-label="Time range end" value={to} onChange={e => { setPage(1); setTo(e.target.value); }} style={{ width: '100%' }} />

        <div style={{ fontSize: 11, fontWeight: 700, textTransform: 'uppercase', color: 'var(--content-subtle)', margin: '14px 0 8px' }}>
          Result
        </div>
        <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 12.5 }}>
          <input type="checkbox" checked={onlyFailed} onChange={e => { setPage(1); setOnlyFailed(e.target.checked); }} />
          Failed only
        </label>
      </aside>

      <div style={{ flex: 1, minWidth: 0, overflow: 'auto', padding: '0 24px 24px' }}>

        {!entries ? (
          <div className="empty">Loading…</div>
        ) : entries.length === 0 ? (
          <div className="empty">No writes match these filters.</div>
        ) : (
          <>
            <div className="grid-wrap">
              <table>
                <thead>
                  <tr><th>Time</th><th>Kind</th><th>Table</th><th>Row</th><th>Source</th><th>Result</th></tr>
                </thead>
                <tbody>
                  {entries.map(e => (
                    <tr key={e.id} style={{ cursor: 'pointer' }} onClick={() => setSelected(e)}>
                      <td className="nowrap">{new Date(e.createdAt).toLocaleString('en-US')}</td>
                      <td className="nowrap" style={{ textTransform: 'uppercase' }}>{e.kind}</td>
                      <td className="nowrap">{e.tableName ?? '—'}</td>
                      <td className="nowrap">{e.rowKey ?? '—'}</td>
                      <td className="nowrap">{e.apiKeyPrefix ? `🔑 ${e.apiKeyPrefix}` : e.actorUserId ? '👤 user' : '—'}</td>
                      <td className="nowrap">{e.succeeded ? `✓ ${e.affectedRows}` : '✗ 0'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <div className="pager">
              <button className="btn btn-sm" disabled={page <= 1} onClick={() => setPage(p => p - 1)}>Previous</button>
              <span>{page} / {lastPage} · {totalCount} entries</span>
              <button className="btn btn-sm" disabled={page >= lastPage} onClick={() => setPage(p => p + 1)}>Next</button>
            </div>
          </>
        )}
      </div>

      {selected && (
        <div className="overlay" onMouseDown={e => { if (e.target === e.currentTarget) setSelected(null); }}>
          <div className="dialog">
            <h2>{selected.kind.toUpperCase()} · {selected.tableName ?? '—'}</h2>
            <p className="hint">{new Date(selected.createdAt).toLocaleString('en-US')}</p>
            <div style={{ fontSize: 12.5, display: 'flex', flexDirection: 'column', gap: 6 }}>
              <div><b>Row key:</b> {selected.rowKey ?? '—'}</div>
              <div><b>Columns:</b> {selected.columns ?? '—'}</div>
              <div><b>Affected rows:</b> {selected.affectedRows}</div>
              <div><b>Result:</b> {selected.succeeded ? 'Succeeded' : 'Failed'}</div>
              <div><b>Key prefix:</b> {selected.apiKeyPrefix ?? '— (session)'}</div>
              <div><b>User:</b> {selected.actorUserId ?? '—'}</div>
            </div>
            <div className="dialog-actions">
              <button className="btn" onClick={() => setSelected(null)}>Close</button>
            </div>
          </div>
        </div>
      )}
      </div>
    </div>
  );
}
