'use client';

import React from 'react';
import { Loader2, Info, CheckCircle2, AlertTriangle, XCircle, type LucideIcon } from 'lucide-react';

/**
 * Çalışma alanı sayfalarının (Flow, Ground, Vault, Launch, Review) ORTAK tasarım
 * dili.
 *
 * Neden ayrı bir kit: bu sayfalar `compile/PanelKit`'i kullanıyordu — o kit
 * "developer console yoğunluğu" için bilerek 10-11px metin ve 8-12px dolguyla
 * yazılmıştı (kod panelleri için doğru), ama yönetim ekranlarında (veritabanı
 * durumu, yedek listesi, kurallar) okunması zor, sıkışık ve hiyerarşisiz görünüyordu:
 * her şey aynı boyutta, tablolar ham, uyarılar renkli kutu.
 *
 * Buradaki ölçek: gövde 13px, etiket 12px, en küçük metin 11px (altına inilmiyor),
 * bölüm dolgusu 16-20px, başlık > bölüm başlığı > etiket üç belirgin adım.
 * Renkler yalnızca token (FRONTEND.md §2/§4) — ham hex yok. Renk TEK BAŞINA anlam
 * taşımıyor: her durum bir ikon ve bir metinle birlikte (FRONTEND.md §6).
 */

const focusRing =
  'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--color-focus-ring)] focus-visible:ring-offset-0';

/* ── Sayfa kabuğu ────────────────────────────────────────────────────────── */
export function PageShell({
  children,
  width = 'max-w-5xl',
}: {
  children: React.ReactNode;
  width?: string;
}) {
  return (
    <div className="min-h-[calc(100vh-56px)] bg-surface-900 text-content-primary font-sans">
      <div className={`mx-auto w-full ${width} px-4 sm:px-6 py-6 sm:py-8 space-y-6`}>{children}</div>
    </div>
  );
}

export function PageHeader({
  icon: Icon,
  title,
  description,
  actions,
  meta,
}: {
  icon: LucideIcon;
  title: string;
  description?: string;
  actions?: React.ReactNode;
  meta?: React.ReactNode;
}) {
  return (
    <header className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
      <div className="flex items-start gap-3.5 min-w-0">
        <span className="shrink-0 flex items-center justify-center w-10 h-10 rounded-[var(--radius-card)] bg-accent-subtle text-accent-text">
          <Icon className="w-5 h-5" aria-hidden="true" />
        </span>
        <div className="min-w-0">
          <h1 className="text-xl font-semibold tracking-tight text-content-primary leading-tight">{title}</h1>
          {description && (
            <p className="mt-1 text-[13px] leading-relaxed text-content-muted max-w-2xl">{description}</p>
          )}
          {meta && <div className="mt-2 flex flex-wrap items-center gap-2">{meta}</div>}
        </div>
      </div>
      {actions && <div className="flex flex-wrap items-center gap-2 shrink-0">{actions}</div>}
    </header>
  );
}

/* ── Bölüm kartı ────────────────────────────────────────────────────────── */
export function Card({
  title,
  description,
  actions,
  children,
  tone = 'default',
  flush,
}: {
  title?: string;
  description?: string;
  actions?: React.ReactNode;
  children?: React.ReactNode;
  tone?: 'default' | 'danger';
  /** İçerik kenardan kenara (tablo gibi) — iç dolgu verilmez. */
  flush?: boolean;
}) {
  return (
    <section
      className={`rounded-[var(--radius-card)] border overflow-hidden ${
        tone === 'danger'
          ? 'border-danger/40 bg-danger-subtle/40'
          : 'border-content-primary/10 bg-surface-800'
      }`}
    >
      {(title || actions) && (
        <div className="flex flex-wrap items-start justify-between gap-3 px-5 pt-4 pb-3">
          <div className="min-w-0">
            {title && <h2 className="text-sm font-semibold text-content-primary">{title}</h2>}
            {description && (
              <p className="mt-0.5 text-[12px] leading-relaxed text-content-muted max-w-xl">{description}</p>
            )}
          </div>
          {actions && <div className="flex items-center gap-2 shrink-0">{actions}</div>}
        </div>
      )}
      {children != null && <div className={flush ? '' : 'px-5 pb-5 pt-1'}>{children}</div>}
    </section>
  );
}

/* ── Bilgi / durum kutusu ───────────────────────────────────────────────── */
type CalloutTone = 'info' | 'success' | 'danger' | 'neutral';

const CALLOUT: Record<CalloutTone, { box: string; icon: string; Icon: LucideIcon; role: 'status' | 'alert' }> = {
  info: { box: 'border-accent/30 bg-accent-subtle', icon: 'text-accent-text', Icon: Info, role: 'status' },
  success: { box: 'border-success/30 bg-success-subtle', icon: 'text-success-text', Icon: CheckCircle2, role: 'status' },
  danger: { box: 'border-danger/40 bg-danger-subtle', icon: 'text-danger-text', Icon: XCircle, role: 'alert' },
  neutral: { box: 'border-content-primary/10 bg-surface-700', icon: 'text-content-muted', Icon: AlertTriangle, role: 'status' },
};

export function Callout({
  tone = 'info',
  title,
  children,
  action,
  icon,
}: {
  tone?: CalloutTone;
  title?: string;
  children?: React.ReactNode;
  action?: React.ReactNode;
  icon?: LucideIcon;
}) {
  const c = CALLOUT[tone];
  const Icon = icon ?? c.Icon;
  return (
    <div role={c.role} className={`flex items-start gap-3 rounded-[var(--radius-card)] border px-4 py-3 ${c.box}`}>
      <Icon className={`w-4 h-4 mt-0.5 shrink-0 ${c.icon}`} aria-hidden="true" />
      <div className="min-w-0 flex-1 text-[13px] leading-relaxed">
        {title && <p className="font-semibold text-content-primary">{title}</p>}
        {children && <div className="text-content-secondary">{children}</div>}
      </div>
      {action && <div className="shrink-0">{action}</div>}
    </div>
  );
}

/* ── Rozet: renk + ikon + metin ─────────────────────────────────────────── */
type BadgeTone = 'neutral' | 'accent' | 'success' | 'danger';

const BADGE: Record<BadgeTone, string> = {
  neutral: 'bg-surface-600 text-content-secondary',
  accent: 'bg-accent-subtle text-accent-text',
  success: 'bg-success-subtle text-success-text',
  danger: 'bg-danger-subtle text-danger-text',
};

export function Badge({
  tone = 'neutral',
  icon: Icon,
  children,
  title,
}: {
  tone?: BadgeTone;
  icon?: LucideIcon;
  children: React.ReactNode;
  title?: string;
}) {
  return (
    <span
      title={title}
      className={`inline-flex items-center gap-1 h-6 px-2 rounded-full text-[11px] font-medium whitespace-nowrap ${BADGE[tone]}`}
    >
      {Icon && <Icon className="w-3 h-3 shrink-0" aria-hidden="true" />}
      {children}
    </span>
  );
}

/* ── Etiket → değer ızgarası (eski ham tablonun yerine) ──────────────────── */
export function DefinitionGrid({
  items,
  columns = 3,
}: {
  items: { label: string; value: React.ReactNode; mono?: boolean }[];
  columns?: 2 | 3;
}) {
  return (
    <dl className={`grid grid-cols-1 sm:grid-cols-2 ${columns === 3 ? 'lg:grid-cols-3' : ''} gap-x-6 gap-y-4`}>
      {items.map(item => (
        <div key={item.label} className="min-w-0">
          <dt className="text-[11px] font-medium uppercase tracking-wider text-content-subtle">{item.label}</dt>
          <dd className={`mt-1 text-[13px] text-content-primary break-words ${item.mono ? 'font-mono text-[12px]' : ''}`}>
            {item.value}
          </dd>
        </div>
      ))}
    </dl>
  );
}

export function StatTile({ label, value, hint }: { label: string; value: React.ReactNode; hint?: string }) {
  return (
    <div className="rounded-[var(--radius-card)] border border-content-primary/10 bg-surface-800 px-4 py-3">
      <p className="text-[11px] font-medium uppercase tracking-wider text-content-subtle">{label}</p>
      <p className="mt-1 text-lg font-semibold font-mono text-content-primary leading-tight">{value}</p>
      {hint && <p className="mt-0.5 text-[11px] text-content-muted">{hint}</p>}
    </div>
  );
}

/* ── Buton ──────────────────────────────────────────────────────────────── */
type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger';

const BUTTON: Record<ButtonVariant, string> = {
  primary: 'bg-content-primary text-surface-900 hover:bg-content-primary-hover font-semibold',
  secondary: 'bg-surface-700 text-content-primary border border-content-primary/10 hover:bg-surface-600 hover:border-content-primary/20',
  ghost: 'text-content-secondary hover:text-content-primary hover:bg-content-primary/[0.06]',
  danger: 'bg-danger-subtle text-danger-text border border-danger/30 hover:bg-danger/20',
};

export function Button({
  variant = 'secondary',
  icon: Icon,
  busy,
  disabled,
  children,
  onClick,
  type = 'button',
  title,
  size = 'md',
  full,
  ...aria
}: {
  variant?: ButtonVariant;
  icon?: LucideIcon;
  busy?: boolean;
  disabled?: boolean;
  children?: React.ReactNode;
  onClick?: () => void;
  type?: 'button' | 'submit';
  title?: string;
  size?: 'sm' | 'md';
  full?: boolean;
} & Pick<React.AriaAttributes, 'aria-label' | 'aria-expanded' | 'aria-pressed'>) {
  return (
    <button
      type={type}
      onClick={onClick}
      disabled={disabled || busy}
      title={title}
      {...aria}
      className={`inline-flex items-center justify-center gap-2 rounded-[var(--radius-control)] text-[13px] transition-colors cursor-pointer disabled:opacity-45 disabled:cursor-not-allowed ${focusRing} ${
        size === 'sm' ? 'h-8 px-3' : 'h-9 px-3.5'
      } ${children ? '' : size === 'sm' ? 'w-8 !px-0' : 'w-9 !px-0'} ${BUTTON[variant]} ${full ? 'w-full' : ''}`}
    >
      {busy ? <Loader2 className="w-4 h-4 animate-spin" aria-hidden="true" /> : Icon ? <Icon className="w-4 h-4" aria-hidden="true" /> : null}
      {children}
    </button>
  );
}

/* ── Boş durum ──────────────────────────────────────────────────────────── */
export function EmptyState({
  icon: Icon,
  title,
  description,
  action,
}: {
  icon: LucideIcon;
  title: string;
  description?: string;
  action?: React.ReactNode;
}) {
  return (
    <div className="flex flex-col items-center justify-center gap-3 text-center px-6 py-12">
      <span className="flex items-center justify-center w-11 h-11 rounded-full bg-surface-700 text-content-muted">
        <Icon className="w-5 h-5" aria-hidden="true" />
      </span>
      <div className="space-y-1">
        <p className="text-sm font-semibold text-content-primary">{title}</p>
        {description && <p className="text-[13px] leading-relaxed text-content-muted max-w-sm">{description}</p>}
      </div>
      {action && <div className="mt-1">{action}</div>}
    </div>
  );
}

/* ── Anahtar (aç/kapa) ──────────────────────────────────────────────────── */
export function Switch({
  checked,
  onChange,
  label,
  disabled,
}: {
  checked: boolean;
  onChange: (next: boolean) => void;
  label: string;
  disabled?: boolean;
}) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={label}
      disabled={disabled}
      onClick={() => onChange(!checked)}
      className={`relative shrink-0 w-10 h-6 rounded-full transition-colors cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed ${focusRing} ${
        checked ? 'bg-accent-hover' : 'bg-surface-500'
      }`}
    >
      <span
        className={`absolute top-1 left-1 w-4 h-4 rounded-full bg-content-primary transition-transform ${
          checked ? 'translate-x-4' : ''
        }`}
      />
    </button>
  );
}

/* ── Form alanı ─────────────────────────────────────────────────────────── */
export function Field({
  label,
  hint,
  children,
  htmlFor,
}: {
  label: string;
  hint?: string;
  children: React.ReactNode;
  htmlFor?: string;
}) {
  return (
    <div className="flex flex-col gap-1.5 min-w-0">
      <label htmlFor={htmlFor} className="text-[12px] font-medium text-content-secondary">
        {label}
      </label>
      {children}
      {hint && <span className="text-[11px] text-content-muted">{hint}</span>}
    </div>
  );
}

export const inputClass =
  `w-full h-9 rounded-[var(--radius-control)] border border-content-primary/10 bg-surface-700 px-3 text-[13px] text-content-primary placeholder:text-content-muted hover:border-content-primary/20 disabled:opacity-50 ${focusRing}`;

/* ── Tablo ──────────────────────────────────────────────────────────────── */
export function DataTable({
  columns,
  children,
  caption,
}: {
  columns: { label: string; align?: 'right' }[];
  children: React.ReactNode;
  caption: string;
}) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-[13px]">
        <caption className="sr-only">{caption}</caption>
        <thead>
          <tr className="border-y border-content-primary/10 bg-surface-700/50">
            {columns.map(col => (
              <th
                key={col.label}
                scope="col"
                className={`px-5 py-2.5 text-[11px] font-medium uppercase tracking-wider text-content-subtle whitespace-nowrap ${
                  col.align === 'right' ? 'text-right' : 'text-left'
                }`}
              >
                {col.label}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-content-primary/[0.07]">{children}</tbody>
      </table>
    </div>
  );
}

export function Td({
  children,
  align,
  mono,
}: {
  children: React.ReactNode;
  align?: 'right';
  mono?: boolean;
}) {
  return (
    <td
      className={`px-5 py-3 text-content-secondary whitespace-nowrap ${align === 'right' ? 'text-right' : 'text-left'} ${
        mono ? 'font-mono text-[12px]' : ''
      }`}
    >
      {children}
    </td>
  );
}

/* ── Sekmeler (WAI-ARIA tablist; ok tuşlarıyla gezinilir) ────────────────── */
export function Tabs<T extends string>({
  value,
  onChange,
  tabs,
  ariaLabel,
}: {
  value: T;
  onChange: (next: T) => void;
  tabs: { value: T; label: string; count?: number; icon?: LucideIcon }[];
  ariaLabel: string;
}) {
  const move = (event: React.KeyboardEvent, index: number) => {
    let next = index;
    if (event.key === 'ArrowRight') next = (index + 1) % tabs.length;
    else if (event.key === 'ArrowLeft') next = (index - 1 + tabs.length) % tabs.length;
    else if (event.key === 'Home') next = 0;
    else if (event.key === 'End') next = tabs.length - 1;
    else return;
    event.preventDefault();
    onChange(tabs[next].value);
    document.getElementById(`tab-${tabs[next].value}`)?.focus();
  };

  return (
    <div role="tablist" aria-label={ariaLabel} className="flex items-center gap-1 border-b border-content-primary/10 overflow-x-auto">
      {tabs.map((tab, index) => {
        const active = tab.value === value;
        const Icon = tab.icon;
        return (
          <button
            key={tab.value}
            id={`tab-${tab.value}`}
            role="tab"
            type="button"
            aria-selected={active}
            aria-controls={`tabpanel-${tab.value}`}
            tabIndex={active ? 0 : -1}
            onClick={() => onChange(tab.value)}
            onKeyDown={event => move(event, index)}
            className={`relative shrink-0 inline-flex items-center gap-2 h-10 px-4 text-[13px] font-medium whitespace-nowrap transition-colors cursor-pointer ${focusRing} ${
              active ? 'text-content-primary' : 'text-content-muted hover:text-content-secondary'
            }`}
          >
            {Icon && <Icon className="w-4 h-4" aria-hidden="true" />}
            {tab.label}
            {tab.count !== undefined && (
              <span className="inline-flex items-center justify-center min-w-5 h-5 px-1.5 rounded-full bg-surface-600 text-[11px] text-content-secondary font-mono">
                {tab.count}
              </span>
            )}
            {active && <span className="absolute left-3 right-3 -bottom-px h-0.5 rounded-full bg-accent-hover" />}
          </button>
        );
      })}
    </div>
  );
}
