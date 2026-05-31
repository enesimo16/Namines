'use client';

import { LucideIcon } from 'lucide-react';

interface BadgeItem {
  label: string;
  color?: 'emerald' | 'sky' | 'violet' | 'amber';
}

interface SandboxActionCardProps {
  /** Kart başlığı — örn: "AI Admin Paneli" */
  title: string;
  /** Alt başlık / açıklama */
  description: string;
  /** İkincil küçük açıklama (renksiz, soluk) */
  meta?: string;
  /** Üstteki ikon bileşeni */
  Icon: LucideIcon;
  /** İkon arka plan rengi (Tailwind sınıfı) */
  iconBg?: string;
  /** İkon rengi (Tailwind sınıfı) */
  iconColor?: string;
  /** Özellik badge'leri */
  badges?: BadgeItem[];
  /** CTA buton metni */
  buttonLabel: string;
  /** Buton tıklandığında */
  onAction: () => void;
  /** Buton devre dışı mı */
  disabled?: boolean;
}

const BADGE_STYLES: Record<NonNullable<BadgeItem['color']>, string> = {
  emerald: 'bg-emerald-500/10 border-emerald-700/40 text-emerald-400',
  sky: 'bg-sky-500/10 border-sky-700/40 text-sky-400',
  violet: 'bg-violet-500/10 border-violet-700/40 text-violet-400',
  amber: 'bg-amber-500/10 border-amber-700/40 text-amber-400',
};

/**
 * SandboxActionCard
 * ─────────────────────────────────────────────────────────────────────────────
 * Ortak glassmorphism wrapper bileşeni.
 * Docker Sandbox (.bak) ve AI Admin Panel (Streamlit) idle kartları
 * bu bileşeni kullanarak birebir aynı görünüme ve hizalamaya sahip olur.
 */
export default function SandboxActionCard({
  title,
  description,
  meta,
  Icon,
  iconBg = 'bg-[#022c22]',
  iconColor = 'text-[#10b981]',
  badges = [],
  buttonLabel,
  onAction,
  disabled = false,
}: SandboxActionCardProps) {
  return (
    <div className="w-full h-full flex flex-col items-center justify-center bg-[#050505] rounded-xl border border-zinc-800/60 overflow-hidden relative">

      {/* Ambient glow */}
      <div className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[600px] h-[400px] bg-emerald-500/5 rounded-full blur-[100px] pointer-events-none" />

      {/* Card */}
      <div className="relative z-10 w-full max-w-[520px] mx-8 bg-[#0c0c0c] border border-zinc-800/80 rounded-[28px] p-12 flex flex-col items-center shadow-2xl">

        {/* Icon */}
        <div
          className={`w-20 h-20 ${iconBg} rounded-2xl flex items-center justify-center mb-6 border border-emerald-900/30 shadow-[0_0_30px_rgba(16,185,129,0.1)]`}
        >
          <Icon className={`w-9 h-9 ${iconColor}`} strokeWidth={2} />
        </div>

        {/* Title */}
        <h2 className="text-[26px] font-bold text-white mb-3 tracking-wide text-center">
          {title}
        </h2>

        {/* Description */}
        <p className="text-[#a1a1aa] text-[15px] mb-2 text-center leading-relaxed">
          {description}
        </p>

        {/* Meta */}
        {meta && (
          <p className="text-zinc-600 text-[13px] mb-8 font-medium text-center">
            {meta}
          </p>
        )}

        {/* Badges */}
        {badges.length > 0 && (
          <div className="flex flex-wrap items-center justify-center gap-2.5 mb-10 w-full px-4">
            {badges.map(({ label, color = 'emerald' }) => (
              <span
                key={label}
                className={`px-3.5 py-1 text-[12px] font-semibold rounded-full border tracking-wide uppercase ${BADGE_STYLES[color]}`}
              >
                {label}
              </span>
            ))}
          </div>
        )}

        {/* CTA Button */}
        <button
          onClick={onAction}
          disabled={disabled}
          className="
            group flex items-center justify-center gap-2.5
            px-8 py-3.5
            bg-gradient-to-r from-emerald-600 to-teal-600
            hover:from-emerald-500 hover:to-teal-500
            text-white font-semibold text-[15px] rounded-xl
            transition-all duration-200
            shadow-[0_0_25px_rgba(16,185,129,0.35)]
            hover:shadow-[0_0_40px_rgba(16,185,129,0.55)]
            hover:scale-[1.02] active:scale-[0.98]
            disabled:opacity-50 disabled:cursor-not-allowed disabled:hover:scale-100
          "
        >
          {buttonLabel}
        </button>
      </div>
    </div>
  );
}
