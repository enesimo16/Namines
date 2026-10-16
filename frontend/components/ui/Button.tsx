import React from 'react';
import { Loader2, type LucideIcon } from 'lucide-react';

/**
 * Tek buton primitifi.
 *
 * Geometri sabit: yalnızca yükseklik ve dolgu ölçeği değişir.
 * Varyasyon yalnızca zemin, kenarlık ve semantik durumda.
 * `focus-ring` varsayılan erişilebilirlik halkasını sağlar.
 */

export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger';
export type ButtonSize = 'sm' | 'md' | 'lg';

const VARIANT: Record<ButtonVariant, string> = {
  primary:
    'bg-content-primary text-surface-900 hover:bg-content-primary-hover border border-transparent shadow-xs',
  secondary:
    'bg-surface-700 text-content-secondary hover:text-content-primary hover:bg-surface-600 border border-[var(--color-border-hairline)] hover:border-[var(--color-border-strong)] shadow-xs',
  ghost:
    'bg-transparent text-content-muted hover:text-content-primary hover:bg-surface-700 border border-transparent',
  danger:
    'bg-danger text-content-primary hover:bg-danger/90 border border-transparent shadow-xs',
};

const SIZE: Record<ButtonSize, string> = {
  sm: 'h-8 px-2.5 gap-1.5 text-xs',
  md: 'h-9 px-3.5 gap-2 text-xs',
  lg: 'h-11 px-4 gap-2 text-sm',
};

const ICON_ONLY_SIZE: Record<ButtonSize, string> = {
  sm: 'h-8 w-8 !px-0',
  md: 'h-9 w-9 !px-0',
  lg: 'h-11 w-11 !px-0',
};

export interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  size?: ButtonSize;
  icon?: LucideIcon;
  iconOnly?: boolean;
  busy?: boolean;
  fullWidth?: boolean;
}

export function Button({
  variant = 'secondary',
  size = 'md',
  icon: Icon,
  iconOnly = false,
  busy = false,
  fullWidth = false,
  className = '',
  type = 'button',
  disabled,
  children,
  ...rest
}: ButtonProps) {
  const isDisabled = disabled || busy;

  return (
    <button
      type={type}
      disabled={isDisabled}
      aria-busy={busy}
      className={[
        'inline-flex items-center justify-center shrink-0 font-medium',
        'rounded-[var(--radius-control)]',
        'transition-colors duration-[var(--dur-fast)] ease-[var(--ease-out)]',
        'cursor-pointer focus-ring select-none',
        'disabled:opacity-50 disabled:cursor-not-allowed',
        fullWidth ? 'w-full' : '',
        VARIANT[variant],
        iconOnly ? `${SIZE[size].replace(/h-\S+|px-\S+|gap-\S+/g, '')} ${ICON_ONLY_SIZE[size]}` : SIZE[size],
        className,
      ].filter(Boolean).join(' ')}
      {...rest}
    >
      {busy ? (
        <Loader2 className="w-3.5 h-3.5 animate-spin shrink-0" aria-hidden="true" />
      ) : Icon ? (
        <Icon className="w-3.5 h-3.5 shrink-0" aria-hidden="true" />
      ) : null}
      {!iconOnly && children}
    </button>
  );
}

export default Button;
