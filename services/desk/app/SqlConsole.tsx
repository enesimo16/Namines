'use client';

import { useCallback, useEffect, useState } from 'react';
import { type DeskSession } from '../lib/api';
import {
  runDeskSql, setDeskSqlEnabled, DeskSqlError, type DeskSqlResult,
  getSqlHistory, clearSqlHistory, getSavedQueries, saveQuery, deleteSavedQuery,
  type SqlHistoryItem, type SavedQueryItem,
} from '../lib/deskSql';
import PageHead from './PageHead';

/**
 * Namines Desk v2 §E4.2 — salt-okunur SQL konsolu.
 *
 * Yalnızca proje Owner'ına gösterilir (isOwner) ve yalnızca proje sahibi
 * bilerek açtıysa (allowDeskSql) çalışır — sunucu bunu ZATEN uyguluyor
 * (GatewayController.DeskSql), burası yalnızca aynı gerçeği arayüzde
 * yansıtıyor; sunucu tarafı yetki kontrolü olmadan bir arayüz gizlemesi
 * güvenlik sağlamaz, yalnızca kullanıcı deneyimini düzeltir.
 */
export default function SqlConsole({ session, isOwner, allowDeskSql, onToggled }: {
  session: DeskSession; isOwner: boolean; allowDeskSql: boolean; onToggled: () => void;
}) {
  const [sql, setSql] = useState('SELECT * FROM ');
  const [result, setResult] = useState<DeskSqlResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [running, setRunning] = useState(false);
  const [toggling, setToggling] = useState(false);

  // Gecmis (F-01) ve kaydedilmis sorgular (F-02).
  //
  // Ikisi de KULLANICIYA OZEL: sunucu baskasinin kaydini dondurmuyor. Bu
  // arayuz o gercegi yansitiyor, saglamiyor -- gizlenmis bir liste guvenlik
  // degildir.
  const [history, setHistory] = useState<SqlHistoryItem[] | null>(null);
  const [saved, setSaved] = useState<SavedQueryItem[] | null>(null);
  const [sideTab, setSideTab] = useState<'history' | 'saved'>('history');
  const [sideBusy, setSideBusy] = useState(false);

  const refreshSide = useCallback(async () => {
    if (!isOwner || !allowDeskSql) return;
    try {
      const [h, q] = await Promise.all([getSqlHistory(session, 50), getSavedQueries(session)]);
      setHistory(h);
      setSaved(q);
    } catch {
      // Yan defterin yuklenememesi konsolu KULLANILAMAZ yapmamali: sorgu
      // calistirmak buna bagli degil. Bos liste, bir hata bandindan iyi.
      setHistory([]);
      setSaved([]);
    }
  }, [session, isOwner, allowDeskSql]);

  useEffect(() => { void refreshSide(); }, [refreshSide]);

  if (!isOwner) {
    return (
      <div className="page">
        <PageHead title="SQL console" />
        <div className="notice">
          The SQL console can only be used by the project Owner.
        </div>
      </div>
    );
  }

  if (!allowDeskSql) {
    return (
      <div className="page">
        <PageHead
          title="SQL console"
          desc="Not yet enabled for this project. Turning it on grants real, if limited, database access — so it should be a deliberate decision, which is why it's off by default."
        />
        <div className="notice" style={{ marginBottom: 12 }}>
          Once enabled, only a <b>single SELECT/WITH/EXPLAIN/SHOW</b> statement can run —
          writes, DDL, and <code>SELECT…INTO</code> are blocked server-side. When the engine
          supports it, the connection also opens read-only at the database level, and every
          run is logged.
        </div>
        <button
          className="btn btn-primary"
          disabled={toggling}
          onClick={async () => {
            setToggling(true);
            try {
              await setDeskSqlEnabled(session, true);
              onToggled();
            } catch (err) {
              setError(err instanceof Error ? err.message : 'Could not enable.');
            } finally {
              setToggling(false);
            }
          }}
        >
          {toggling ? 'Enabling…' : 'Enable SQL console'}
        </button>
        {error && <div className="notice notice-error" style={{ marginTop: 10 }}>{error}</div>}
      </div>
    );
  }

  async function run() {
    setRunning(true);
    setError(null);
    try {
      const res = await runDeskSql(session, sql, 500);
      setResult(res);
    } catch (err) {
      setResult(null);
      setError(err instanceof DeskSqlError ? err.message : 'Could not run the query.');
    } finally {
      setRunning(false);
      // Basarisiz calistirma da gecmise yaziliyor (sunucu tarafi), o yuzden
      // yenileme `finally` icinde -- hata durumunda atlanmasi, kullanicinin
      // aradigi kaydin gorunmemesi demek olurdu.
      void refreshSide();
    }
  }

  async function handleSave() {
    const name = prompt('Name this query:')?.trim();
    if (!name) return;
    setSideBusy(true);
    setError(null);
    try {
      await saveQuery(session, name, sql);
      setSideTab('saved');
      await refreshSide();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not save the query.');
    } finally {
      setSideBusy(false);
    }
  }

  const columns = result && result.rows.length > 0 ? Object.keys(result.rows[0].values) : [];

  return (
    <div className="page" style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
      <PageHead
        title="SQL console"
        desc={<>
          A read-only query window. Only a <b>single SELECT/WITH/EXPLAIN/SHOW</b> statement
          {' '}can run — writes, DDL, and <code>SELECT…INTO</code> are blocked server-side,
          and the connection also opens read-only at the database level when the engine
          supports it.
        </>}
      />
      <textarea
        aria-label="SQL query"
        value={sql}
        onChange={e => setSql(e.target.value)}
        rows={5}
        style={{
          width: '100%', fontFamily: 'ui-monospace, monospace', fontSize: 12.5,
          background: 'var(--surface-900)', color: 'var(--content-primary)',
          border: '1px solid var(--line-strong)', borderRadius: 'var(--radius-control)', padding: 10,
        }}
      />
      <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
        <button className="btn btn-primary" disabled={running || !sql.trim()} onClick={run}>
          {running ? 'Running…' : 'Run'}
        </button>
        <button className="btn btn-sm" disabled={sideBusy || !sql.trim()} onClick={handleSave}>
          Save query
        </button>
        <button
          className="btn btn-sm"
          disabled={toggling}
          onClick={async () => {
            if (!confirm('Disable the SQL console for this project?')) return;
            setToggling(true);
            setError(null);
            try {
              await setDeskSqlEnabled(session, false);
              onToggled();
            } catch (err) {
              // Without this catch, disabling would silently fail: the user
              // would think the console is off while the server keeps it ON.
              // Misreporting a sensitive surface's state is worse than not
              // showing an error at all.
              setError(err instanceof Error ? err.message : 'Could not disable.');
            } finally {
              setToggling(false);
            }
          }}
        >
          Disable console
        </button>
      </div>

      {error && <div className="notice notice-error">{error}</div>}

      {/* Gecmis / kaydedilmis sorgular. Bir kayda tiklamak metni editore
          YAZAR ama CALISTIRMAZ: gecmisten gelen bir sorgunun kullanicinin
          bakmadan calistirmasini istemedigi bir sey olma ihtimali var. */}
      <div style={{ borderTop: '1px solid var(--line-strong)', paddingTop: 10 }}>
        <div style={{ display: 'flex', gap: 6, alignItems: 'center', marginBottom: 8 }}>
          <button
            className={`btn btn-sm${sideTab === 'history' ? ' btn-primary' : ''}`}
            onClick={() => setSideTab('history')}
          >
            History{history ? ` (${history.length})` : ''}
          </button>
          <button
            className={`btn btn-sm${sideTab === 'saved' ? ' btn-primary' : ''}`}
            onClick={() => setSideTab('saved')}
          >
            Saved{saved ? ` (${saved.length})` : ''}
          </button>
          {sideTab === 'history' && history && history.length > 0 && (
            <button
              className="btn btn-sm"
              style={{ marginLeft: 'auto' }}
              disabled={sideBusy}
              onClick={async () => {
                if (!confirm('Clear your query history? The audit log is not affected.')) return;
                setSideBusy(true);
                try { await clearSqlHistory(session); await refreshSide(); }
                finally { setSideBusy(false); }
              }}
            >
              Clear history
            </button>
          )}
        </div>

        {sideTab === 'history' ? (
          history === null ? <div className="empty">Loading…</div>
          : history.length === 0 ? <div className="empty">You haven&apos;t run a query yet.</div>
          : (
            <ul style={{ listStyle: 'none', margin: 0, padding: 0, maxHeight: 200, overflow: 'auto' }}>
              {history.map(h => (
                <li key={h.id} style={{ borderBottom: '1px solid var(--line)', padding: '6px 0' }}>
                  <button
                    onClick={() => setSql(h.sql)}
                    title="Write to editor"
                    style={{
                      all: 'unset', cursor: 'pointer', display: 'block', width: '100%',
                      fontFamily: 'ui-monospace, monospace', fontSize: 12,
                      color: 'var(--content-primary)', whiteSpace: 'pre-wrap', wordBreak: 'break-word',
                    }}
                  >
                    {h.sql}{h.truncated && ' …'}
                  </button>
                  <div style={{ fontSize: 11, color: 'var(--content-subtle)', marginTop: 2 }}>
                    {h.succeeded
                      ? `${h.rowCount} rows · ${h.durationMs} ms`
                      : `Error: ${h.errorMessage ?? 'unknown'}`}
                    {' · '}{new Date(h.createdAt).toLocaleString('en-US')}
                  </div>
                </li>
              ))}
            </ul>
          )
        ) : (
          saved === null ? <div className="empty">Loading…</div>
          : saved.length === 0 ? <div className="empty">No saved queries.</div>
          : (
            <ul style={{ listStyle: 'none', margin: 0, padding: 0, maxHeight: 200, overflow: 'auto' }}>
              {saved.map(q => (
                <li key={q.id} style={{
                  borderBottom: '1px solid var(--line)', padding: '6px 0',
                  display: 'flex', gap: 8, alignItems: 'flex-start',
                }}>
                  <button
                    onClick={() => setSql(q.sql)}
                    title="Write to editor"
                    style={{
                      all: 'unset', cursor: 'pointer', flex: 1, minWidth: 0,
                      color: 'var(--content-primary)',
                    }}
                  >
                    <b style={{ fontSize: 12.5 }}>{q.name}</b>
                    <div style={{
                      fontFamily: 'ui-monospace, monospace', fontSize: 11,
                      color: 'var(--content-subtle)', whiteSpace: 'nowrap',
                      overflow: 'hidden', textOverflow: 'ellipsis',
                    }}>
                      {q.sql}
                    </div>
                  </button>
                  <button
                    className="btn btn-sm btn-danger"
                    disabled={sideBusy}
                    onClick={async () => {
                      if (!confirm(`Delete "${q.name}"?`)) return;
                      setSideBusy(true);
                      try { await deleteSavedQuery(session, q.id); await refreshSide(); }
                      finally { setSideBusy(false); }
                    }}
                  >
                    Delete
                  </button>
                </li>
              ))}
            </ul>
          )
        )}
      </div>

      {result && (
        <div style={{ flex: 1, minHeight: 0, overflow: 'auto' }}>
          {result.truncated && (
            <div className="notice" style={{ marginBottom: 8 }}>
              Result truncated at 500 rows — there are more.
            </div>
          )}
          {result.rows.length === 0 ? (
            <div className="empty">No results.</div>
          ) : (
            <div className="grid-wrap">
              <table>
                <thead>
                  <tr>{columns.map(c => <th key={c}>{c}</th>)}</tr>
                </thead>
                <tbody>
                  {result.rows.map((r, i) => (
                    <tr key={i}>
                      {columns.map(c => <td key={c}>{formatSqlCell(r.values[c])}</td>)}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}
    </div>
  );
}

function formatSqlCell(value: unknown): string {
  if (value === null || value === undefined) return '—';
  if (typeof value === 'boolean') return value ? '✓' : '✗';
  return String(value);
}
