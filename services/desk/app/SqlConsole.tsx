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
        <PageHead title="SQL konsolu" />
        <div className="notice">
          SQL konsolu yalnızca proje sahibi (Owner) tarafından kullanılabilir.
        </div>
      </div>
    );
  }

  if (!allowDeskSql) {
    return (
      <div className="page">
        <PageHead
          title="SQL konsolu"
          desc="Bu proje için henüz açılmadı. Açmak, kapsamı sınırlı ama gerçek bir veritabanı erişimi olduğu için bilinçli bir karar olmalı — o yüzden varsayılan kapalı."
        />
        <div className="notice" style={{ marginBottom: 12 }}>
          Açtığınızda yalnızca <b>tek bir SELECT/WITH/EXPLAIN/SHOW</b> ifadesi
          çalıştırılabilir — yazma, DDL ve <code>SELECT…INTO</code> sunucu tarafında
          engellenir. Motor destekliyorsa bağlantı ayrıca veritabanı düzeyinde
          salt-okunur açılır ve her çalıştırma denetim kaydına yazılır.
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
              setError(err instanceof Error ? err.message : 'Açılamadı.');
            } finally {
              setToggling(false);
            }
          }}
        >
          {toggling ? 'Açılıyor…' : 'SQL konsolunu aç'}
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
      setError(err instanceof DeskSqlError ? err.message : 'Çalıştırılamadı.');
    } finally {
      setRunning(false);
      // Basarisiz calistirma da gecmise yaziliyor (sunucu tarafi), o yuzden
      // yenileme `finally` icinde -- hata durumunda atlanmasi, kullanicinin
      // aradigi kaydin gorunmemesi demek olurdu.
      void refreshSide();
    }
  }

  async function handleSave() {
    const name = prompt('Sorguya bir ad verin:')?.trim();
    if (!name) return;
    setSideBusy(true);
    setError(null);
    try {
      await saveQuery(session, name, sql);
      setSideTab('saved');
      await refreshSide();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Kaydedilemedi.');
    } finally {
      setSideBusy(false);
    }
  }

  const columns = result && result.rows.length > 0 ? Object.keys(result.rows[0].values) : [];

  return (
    <div className="page" style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
      <PageHead
        title="SQL konsolu"
        desc={<>
          Salt-okunur sorgu penceresi. Yalnızca <b>tek bir SELECT/WITH/EXPLAIN/SHOW</b>
          {' '}çalıştırılabilir; yazma, DDL ve <code>SELECT…INTO</code> sunucu tarafında
          engellenir — motor destekliyorsa bağlantı ayrıca veritabanı düzeyinde
          salt-okunur açılır.
        </>}
      />
      <textarea
        aria-label="SQL sorgusu"
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
          {running ? 'Çalıştırılıyor…' : 'Çalıştır'}
        </button>
        <button className="btn btn-sm" disabled={sideBusy || !sql.trim()} onClick={handleSave}>
          Sorguyu kaydet
        </button>
        <button
          className="btn btn-sm"
          disabled={toggling}
          onClick={async () => {
            if (!confirm('SQL konsolunu bu proje için kapatmak istiyor musunuz?')) return;
            setToggling(true);
            setError(null);
            try {
              await setDeskSqlEnabled(session, false);
              onToggled();
            } catch (err) {
              // Bu catch OLMADAN kapatma sessizce başarısız oluyordu: kullanıcı
              // konsolun kapandığını sanıyor, oysa sunucuda AÇIK kalıyor.
              // Hassas bir yüzeyin durumu hakkında yanlış bilgi vermek,
              // hatayı göstermemekten çok daha kötü.
              setError(err instanceof Error ? err.message : 'Kapatılamadı.');
            } finally {
              setToggling(false);
            }
          }}
        >
          Konsolu kapat
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
            Geçmiş{history ? ` (${history.length})` : ''}
          </button>
          <button
            className={`btn btn-sm${sideTab === 'saved' ? ' btn-primary' : ''}`}
            onClick={() => setSideTab('saved')}
          >
            Kayıtlı{saved ? ` (${saved.length})` : ''}
          </button>
          {sideTab === 'history' && history && history.length > 0 && (
            <button
              className="btn btn-sm"
              style={{ marginLeft: 'auto' }}
              disabled={sideBusy}
              onClick={async () => {
                if (!confirm('Sorgu geçmişinizi silmek istiyor musunuz? Denetim kaydı silinmez.')) return;
                setSideBusy(true);
                try { await clearSqlHistory(session); await refreshSide(); }
                finally { setSideBusy(false); }
              }}
            >
              Geçmişi temizle
            </button>
          )}
        </div>

        {sideTab === 'history' ? (
          history === null ? <div className="empty">Yükleniyor…</div>
          : history.length === 0 ? <div className="empty">Henüz sorgu çalıştırmadınız.</div>
          : (
            <ul style={{ listStyle: 'none', margin: 0, padding: 0, maxHeight: 200, overflow: 'auto' }}>
              {history.map(h => (
                <li key={h.id} style={{ borderBottom: '1px solid var(--line)', padding: '6px 0' }}>
                  <button
                    onClick={() => setSql(h.sql)}
                    title="Editöre yaz"
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
                      ? `${h.rowCount} satır · ${h.durationMs} ms`
                      : `Hata: ${h.errorMessage ?? 'bilinmiyor'}`}
                    {' · '}{new Date(h.createdAt).toLocaleString('tr-TR')}
                  </div>
                </li>
              ))}
            </ul>
          )
        ) : (
          saved === null ? <div className="empty">Yükleniyor…</div>
          : saved.length === 0 ? <div className="empty">Kaydedilmiş sorgu yok.</div>
          : (
            <ul style={{ listStyle: 'none', margin: 0, padding: 0, maxHeight: 200, overflow: 'auto' }}>
              {saved.map(q => (
                <li key={q.id} style={{
                  borderBottom: '1px solid var(--line)', padding: '6px 0',
                  display: 'flex', gap: 8, alignItems: 'flex-start',
                }}>
                  <button
                    onClick={() => setSql(q.sql)}
                    title="Editöre yaz"
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
                      if (!confirm(`"${q.name}" silinsin mi?`)) return;
                      setSideBusy(true);
                      try { await deleteSavedQuery(session, q.id); await refreshSide(); }
                      finally { setSideBusy(false); }
                    }}
                  >
                    Sil
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
              Sonuç 500 satırda kesildi — daha fazla kayıt var.
            </div>
          )}
          {result.rows.length === 0 ? (
            <div className="empty">Sonuç yok.</div>
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
