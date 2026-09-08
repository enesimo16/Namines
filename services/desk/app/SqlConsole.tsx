'use client';

import { useState } from 'react';
import { type DeskSession } from '../lib/api';
import { runDeskSql, setDeskSqlEnabled, DeskSqlError, type DeskSqlResult } from '../lib/deskSql';
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
