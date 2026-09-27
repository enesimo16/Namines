'use client';

import { useCallback, useEffect, useState } from 'react';
import { type DeskSession } from '../lib/api';
import {
  runDeskSql, setDeskSqlEnabled, DeskSqlError, type DeskSqlResult,
  getSqlHistory, clearSqlHistory, getSavedQueries, saveQuery, deleteSavedQuery,
  type SqlHistoryItem, type SavedQueryItem,
} from '../lib/deskSql';
import PageHead from './PageHead';
import { useI18n } from '../lib/i18n';
import { rich } from '../lib/rich';

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
  const { t, dt } = useI18n();
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
        <PageHead title={t('nav.views.sql')} />
        <div className="notice">{t('sql.ownerOnly')}</div>
      </div>
    );
  }

  if (!allowDeskSql) {
    return (
      <div className="page">
        <PageHead
          title={t('nav.views.sql')}
          desc={t('sql.disabledDesc')}
        />
        <div className="notice" style={{ marginBottom: 12 }}>{rich(t('sql.disabledNotice'))}</div>
        <button
          className="btn btn-primary"
          disabled={toggling}
          onClick={async () => {
            setToggling(true);
            try {
              await setDeskSqlEnabled(session, true);
              onToggled();
            } catch (err) {
              setError(err instanceof Error ? err.message : t('sql.enableFailed'));
            } finally {
              setToggling(false);
            }
          }}
        >
          {toggling ? t('sql.enabling') : t('sql.enable')}
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
      setError(err instanceof DeskSqlError ? err.message : t('sql.runFailed'));
    } finally {
      setRunning(false);
      // Basarisiz calistirma da gecmise yaziliyor (sunucu tarafi), o yuzden
      // yenileme `finally` icinde -- hata durumunda atlanmasi, kullanicinin
      // aradigi kaydin gorunmemesi demek olurdu.
      void refreshSide();
    }
  }

  async function handleSave() {
    const name = prompt(t('sql.namePrompt'))?.trim();
    if (!name) return;
    setSideBusy(true);
    setError(null);
    try {
      await saveQuery(session, name, sql);
      setSideTab('saved');
      await refreshSide();
    } catch (err) {
      setError(err instanceof Error ? err.message : t('sql.saveFailed'));
    } finally {
      setSideBusy(false);
    }
  }

  const columns = result && result.rows.length > 0 ? Object.keys(result.rows[0].values) : [];

  return (
    <div className="page" style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
      <PageHead
        title={t('nav.views.sql')}
        desc={rich(t('sql.desc'))}
      />
      <textarea
        aria-label={t('sql.queryLabel')}
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
          {running ? t('common.running') : t('sql.run')}
        </button>
        <button className="btn btn-sm" disabled={sideBusy || !sql.trim()} onClick={handleSave}>
          {t('sql.saveQuery')}
        </button>
        <button
          className="btn btn-sm"
          disabled={toggling}
          onClick={async () => {
            if (!confirm(t('sql.confirmDisable'))) return;
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
              setError(err instanceof Error ? err.message : t('sql.disableFailed'));
            } finally {
              setToggling(false);
            }
          }}
        >
          {t('sql.disable')}
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
            {history ? t('sql.historyCount', { count: history.length }) : t('sql.history')}
          </button>
          <button
            className={`btn btn-sm${sideTab === 'saved' ? ' btn-primary' : ''}`}
            onClick={() => setSideTab('saved')}
          >
            {saved ? t('sql.savedCount', { count: saved.length }) : t('sql.saved')}
          </button>
          {sideTab === 'history' && history && history.length > 0 && (
            <button
              className="btn btn-sm"
              style={{ marginLeft: 'auto' }}
              disabled={sideBusy}
              onClick={async () => {
                if (!confirm(t('sql.confirmClear'))) return;
                setSideBusy(true);
                try { await clearSqlHistory(session); await refreshSide(); }
                finally { setSideBusy(false); }
              }}
            >
              {t('sql.clearHistory')}
            </button>
          )}
        </div>

        {sideTab === 'history' ? (
          history === null ? <div className="empty">{t('common.loading')}</div>
          : history.length === 0 ? <div className="empty">{t('sql.noHistory')}</div>
          : (
            <ul style={{ listStyle: 'none', margin: 0, padding: 0, maxHeight: 200, overflow: 'auto' }}>
              {history.map(h => (
                <li key={h.id} style={{ borderBottom: '1px solid var(--line)', padding: '6px 0' }}>
                  <button
                    onClick={() => setSql(h.sql)}
                    title={t('sql.writeToEditor')}
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
                      ? t('sql.historyOk', { rows: h.rowCount, ms: h.durationMs })
                      : t('sql.historyError', { message: h.errorMessage ?? t('sql.unknown') })}
                    {' · '}{dt(h.createdAt)}
                  </div>
                </li>
              ))}
            </ul>
          )
        ) : (
          saved === null ? <div className="empty">{t('common.loading')}</div>
          : saved.length === 0 ? <div className="empty">{t('sql.noSaved')}</div>
          : (
            <ul style={{ listStyle: 'none', margin: 0, padding: 0, maxHeight: 200, overflow: 'auto' }}>
              {saved.map(q => (
                <li key={q.id} style={{
                  borderBottom: '1px solid var(--line)', padding: '6px 0',
                  display: 'flex', gap: 8, alignItems: 'flex-start',
                }}>
                  <button
                    onClick={() => setSql(q.sql)}
                    title={t('sql.writeToEditor')}
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
                      if (!confirm(t('sql.confirmDeleteSaved', { name: q.name }))) return;
                      setSideBusy(true);
                      try { await deleteSavedQuery(session, q.id); await refreshSide(); }
                      finally { setSideBusy(false); }
                    }}
                  >
                    {t('common.delete')}
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
              {t('sql.truncated')}
            </div>
          )}
          {result.rows.length === 0 ? (
            <div className="empty">{t('sql.noResults')}</div>
          ) : (
            <div className="grid-wrap">
              <table>
                <thead>
                  <tr>{columns.map(c => <th key={c} lang="en">{c}</th>)}</tr>
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
