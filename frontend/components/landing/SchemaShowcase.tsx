'use client';

import { useHomeThemeStore } from '../../store/useHomeThemeStore';

/**
 * Hero'nun sağ tarafı — render.com'un "$ git push" + "PRODUCTION" paneli
 * ikilisinin yerini alan, kendi ürünümüzün GERÇEK görünümü: birden fazla
 * bağlı tablo, FK çizgileriyle bağlanmış, arkasında hafif bir ızgara —
 * Namines Desk'in Canvas görünümünün (bkz. services/desk/app/Canvas.tsx)
 * küçük, statik bir yeniden üretimi. Sahte bir ekran görüntüsü DEĞİL.
 *
 * <b>Neden 5 tablo:</b> kullanıcı isteği "daha sistematik ve büyük... bir
 * database şeması olabilir" — iki kartlık önceki hâl "büyük" değildi. Beş
 * tablo + dört ilişki gerçek bir şemanın yoğunluğunu hissettiriyor.
 */
const TABLES = [
  // top:24% — terminal etiketi sol-üst köşeyi kaplıyor (~50px), customers
  // onun ALTINDA başlıyor ki iki öğe üst üste binmesin.
  { id: 'customers', name: 'customers', top: '24%', left: '2%', cols: [{ n: 'id', k: 'pk' }, { n: 'email', k: '' }], highlighted: false },
  { id: 'products', name: 'products', top: '0%', left: '62%', cols: [{ n: 'id', k: 'pk' }, { n: 'sku', k: '' }, { n: 'price_cents', k: '' }], highlighted: false },
  { id: 'orders', name: 'orders', top: '42%', left: '30%', cols: [{ n: 'id', k: 'pk' }, { n: 'customer_id', k: 'fk' }, { n: 'status', k: '' }], highlighted: true },
  { id: 'payments', name: 'payments', top: '70%', left: '0%', cols: [{ n: 'id', k: 'pk' }, { n: 'order_id', k: 'fk' }, { n: 'amount_cents', k: '' }], highlighted: false },
  { id: 'order_items', name: 'order_items', top: '72%', left: '56%', cols: [{ n: 'id', k: 'pk' }, { n: 'order_id', k: 'fk' }, { n: 'product_id', k: 'fk' }], highlighted: false },
] as const;

// Kart merkezlerinin yaklaşık yüzde konumu — yukarıdaki top/left'lerden elle
// türetildi (kart genişliği ~150px, yükseklik kolon sayısına göre değişken).
// Kesin piksel hizası yerine "yaklaşık, okunaklı bağlantı" yeterli — bu bir
// veri görselleştirmesi değil, bir illüstrasyon.
const LINES = [
  { from: [16, 30], to: [40, 46] },   // customers → orders
  { from: [70, 12], to: [55, 70] },   // products → order_items
  { from: [40, 52], to: [16, 76] },   // orders → payments
  { from: [46, 52], to: [64, 78] },   // orders → order_items
];

export default function SchemaShowcase() {
  const theme = useHomeThemeStore(s => s.theme);
  const isLight = theme === 'light';

  return (
    <div
      className="relative w-full h-[420px] sm:h-[480px] lg:h-[560px] rounded-[var(--radius-modal)] overflow-hidden border"
      style={{
        borderColor: isLight ? 'rgba(0,0,0,0.08)' : 'var(--color-line-strong)',
        background: isLight ? '#ffffff' : 'var(--surface-900)',
        backgroundImage: isLight
          ? 'linear-gradient(rgba(0,0,0,0.04) 1px, transparent 1px), linear-gradient(90deg, rgba(0,0,0,0.04) 1px, transparent 1px)'
          : 'linear-gradient(rgba(255,255,255,0.035) 1px, transparent 1px), linear-gradient(90deg, rgba(255,255,255,0.035) 1px, transparent 1px)',
        backgroundSize: '32px 32px',
      }}
    >
      {/* Terminal etiketi — render'ın "$ git push" kutusunun karşılığı */}
      <div
        className="absolute left-4 top-4 z-20 rounded-[var(--radius-control)] border px-3 py-2 font-mono text-[11px] shadow-lg"
        style={{
          borderColor: isLight ? 'rgba(0,0,0,0.1)' : 'var(--color-line-strong)',
          background: isLight ? '#ffffff' : 'var(--surface-800)',
          color: isLight ? '#111827' : 'var(--content-secondary)',
        }}
      >
        <span style={{ color: 'var(--accent-text)' }}>$</span> describe-schema
      </div>

      {/* FK çizgileri */}
      <svg className="absolute inset-0 w-full h-full" viewBox="0 0 100 100" preserveAspectRatio="none" aria-hidden="true">
        {LINES.map((l, i) => (
          <line
            key={i}
            x1={l.from[0]} y1={l.from[1]} x2={l.to[0]} y2={l.to[1]}
            stroke={isLight ? 'rgba(0,0,0,0.28)' : 'rgba(255,255,255,0.28)'}
            strokeWidth="0.6"
            strokeDasharray="1.6 1.6"
            strokeLinecap="round"
          />
        ))}
      </svg>

      {/* Tablo kartları */}
      {TABLES.map(t => (
        <div
          key={t.id}
          className="absolute w-[150px] rounded-[var(--radius-card)] overflow-hidden shadow-lg"
          style={{
            top: t.top, left: t.left,
            border: `1px solid ${t.highlighted ? 'var(--accent-hover)' : isLight ? 'rgba(0,0,0,0.1)' : 'var(--color-line-strong)'}`,
            boxShadow: t.highlighted ? '0 0 0 2px var(--accent-subtle)' : undefined,
            background: isLight ? '#ffffff' : 'var(--surface-800)',
          }}
        >
          <div
            className="px-2.5 py-1.5 text-[11px] font-mono font-bold border-b"
            style={{
              background: isLight ? '#f3f4f6' : 'var(--surface-700)',
              borderColor: isLight ? 'rgba(0,0,0,0.08)' : 'var(--color-line)',
              color: isLight ? '#111827' : 'var(--content-primary)',
            }}
          >
            {t.name}
          </div>
          <div>
            {t.cols.map(c => (
              <div
                key={c.n}
                className="px-2.5 py-1 flex items-center gap-1.5 text-[10.5px] border-b last:border-b-0"
                style={{
                  color: isLight ? '#374151' : 'var(--content-secondary)',
                  borderColor: isLight ? 'rgba(0,0,0,0.04)' : 'rgba(255,255,255,0.03)',
                }}
              >
                <span className="w-3 text-center text-[9px]">
                  {c.k === 'pk' ? '🔑' : c.k === 'fk' ? '🔗' : ''}
                </span>
                <span className="truncate">{c.n}</span>
              </div>
            ))}
          </div>
        </div>
      ))}
    </div>
  );
}
