'use client';

import { useEffect, useMemo, useState } from 'react';
import { type DeskSession } from '../lib/api';
import { fetchAnalytics, AnalyticsAccessError, type AnalyticsResult } from '../lib/analytics';
import { type DeskTable } from '../lib/schema';
import PageHead from './PageHead';

type Period = '24h' | '7d' | '30d';
const PERIODS: { key: Period; label: string; hours: number; bucket: 'hour' | 'day' }[] = [
  { key: '24h', label: 'Son 24 saat', hours: 24, bucket: 'hour' },
  { key: '7d', label: '7 gün', hours: 24 * 7, bucket: 'day' },
  { key: '30d', label: '30 gün', hours: 24 * 30, bucket: 'day' },
];

// Islem turu basina ayirt edilebilir renkler. Onceki palet bes farkli GRI
// tonuydu — yigilmis bir cubukta hangi dilimin hangi tur oldugu ayirt
// edilemiyordu (ve acik temada tek bir koyu blok gibi gorunuyordu).
// Ton seciminde anlam var: yazma yesil, guncelleme mavi, SILME kirmizi.
const KIND_COLORS: Record<string, string> = {
  create: '#2e9e6b',
  update: '#3b82c4',
  delete: '#c9524b',
  import: '#8b6bc7',
  rpc: '#c98a2e',
  sql: '#5f7d8c',
};
const KIND_LABELS: Record<string, string> = {
  create: 'ekleme', update: 'güncelleme', delete: 'silme',
  import: 'içe aktarma', rpc: 'rpc', sql: 'sql',
};

/**
 * D7 — Analytics (namines_desk/07-ANALYTICS.md). Kütüphane KULLANILMADI —
 * grafik kendi (bağımlılıksız) inline SVG'si; §5'in "hafif bir grafik paketi,
 * Desk'in kendi bağımlılığı" isteğinin en hafif hâli: hiç bağımlılık.
 * Y ekseni her zaman 0'dan başlar (§5 — aksi hâlde küçük dalgalanma felaket
 * gibi gösterilir, bu analitik ekranında yanlış karar aldıran bir görsel yalan).
 */
export default function Analytics({ session, tables }: { session: DeskSession; tables: DeskTable[] | null }) {
  const [period, setPeriod] = useState<Period>('7d');
  const [data, setData] = useState<AnalyticsResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [forbidden, setForbidden] = useState(false);

  useEffect(() => {
    let cancelled = false;
    setError(null);
    setForbidden(false);
    setData(null);
    const p = PERIODS.find(x => x.key === period)!;
    const to = new Date();
    const from = new Date(to.getTime() - p.hours * 3600_000);
    fetchAnalytics(session, session.projectId, from.toISOString(), to.toISOString(), p.bucket)
      .then(res => { if (!cancelled) setData(res); })
      .catch(err => {
        if (cancelled) return;
        if (err instanceof AnalyticsAccessError && err.status === 403) { setForbidden(true); return; }
        setError(err instanceof Error ? err.message : 'Analitik okunamadı.');
      });
    return () => { cancelled = true; };
  }, [session, period]);

  // Canlı şemadan — "Tablo / kolon / ilişki sayısı" (§2), ayrı bir uç gerekmiyor:
  // Desk zaten şemayı okumuş durumda (D3/D4).
  const schemaCounts = useMemo(() => {
    if (!tables) return null;
    const columnCount = tables.reduce((sum, t) => sum + t.columns.length, 0);
    const relationCount = tables.reduce((sum, t) => sum + t.columns.filter(c => c.references).length, 0);
    return { tableCount: tables.length, columnCount, relationCount };
  }, [tables]);

  const head = (
    <PageHead
      title="Analitik"
      desc={<>
        Bu projede yapılan <b>yazma işlemlerinin</b> özeti — hepsi denetim kaydından
        (GatewayAuditEntry) türetiliyor. CPU/istek/transfer gibi barındırma metrikleri
        burada YOK: Namines kimsenin uygulamasını çalıştırmıyor, o sayıların bir
        kaynağı olmazdı.
      </>}
    />
  );

  if (forbidden) {
    return (
      <div className="page">
        {head}
        <div className="notice">
          Bu bölüm için yönetici (Admin) yetkisi gerekiyor. Toplamlar, projenin
          tüm veri hareketini açığa vurur — bu yüzden bir yönetim yetkisi.
        </div>
      </div>
    );
  }
  if (error) return <div className="page">{head}<div className="notice notice-error">{error}</div></div>;

  return (
    <div className="page">
      {head}
      <div style={{ display: 'flex', gap: 4, marginBottom: 16 }}>
        {PERIODS.map(p => (
          <button key={p.key} className="btn btn-sm" aria-current={period === p.key} onClick={() => setPeriod(p.key)}>
            {p.label}
          </button>
        ))}
      </div>

      {!data ? (
        <div className="empty">Yükleniyor…</div>
      ) : data.totalWrites === 0 ? (
        <div className="empty">
          Bu dönemde yazma işlemi yok. Okuma istekleri kaydedilmiyor (§1) — bu, projenin
          kullanılmadığı anlamına gelmez.
        </div>
      ) : (
        <>
          <div className="stats">
            <Card label="Yazma işlemi" value={String(data.totalWrites)} />
            <Card label="Başarı oranı" value={`${(data.successRate * 100).toFixed(0)}%`} />
            <Card label="Etkilenen satır" value={String(data.totalAffectedRows)} />
            <Card label="Şema sürümü" value={String(data.schemaVersionCount)} />
            <Card label="İnsan / Uygulama" value={`${data.sourceBreakdown.human} / ${data.sourceBreakdown.application}`} />
            {schemaCounts && (
              <Card label="Tablo / Kolon / İlişki" value={`${schemaCounts.tableCount} / ${schemaCounts.columnCount} / ${schemaCounts.relationCount}`} />
            )}
          </div>

          <StackedBarChart buckets={data.buckets} bucketUnit={data.bucket} />

          {data.topTables.length > 0 && (
            <div style={{ marginTop: 20 }}>
              <div style={{ fontSize: 11, fontWeight: 700, textTransform: 'uppercase', color: 'var(--content-subtle)', marginBottom: 8 }}>
                En çok yazılan tablolar
              </div>
              {data.topTables.map(t => (
                <div key={t.tableName} style={{ display: 'flex', justifyContent: 'space-between', fontSize: 12.5, padding: '4px 0', borderBottom: '1px solid var(--line)' }}>
                  <span>{t.tableName}</span>
                  <span style={{ fontVariantNumeric: 'tabular-nums' }}>{t.count}</span>
                </div>
              ))}
            </div>
          )}
        </>
      )}
    </div>
  );
}

function Card({ label, value }: { label: string; value: string }) {
  // Panonun geri kalanıyla aynı istatistik kutusu (bkz. globals.css .stat) —
  // aynı bilgi tipinin iki farklı görünmesi, arayüzü rastgele gösteriyordu.
  return (
    <div className="stat">
      <div className="stat-label">{label}</div>
      <div className="stat-value" style={{ fontVariantNumeric: 'tabular-nums' }}>{value}</div>
    </div>
  );
}

/**
 * Bağımlılıksız, yığılmış çubuk grafik. Y ekseni HER ZAMAN 0'dan başlar
 * (§5 — aksi hâlde küçük dalgalanma felaket gibi görünür).
 *
 * <b>Çubuk genişliği SINIRLI:</b> önceki hâlde genişlik kova sayısına
 * bölünüyordu, yani tek bir veri noktası olan bir projede grafik, ekranı
 * kaplayan tek bir dolu dikdörtgene dönüşüyordu — bir grafik değil, bir
 * blok. Artık çubuk en fazla 44px ve seri ortalanıyor.
 */
function StackedBarChart({ buckets, bucketUnit }: { buckets: AnalyticsBucketLike[]; bucketUnit: 'hour' | 'day' }) {
  const kinds = ['create', 'update', 'delete', 'import', 'rpc', 'sql'] as const;
  const totals = buckets.map(b => kinds.reduce((s, k) => s + b[k], 0));
  const rawMax = Math.max(1, ...totals);
  // Y ekseni "yuvarlak" bir tavana çıkarılıyor: 7 yerine 10, 23 yerine 25 —
  // ızgara çizgilerine okunabilir sayılar düşsün.
  const max = niceCeil(rawMax);

  const width = 720, height = 190;
  const padL = 34, padR = 12, padT = 12, padB = 26;
  const plotW = width - padL - padR;
  const plotH = height - padT - padB;

  const slot = buckets.length > 0 ? plotW / buckets.length : plotW;
  const barW = Math.min(44, Math.max(3, slot - 6));
  const ticks = [0, 0.5, 1];

  return (
    <div className="grid-wrap" style={{ padding: 14 }}>
      <svg
        viewBox={`0 0 ${width} ${height}`}
        style={{ width: '100%', height: 'auto', display: 'block' }}
        role="img"
        aria-label={`Yazma işlemleri zaman serisi — en yüksek ${rawMax}`}
      >
        {/* Yatay ızgara + Y ekseni etiketleri */}
        {ticks.map(t => {
          const y = padT + plotH * (1 - t);
          return (
            <g key={t}>
              <line x1={padL} y1={y} x2={width - padR} y2={y} stroke="var(--line)" strokeWidth="1" />
              <text
                x={padL - 7} y={y + 3} textAnchor="end"
                fontSize="9" fill="var(--content-subtle)" fontFamily="ui-sans-serif, system-ui"
              >
                {Math.round(max * t)}
              </text>
            </g>
          );
        })}

        {/* Çubuklar */}
        {buckets.map((b, i) => {
          let yOffset = 0;
          const cx = padL + slot * i + slot / 2;
          const x = cx - barW / 2;
          const total = totals[i];
          return (
            <g key={i}>
              <title>{`${formatBucketLabel(b, bucketUnit)} — ${total} işlem`}</title>
              {kinds.map(k => {
                const v = b[k];
                if (v === 0) return null;
                const barHeight = (plotH * v) / max;
                const y = padT + plotH - yOffset - barHeight;
                yOffset += barHeight;
                return <rect key={k} x={x} y={y} width={barW} height={barHeight} fill={KIND_COLORS[k]} rx="1.5" />;
              })}
            </g>
          );
        })}

        {/* X ekseni: ilk ve son kova — arası kalabalık yapardı */}
        {buckets.length > 0 && (
          <>
            <text x={padL} y={height - 8} fontSize="9" fill="var(--content-subtle)" fontFamily="ui-sans-serif, system-ui">
              {formatBucketLabel(buckets[0], bucketUnit)}
            </text>
            {buckets.length > 1 && (
              <text
                x={width - padR} y={height - 8} textAnchor="end"
                fontSize="9" fill="var(--content-subtle)" fontFamily="ui-sans-serif, system-ui"
              >
                {formatBucketLabel(buckets[buckets.length - 1], bucketUnit)}
              </text>
            )}
          </>
        )}
      </svg>

      <div style={{ display: 'flex', gap: 12, marginTop: 10, fontSize: 10.5, color: 'var(--content-muted)', flexWrap: 'wrap' }}>
        {kinds.map(k => (
          <span key={k} style={{ display: 'flex', alignItems: 'center', gap: 5 }}>
            <span style={{ width: 8, height: 8, background: KIND_COLORS[k], display: 'inline-block', borderRadius: 2 }} />
            {KIND_LABELS[k]}
          </span>
        ))}
        <span style={{ marginLeft: 'auto', color: 'var(--content-subtle)' }}>
          kova: {bucketUnit === 'hour' ? 'saat' : 'gün'}
        </span>
      </div>
    </div>
  );
}

/** 7 → 10, 23 → 25, 140 → 150: ızgaraya okunabilir sayılar düşsün. */
function niceCeil(n: number): number {
  if (n <= 5) return 5;
  const mag = Math.pow(10, Math.floor(Math.log10(n)));
  const step = mag / 2;
  return Math.ceil(n / step) * step;
}

function formatBucketLabel(b: AnalyticsBucketLike, unit: 'hour' | 'day'): string {
  const raw = (b as { bucket?: string; timestamp?: string }).bucket
    ?? (b as { bucket?: string; timestamp?: string }).timestamp;
  if (!raw) return '';
  const d = new Date(raw);
  if (Number.isNaN(d.getTime())) return '';
  return unit === 'hour'
    ? d.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })
    : d.toLocaleDateString('tr-TR', { day: '2-digit', month: '2-digit' });
}

interface AnalyticsBucketLike {
  create: number; update: number; delete: number; import: number; rpc: number; sql: number;
}
