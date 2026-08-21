'use client';

import React from 'react';
import { LucideIcon, Loader2 } from 'lucide-react';

/**
 * Compile ekranının ORTAK BÖLMELERİ — 8 panelin (DDL, EF Core, Mermaid, Test Data,
 * Data Dictionary, README, Docker Sandbox, Developer Package) tamamı buradaki
 * primitifleri kullanır.
 *
 * Neden: her panel kendi başlığını, açıklama paragrafını ve kart kromunu yeniden
 * çiziyordu. Kabuğun (app/compile/page.tsx) üst şeridi zaten aktif sekmenin adını
 * gösterdiği için bu başlıklar BİLGİ TEKRARIYDI ve p-6/p-7/p-8 dolgularla birleşince
 * içeriği 1080p ekranda bile scroll'a itiyordu. Buradaki ölçek bilinçli olarak dar:
 * dolgu 8-12px, metin 11px, etiket 10px — "developer console" yoğunluğu, pazarlama
 * sayfası değil.
 *
 * Renkler yalnızca token üzerinden (FRONTEND.md §4) — component'te ham hex yok.
 */

/* ── Yüzey: her panelin dış kabuğu. Tek scroll noktası burasıdır. ───────────── */
export function Panel({
  children,
  scroll = true,
  className = '',
}: {
  children: React.ReactNode;
  scroll?: boolean;
  className?: string;
}) {
  return (
    <div
      className={`w-full h-full min-h-0 flex flex-col bg-surface-700 border border-surface-500 rounded-lg ${className}`}
    >
      <div className={`flex-1 min-h-0 ${scroll ? 'overflow-y-auto' : 'overflow-hidden'}`}>
        {children}
      </div>
    </div>
  );
}

/* ── Dar aksiyon şeridi — panel içi başlık YERİNE geçer. Sadece kontroller. ─── */
export function PanelBar({
  left,
  children,
}: {
  left?: React.ReactNode;
  children?: React.ReactNode;
}) {
  return (
    <div className="shrink-0 flex items-center justify-between gap-3 h-9 px-2.5 border-b border-surface-500 bg-surface-800">
      <div className="flex items-center gap-2 min-w-0">{left}</div>
      <div className="flex items-center gap-1.5 shrink-0">{children}</div>
    </div>
  );
}

/* ── Segment seçici (dil, görünüm modu, motor) — native select yerine. ──────── */
export function Segmented<T extends string>({
  value,
  onChange,
  options,
  ariaLabel,
}: {
  value: T;
  onChange: (v: T) => void;
  options: { value: T; label: string }[];
  ariaLabel: string;
}) {
  return (
    <div role="group" aria-label={ariaLabel} className="flex items-center gap-0.5 bg-surface-600 rounded-md p-0.5">
      {options.map(opt => {
        const active = opt.value === value;
        return (
          <button
            key={opt.value}
            onClick={() => onChange(opt.value)}
            aria-pressed={active}
            className={`px-2 h-6 rounded text-[10px] font-semibold transition-colors cursor-pointer focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-[var(--color-focus-ring)] ${
              active
                ? 'bg-accent-subtle text-accent-text'
                : 'text-content-muted hover:text-content-secondary'
            }`}
          >
            {opt.label}
          </button>
        );
      })}
    </div>
  );
}

/* ── İkon butonu — 32px, aria-label ZORUNLU (FRONTEND.md §6). ───────────────── */
export function IconButton({
  icon: Icon,
  label,
  onClick,
  disabled,
  busy,
  tone = 'default',
}: {
  icon: LucideIcon;
  label: string;
  onClick: () => void;
  disabled?: boolean;
  busy?: boolean;
  tone?: 'default' | 'primary';
}) {
  return (
    <button
      onClick={onClick}
      disabled={disabled || busy}
      title={label}
      aria-label={label}
      className={`flex items-center justify-center w-8 h-8 rounded-md transition-colors cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-[var(--color-focus-ring)] ${
        tone === 'primary'
          ? 'bg-accent-subtle text-accent-text hover:bg-accent-hover/30'
          : 'text-content-muted hover:text-content-primary hover:bg-surface-600'
      }`}
    >
      {busy ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Icon className="w-3.5 h-3.5" />}
    </button>
  );
}

/* ── Metinli buton — birincil eylem 36px yükseklik. ─────────────────────────── */
export function ActionButton({
  icon: Icon,
  children,
  onClick,
  disabled,
  busy,
  tone = 'default',
  full,
}: {
  icon?: LucideIcon;
  children: React.ReactNode;
  onClick?: () => void;
  disabled?: boolean;
  busy?: boolean;
  tone?: 'default' | 'primary' | 'danger';
  full?: boolean;
}) {
  const tones = {
    default: 'bg-surface-600 text-content-secondary hover:text-content-primary hover:bg-surface-500/40',
    primary: 'bg-content-primary text-surface-900 hover:opacity-90 font-semibold',
    danger: 'bg-[var(--color-danger-subtle)] text-[var(--color-danger)] hover:bg-[var(--color-danger)]/20',
  };
  return (
    <button
      onClick={onClick}
      disabled={disabled || busy}
      className={`inline-flex items-center justify-center gap-1.5 h-9 px-3 rounded-md text-[11px] font-medium transition-all cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-[var(--color-focus-ring)] ${tones[tone]} ${full ? 'w-full' : ''}`}
    >
      {busy ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : Icon ? <Icon className="w-3.5 h-3.5" /> : null}
      {children}
    </button>
  );
}

/* ── Boş/başlangıç durumu — eski "hero" bloklarının dar karşılığı. ──────────── */
export function PanelEmpty({
  icon: Icon,
  title,
  hint,
  children,
}: {
  icon: LucideIcon;
  title: string;
  hint?: string;
  children?: React.ReactNode;
}) {
  return (
    <div className="h-full flex flex-col items-center justify-center gap-2.5 px-6 text-center">
      <Icon className="w-5 h-5 text-content-muted" />
      <div className="space-y-1">
        <p className="text-[12px] font-semibold text-content-primary">{title}</p>
        {hint && <p className="text-[11px] text-content-muted max-w-sm leading-relaxed">{hint}</p>}
      </div>
      {children && <div className="mt-1 flex items-center gap-2">{children}</div>}
    </div>
  );
}

/* ── Kod/metin yüzeyi — SQL, EF Core, README raw, seed script hepsi bunu kullanır. */
export function CodeSurface({ children }: { children: React.ReactNode }) {
  return (
    <pre className="h-full overflow-auto m-0 p-3 bg-surface-900 text-[11px] leading-relaxed font-mono text-content-secondary">
      {children}
    </pre>
  );
}

/* ── Satır içi metrik — panel üstü özet şeridi için. ────────────────────────── */
export function StatStrip({ items }: { items: { label: string; value: React.ReactNode }[] }) {
  return (
    <div className="shrink-0 flex items-center gap-4 px-3 h-8 border-b border-surface-500 bg-surface-800">
      {items.map(it => (
        <div key={it.label} className="flex items-baseline gap-1.5">
          <span className="text-[11px] font-mono font-semibold text-content-primary">{it.value}</span>
          <span className="text-[9px] uppercase tracking-wider text-content-muted">{it.label}</span>
        </div>
      ))}
    </div>
  );
}

/* ── Seçenek kartı — Docker Sandbox / Developer Package seçimleri için. ─────── */
export function OptionCard({
  icon: Icon,
  title,
  description,
  badge,
  bullets,
  action,
  disabled,
}: {
  icon: LucideIcon;
  title: string;
  description: string;
  badge?: string;
  bullets?: string[];
  action: React.ReactNode;
  disabled?: boolean;
}) {
  return (
    <div
      className={`flex flex-col gap-2.5 p-3 rounded-lg border bg-surface-600 border-surface-500 ${
        disabled ? 'opacity-50' : ''
      }`}
    >
      <div className="flex items-start gap-2.5">
        <span className="shrink-0 flex items-center justify-center w-7 h-7 rounded-md bg-accent-subtle text-accent-text">
          <Icon className="w-3.5 h-3.5" />
        </span>
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-1.5">
            <h3 className="text-[12px] font-semibold text-content-primary truncate">{title}</h3>
            {badge && (
              <span className="shrink-0 text-[9px] font-bold uppercase tracking-wider text-content-muted bg-surface-500/30 px-1.5 py-0.5 rounded">
                {badge}
              </span>
            )}
          </div>
          <p className="text-[11px] text-content-muted leading-snug mt-0.5">{description}</p>
        </div>
      </div>

      {bullets && bullets.length > 0 && (
        <ul className="space-y-1 pl-9">
          {bullets.map(b => (
            <li key={b} className="flex items-start gap-1.5 text-[10px] text-content-secondary leading-snug">
              <span className="text-content-muted shrink-0 mt-px">·</span>
              <span>{b}</span>
            </li>
          ))}
        </ul>
      )}

      <div className="pl-9">{action}</div>
    </div>
  );
}
