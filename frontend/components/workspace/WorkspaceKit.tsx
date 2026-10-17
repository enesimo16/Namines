'use client';

import React from 'react';
import { Loader2, Info, CheckCircle2, AlertTriangle, XCircle, type LucideIcon } from 'lucide-react';
export { Select, type SelectProps, type SelectOption } from '../ui/Select';

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
        <span className="shrink-0 flex items-center justify-center w-10 h-10 rounded-[var(--radius-card)] bg-surface-800 border border-line-strong text-namines-teal-vibrant shadow-xs">
          <Icon className="w-5 h-5" aria-hidden="true" />
        </span>
        <div className="min-w-0">
          <h1 className="text-xl font-bold tracking-tight text-content-primary leading-tight">{title}</h1>
          {description && (
            <p className="mt-1 text-xs leading-relaxed text-content-muted max-w-2xl">{description}</p>
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
  const hasChildren = Boolean(children);
  return (
    <section
      className={`rounded-[var(--radius-card)] border overflow-hidden shadow-xs ${
        tone === 'danger'
          ? 'border-danger/35 bg-surface-800'
          : 'border-line-strong bg-surface-800'
      }`}
    >
      {(title || actions) && (
        <div
          className={`flex flex-wrap items-center justify-between gap-3 px-5 py-4 ${
            hasChildren ? 'border-b border-line' : ''
          } ${tone === 'danger' ? 'bg-danger-subtle/15' : 'bg-surface-800/60'}`}
        >
          <div className="min-w-0 flex-1">
            {title && (
              <h2 className={`text-xs font-bold uppercase tracking-wider ${tone === 'danger' ? 'text-danger-text' : 'text-content-primary'}`}>
                {title}
              </h2>
            )}
            {description && (
              <p className="mt-1 text-xs leading-relaxed text-content-secondary max-w-xl">{description}</p>
            )}
          </div>
          {actions && <div className="flex items-center gap-2 shrink-0">{actions}</div>}
        </div>
      )}
      {hasChildren && <div className={flush ? '' : 'px-5 py-4'}>{children}</div>}
    </section>
  );
}

/* ── Bilgi / durum kutusu ───────────────────────────────────────────────── */
type CalloutTone = 'info' | 'success' | 'danger' | 'neutral';

const CALLOUT: Record<CalloutTone, { box: string; icon: string; Icon: LucideIcon; role: 'status' | 'alert' }> = {
  info: { box: 'border-accent/30 bg-accent-subtle/40', icon: 'text-accent-text', Icon: Info, role: 'status' },
  success: { box: 'border-success/30 bg-success-subtle/40', icon: 'text-success-text', Icon: CheckCircle2, role: 'status' },
  danger: { box: 'border-danger/30 bg-danger-subtle/40', icon: 'text-danger-text', Icon: XCircle, role: 'alert' },
  neutral: { box: 'border-line-strong bg-surface-700/60', icon: 'text-content-muted', Icon: AlertTriangle, role: 'status' },
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
    <div role={c.role} className={`flex flex-wrap sm:flex-nowrap items-start gap-3 rounded-[var(--radius-card)] border px-4 py-3 shadow-xs ${c.box}`}>
      <Icon className={`w-4 h-4 mt-0.5 shrink-0 ${c.icon}`} aria-hidden="true" />
      <div className="min-w-0 flex-1 text-xs leading-relaxed">
        {title && <p className="font-bold text-content-primary">{title}</p>}
        {children && <div className="text-content-secondary mt-0.5">{children}</div>}
      </div>
      {action && <div className="shrink-0">{action}</div>}
    </div>
  );
}

/* ── Rozet: renk + ikon + metin ─────────────────────────────────────────── */
type BadgeTone = 'neutral' | 'accent' | 'success' | 'danger';

const BADGE: Record<BadgeTone, string> = {
  neutral: 'bg-surface-700/80 border-line-strong text-content-secondary',
  accent: 'bg-accent-subtle/50 border-accent/30 text-accent-text',
  success: 'bg-success-subtle/50 border-success/30 text-success-text',
  danger: 'bg-danger-subtle/50 border-danger/30 text-danger-text',
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
      className={`inline-flex items-center gap-1.5 h-6 px-2.5 rounded-full text-micro font-bold border whitespace-nowrap shadow-xs ${BADGE[tone]}`}
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
          <dt className="text-micro font-bold uppercase tracking-wider text-content-muted">{item.label}</dt>
          <dd className={`mt-1 text-xs text-content-primary break-words font-medium ${item.mono ? 'font-mono text-micro' : ''}`}>
            {item.value}
          </dd>
        </div>
      ))}
    </dl>
  );
}

export function StatTile({ label, value, hint }: { label: string; value: React.ReactNode; hint?: string }) {
  return (
    <div className="rounded-[var(--radius-card)] border border-line-strong bg-surface-800 p-3.5 sm:p-4 shadow-xs min-w-0">
      <p className="text-micro font-bold uppercase tracking-wider text-content-muted truncate">{label}</p>
      <div className="mt-1 text-base sm:text-xl font-bold font-mono text-content-primary leading-tight break-words">{value}</div>
      {hint && <p className="mt-1 text-micro text-content-muted">{hint}</p>}
    </div>
  );
}

/* ── Buton ──────────────────────────────────────────────────────────────── */
type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger';

const BUTTON: Record<ButtonVariant, string> = {
  primary: 'bg-namines-teal-vibrant text-surface-900 font-bold hover:brightness-105 active:scale-95 shadow-xs',
  secondary: 'bg-surface-700 hover:bg-surface-600 text-content-primary border border-line-strong font-semibold shadow-xs',
  ghost: 'text-content-secondary hover:text-content-primary hover:bg-surface-700/60 font-medium',
  danger: 'bg-danger-subtle hover:bg-danger/25 text-danger-text border border-danger/30 font-semibold shadow-xs',
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
      className={`inline-flex items-center justify-center gap-1.5 rounded-[var(--radius-control)] text-xs transition-all cursor-pointer disabled:opacity-45 disabled:cursor-not-allowed ${focusRing} ${
        size === 'sm' ? 'h-8 px-2.5' : 'h-9 px-3.5'
      } ${children ? '' : size === 'sm' ? 'w-8 !px-0' : 'w-9 !px-0'} ${BUTTON[variant]} ${full ? 'w-full' : ''}`}
    >
      {busy ? <Loader2 className="w-3.5 h-3.5 animate-spin" aria-hidden="true" /> : Icon ? <Icon className="w-3.5 h-3.5 shrink-0" aria-hidden="true" /> : null}
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
      <span className="flex items-center justify-center w-12 h-12 rounded-full bg-surface-700 border border-line-strong text-content-muted shadow-xs">
        <Icon className="w-5 h-5" aria-hidden="true" />
      </span>
      <div className="space-y-1">
        <p className="text-sm font-bold text-content-primary">{title}</p>
        {description && <p className="text-xs leading-relaxed text-content-muted max-w-sm">{description}</p>}
      </div>
      {action && <div className="mt-2">{action}</div>}
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
      className={`relative shrink-0 w-10 h-6 rounded-full border transition-all cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed ${focusRing} ${
        checked ? 'bg-namines-teal-vibrant border-transparent' : 'bg-surface-600 border-line-strong'
      }`}
    >
      <span
        className={`absolute top-0.5 left-0.5 w-4.5 h-4.5 rounded-full shadow-xs transition-transform ${
          checked ? 'translate-x-4 bg-surface-900' : 'bg-content-primary'
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
      <label htmlFor={htmlFor} className="text-micro font-bold uppercase tracking-wider text-content-muted">
        {label}
      </label>
      {children}
      {hint && <span className="text-micro text-content-muted">{hint}</span>}
    </div>
  );
}

export const inputClass =
  `w-full h-9 rounded-[var(--radius-control)] border border-line-strong bg-surface-700/80 px-3 text-xs text-content-primary placeholder:text-content-muted hover:border-line-strong focus:outline-none focus:border-focus-ring focus:ring-1 focus:ring-focus-ring disabled:opacity-50 transition-all shadow-xs ${focusRing}`;

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
      <table className="w-full text-xs">
        <caption className="sr-only">{caption}</caption>
        <thead>
          <tr className="border-y border-line-strong bg-surface-700/60">
            {columns.map(col => (
              <th
                key={col.label}
                scope="col"
                className={`px-5 py-2.5 text-micro font-bold uppercase tracking-wider text-content-muted whitespace-nowrap ${
                  col.align === 'right' ? 'text-right' : 'text-left'
                }`}
              >
                {col.label}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-line">{children}</tbody>
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
        mono ? 'font-mono text-micro' : ''
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
    <div role="tablist" aria-label={ariaLabel} className="flex items-center gap-1 border-b border-line overflow-x-auto">
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
            className={`relative shrink-0 inline-flex items-center gap-2 h-10 px-4 text-xs font-semibold whitespace-nowrap transition-colors cursor-pointer ${focusRing} ${
              active ? 'text-content-primary font-bold' : 'text-content-muted hover:text-content-secondary font-medium'
            }`}
          >
            {Icon && <Icon className="w-4 h-4" aria-hidden="true" />}
            {tab.label}
            {tab.count !== undefined && (
              <span className="inline-flex items-center justify-center min-w-5 h-5 px-1.5 rounded-full bg-surface-600 text-micro text-content-secondary font-mono">
                {tab.count}
              </span>
            )}
            {active && <span className="absolute left-3 right-3 -bottom-px h-0.5 rounded-full bg-namines-teal-vibrant" />}
          </button>
        );
      })}
    </div>
  );
}

/* ══════════════════════════════════════════════════════════════════════
   SaaS Design System — Shopify Polaris Component Naming Standard (Section 29)
   ══════════════════════════════════════════════════════════════════════ */

/** Alias for PageShell */
export const AppShell = PageShell;
export const PageContainer = PageShell;

/** Alias for StatTile */
export const KpiCard = StatTile;

export function CardHeader({
  title,
  description,
  actions,
  className = '',
}: {
  title?: React.ReactNode;
  description?: React.ReactNode;
  actions?: React.ReactNode;
  className?: string;
}) {
  return (
    <div className={`flex flex-wrap items-start justify-between gap-3 px-5 py-3.5 border-b border-line bg-surface-800/60 ${className}`}>
      <div className="min-w-0">
        {title && (typeof title === 'string' ? <h2 className="text-xs font-bold uppercase tracking-wider text-content-primary">{title}</h2> : title)}
        {description && (typeof description === 'string' ? <p className="mt-0.5 text-micro leading-relaxed text-content-muted max-w-xl">{description}</p> : description)}
      </div>
      {actions && <div className="flex items-center gap-2 shrink-0">{actions}</div>}
    </div>
  );
}

export function CardBody({
  children,
  className = '',
  flush = false,
}: {
  children: React.ReactNode;
  className?: string;
  flush?: boolean;
}) {
  return <div className={`${flush ? '' : 'px-5 py-4'} ${className}`}>{children}</div>;
}

export function CardFooter({
  children,
  actions,
  className = '',
}: {
  children?: React.ReactNode;
  actions?: React.ReactNode;
  className?: string;
}) {
  return (
    <div className={`flex flex-wrap items-center justify-between gap-3 px-5 py-3 border-t border-line bg-surface-800/40 text-xs text-content-muted ${className}`}>
      <div className="min-w-0">{children}</div>
      {actions && <div className="flex items-center gap-2 shrink-0">{actions}</div>}
    </div>
  );
}

export function PageActions({
  children,
  className = '',
}: {
  children: React.ReactNode;
  className?: string;
}) {
  return <div className={`flex flex-wrap items-center gap-2 shrink-0 ${className}`}>{children}</div>;
}

export function TableToolbar({
  search,
  filters,
  actions,
  className = '',
}: {
  search?: React.ReactNode;
  filters?: React.ReactNode;
  actions?: React.ReactNode;
  className?: string;
}) {
  return (
    <div className={`flex flex-col sm:flex-row sm:items-center justify-between gap-3 px-5 py-3 border-b border-line bg-surface-800/40 ${className}`}>
      <div className="flex flex-1 items-center gap-2.5 min-w-0">
        {search}
        {filters}
      </div>
      {actions && <div className="flex items-center gap-2 shrink-0">{actions}</div>}
    </div>
  );
}

export function TablePagination({
  page,
  totalPages,
  onPageChange,
  totalItems,
}: {
  page: number;
  totalPages: number;
  onPageChange: (newPage: number) => void;
  totalItems?: number;
}) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-3 px-5 py-3 border-t border-line text-micro text-content-muted">
      <div>
        {totalItems !== undefined && (
          <span>
            Toplam <strong className="font-semibold text-content-primary">{totalItems}</strong> kayıt
          </span>
        )}
      </div>
      <div className="flex items-center gap-2">
        <span className="text-content-secondary">
          Sayfa <strong className="font-semibold text-content-primary">{page}</strong> / {totalPages}
        </span>
        <div className="flex items-center gap-1">
          <Button
            size="sm"
            variant="ghost"
            disabled={page <= 1}
            onClick={() => onPageChange(page - 1)}
            aria-label="Önceki Sayfa"
          >
            Önceki
          </Button>
          <Button
            size="sm"
            variant="ghost"
            disabled={page >= totalPages}
            onClick={() => onPageChange(page + 1)}
            aria-label="Sonraki Sayfa"
          >
            Sonraki
          </Button>
        </div>
      </div>
    </div>
  );
}

export function Skeleton({
  className = '',
  width,
  height,
}: {
  className?: string;
  width?: string;
  height?: string;
}) {
  return (
    <div
      style={{ width, height }}
      className={`animate-pulse rounded-[var(--radius-control)] bg-surface-700/60 ${className}`}
      aria-hidden="true"
    />
  );
}

export interface InputProps extends React.InputHTMLAttributes<HTMLInputElement> {
  icon?: LucideIcon;
  error?: string;
}

export const Input = React.forwardRef<HTMLInputElement, InputProps>(
  ({ icon: Icon, error, className = '', ...props }, ref) => {
    return (
      <div className="relative w-full">
        {Icon && (
          <span className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none text-content-muted">
            <Icon className="w-3.5 h-3.5" aria-hidden="true" />
          </span>
        )}
        <input
          ref={ref}
          className={`${inputClass} ${Icon ? 'pl-8' : ''} ${error ? 'border-danger/60 focus:border-danger' : ''} ${className}`}
          {...props}
        />
        {error && <p className="mt-1 text-micro text-danger-text">{error}</p>}
      </div>
    );
  }
);
Input.displayName = 'Input';

