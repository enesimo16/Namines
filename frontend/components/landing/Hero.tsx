'use client';

import Link from 'next/link';
import { Wand2, Mail, Play } from 'lucide-react';
import RotatingHeadline from './RotatingHeadline';
import SchemaShowcase from './SchemaShowcase';
import { useHomeThemeStore } from '../../store/useHomeThemeStore';

const ENGINES = ['PostgreSQL', 'MySQL', 'MariaDB', 'SQL Server', 'Oracle', 'SQLite'];

/**
 * Hero bölümü — kullanıcının render.com ekran görüntüsüne göre istediği
 * düzeltmeler:
 * - Üstteki "AI-assisted database architecture" rozeti kaldırıldı (gürültü).
 * - Başlık büyütüldü; dönen kelime KENDİ SATIRINDA — üstteki sabit metinle
 *   asla çakışmıyor (önceki hâlde uzun kelimeler satırı taşırıp üstteki
 *   satıra bindirebiliyordu).
 * - Ortadaki ışık lekesi soldan hafif geliyor, merkeze oturmuyor ve daha
 *   düşük opaklıkta (önceki 0.16 → 0.08, render.com'un çok daha sakin
 *   ambiyansına yakın).
 * - Sağ taraf komple değişti: `SchemaShowcase` — büyük, çok tablolu, FK
 *   çizgili bir şema illüstrasyonu (bkz. o dosyanın kendi notu).
 *
 * <b>Açık/koyu önizleme:</b> `useHomeThemeStore` — yalnızca bu bölümün ve
 * `Header`'ın `isHome` dalının tepki verdiği, sayfa geneli olmayan bir
 * anahtar (bkz. store'un kendi notu).
 */
export default function Hero() {
  const theme = useHomeThemeStore(s => s.theme);
  const isLight = theme === 'light';

  return (
    // Açık önizlemede bu sarmalayıcı kendi beyaz zeminini taşıyor — Header
    // zaten aynı anahtarla beyaza dönüyor, ama sayfanın geri kalanı (body)
    // koyu kalıyor; bu div olmadan hero'nun metni koyu zemin üstünde siyah
    // kalır ve okunmaz olurdu (bkz. store'un "yalnızca nav + hero" kapsam notu).
    <div className={`relative w-full ${isLight ? 'bg-white' : ''}`}>
      {/* Ambient glow — soldan hafif, merkezde değil; render.com'un çok daha
          sakin, tek köşeden gelen ışığına yakın (önceki: ortalanmış + 0.16 opaklık). */}
      {!isLight && (
        <div
          aria-hidden="true"
          className="pointer-events-none absolute top-0 left-0 w-[720px] h-[520px] rounded-full opacity-[0.08] blur-[130px]"
          style={{ background: 'var(--accent)' }}
        />
      )}

      <section className="relative z-10 w-full max-w-[var(--w-app)] px-4 sm:px-6 lg:px-8 pt-16 sm:pt-24 pb-16 sm:pb-24">
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-12 lg:gap-8 items-center">
          {/* Sol — metin */}
          <div className="flex flex-col items-start text-left">
            <h1
              className={`font-mono text-5xl sm:text-6xl lg:text-[4.25rem] max-w-[13ch] mb-2 leading-[1.05] font-medium ${
                isLight ? 'text-black' : 'text-content-primary'
              }`}
            >
              Your fastest path to a schema for
            </h1>
            {/* Dönen kelime KENDİ SATIRINDA — sabit yükseklikli satır, üstteki
                başlıkla asla karışmaz (kullanıcı talebi: "alt satırda olsun
                ve üst yazılara karışmasın"). */}
            <div className="mb-6 min-h-[1.15em] sm:min-h-[1.15em]">
              <span className="font-mono text-5xl sm:text-6xl lg:text-[4.25rem] font-medium leading-[1.05]">
                <RotatingHeadline light={isLight} />
              </span>
            </div>

            <p className={`text-body sm:text-body-lg max-w-[46ch] mb-8 leading-relaxed ${
              isLight ? 'text-black/70' : 'text-content-secondary'
            }`}>
              Describe what you&apos;re building in plain English. Get back a schema a
              deterministic rule engine has already checked — real DDL, six SQL
              dialects, no black box in between.
            </p>

            <div className="flex flex-col sm:flex-row items-center gap-3">
              <Link
                href="/new"
                className={`w-full sm:w-auto inline-flex items-center justify-center gap-2 font-semibold py-3 px-6 rounded-[var(--radius-card)] transition-all duration-200 text-sm ${
                  isLight
                    ? 'bg-black hover:bg-black/85 text-white'
                    : 'bg-content-primary hover:bg-content-secondary text-surface-900'
                }`}
              >
                <Wand2 className="w-4 h-4" />
                Start for free
              </Link>
              <a
                href="mailto:hello@namines.com"
                className={`w-full sm:w-auto inline-flex items-center justify-center gap-2 font-semibold py-3 px-6 rounded-[var(--radius-card)] border transition-all duration-200 text-sm ${
                  isLight
                    ? 'border-black/20 text-black hover:bg-black/[0.03]'
                    : 'glass-button text-content-secondary hover:text-content-primary'
                }`}
              >
                <Mail className="w-3.5 h-3.5" />
                Talk to sales
              </a>
            </div>

            {/* "Girişsiz demo" çağrısı — küçük bir rozet gibi, gövde metninden
                ayrışsın diye kendi arka planı var (önceki hâl düz bir alt
                yazıydı, CTA'lardan hemen sonra kaybolup gidiyordu). */}
            <Link
              href="/demo"
              className={`mt-5 inline-flex items-center gap-2 pl-1 pr-3 py-1 rounded-full border text-xs transition-colors ${
                isLight
                  ? 'border-black/10 text-black/70 hover:text-black hover:bg-black/[0.03]'
                  : 'border-content-primary/10 text-content-muted hover:text-content-secondary hover:bg-white/[0.03]'
              }`}
            >
              <span className={`flex items-center justify-center w-5 h-5 rounded-full ${isLight ? 'bg-black/5' : 'bg-white/[0.06]'}`}>
                <Play className="w-2.5 h-2.5" />
              </span>
              Try the live demo — no account, no AI, real checks
            </Link>
          </div>

          {/* Sağ — büyük, çok tablolu şema illüstrasyonu */}
          <SchemaShowcase />
        </div>

        {/* Engine strip */}
        <div className="mt-12 sm:mt-16 flex flex-wrap items-center justify-center gap-2">
          {ENGINES.map(engine => (
            <span
              key={engine}
              className={`px-3 py-1 rounded-full border text-[11px] font-mono ${
                isLight
                  ? 'border-black/10 bg-black/[0.02] text-black/60'
                  : 'border-content-primary/10 bg-white/[0.02] text-content-muted'
              }`}
            >
              {engine}
            </span>
          ))}
        </div>
      </section>
    </div>
  );
}
