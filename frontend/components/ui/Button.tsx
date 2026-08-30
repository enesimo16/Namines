import React from 'react';

/**
 * Tek buton primitifi.
 *
 * <b>Neden gerekti:</b> denetimde `<button>` sayıldı — **286 adet, sıfır
 * paylaşılan primitif**. `components/ui/` dizini yoktu. Aynı birincil buton
 * şu varyasyonlarla el yazması olarak yazılmıştı: `rounded-lg`/`rounded-xl`,
 * `py-2`/`py-2.5`/`py-3`, `text-xs`/`text-sm`, `font-semibold`/`font-bold`.
 * Bir buton kararını değiştirmek 286 dosyaya dokunmak demekti.
 *
 * Vercel'de ölçülen: TEK yükseklik (40px), TEK radius (6px), TEK yatay dolgu
 * (12px). Varyasyon yalnızca zemin ve kenarlıkta — geometride değil.
 * Buradaki üç boyut da aynı disiplinde: geometri sabit, yalnızca ölçek değişir.
 *
 * <b>`focus-ring` varsayılan:</b> denetimde 41 `outline-none` bulundu ama
 * yalnızca 10 `focus-visible` — odak halkası çoğunlukla kaldırılmış, yerine
 * bir şey konmamıştı. Primitif bunu varsayılan yapıyor ki bir daha unutulmasın.
 */

type Variant = 'primary' | 'secondary' | 'ghost' | 'danger';
type Size = 'sm' | 'md' | 'lg';

const VARIANT: Record<Variant, string> = {
  primary:
    'bg-content-primary text-surface-900 hover:bg-content-primary-hover border border-transparent',
  secondary:
    'bg-surface-700 text-content-secondary hover:text-content-primary hover:bg-surface-600 border border-[var(--color-border-hairline)] hover:border-[var(--color-border-strong)]',
  ghost:
    'bg-transparent text-content-muted hover:text-content-primary hover:bg-surface-700 border border-transparent',
  danger:
    'bg-danger text-content-primary hover:bg-danger/90 border border-transparent',
};

const SIZE: Record<Size, string> = {
  sm: 'h-8 px-2.5 gap-1.5 text-caption',
  md: 'h-9 px-3 gap-2 text-label',
  lg: 'h-11 px-4 gap-2 text-label',
};

interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant;
  size?: Size;
  /** Yalnızca ikon içeren buton — kare oran ve zorunlu aria-label. */
  iconOnly?: boolean;
}

const ICON_ONLY_SIZE: Record<Size, string> = {
  sm: 'h-8 w-8 px-0',
  md: 'h-9 w-9 px-0',
  lg: 'h-11 w-11 px-0',
};

export default function Button({
  variant = 'secondary',
  size = 'md',
  iconOnly = false,
  className = '',
  type = 'button',
  ...rest
}: ButtonProps) {
  return (
    <button
      type={type}
      className={[
        'inline-flex items-center justify-center shrink-0',
        'rounded-[var(--radius-control)] font-medium',
        'transition-colors duration-[var(--dur-fast)] ease-[var(--ease-out)]',
        'cursor-pointer focus-ring',
        'disabled:opacity-50 disabled:cursor-not-allowed',
        VARIANT[variant],
        iconOnly ? `${SIZE[size].replace(/h-\S+|px-\S+|gap-\S+/g, '')} ${ICON_ONLY_SIZE[size]}` : SIZE[size],
        className,
      ].join(' ')}
      {...rest}
    />
  );
}
