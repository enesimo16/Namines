import React from 'react';
import { type LucideIcon } from 'lucide-react';
import { type ButtonVariant, type ButtonSize } from './Button';

export interface IconButtonProps extends Omit<React.ButtonHTMLAttributes<HTMLButtonElement>, 'children'> {
  icon: LucideIcon;
  'aria-label': string; // Erişilebilirlik için zorunlu
  variant?: ButtonVariant;
  size?: ButtonSize;
}

const VARIANT: Record<ButtonVariant, string> = {
  primary: 'bg-content-primary text-surface-900 hover:bg-content-primary-hover border border-transparent shadow-xs',
  secondary: 'bg-surface-700 text-content-secondary hover:text-content-primary hover:bg-surface-600 border border-[var(--color-border-hairline)] hover:border-[var(--color-border-strong)] shadow-xs',
  ghost: 'bg-transparent text-content-muted hover:text-content-primary hover:bg-surface-700 border border-transparent',
  danger: 'bg-danger text-content-primary hover:bg-danger/90 border border-transparent shadow-xs',
};

const SIZE: Record<ButtonSize, { box: string; icon: string }> = {
  sm: { box: 'w-8 h-8', icon: 'w-3.5 h-3.5' },
  md: { box: 'w-9 h-9', icon: 'w-4 h-4' },
  lg: { box: 'w-11 h-11', icon: 'w-5 h-5' },
};

export function IconButton({
  icon: Icon,
  variant = 'ghost',
  size = 'md',
  className = '',
  type = 'button',
  disabled,
  ...rest
}: IconButtonProps) {
  const { box, icon: iconSize } = SIZE[size];

  return (
    <button
      type={type}
      disabled={disabled}
      className={[
        'inline-flex items-center justify-center shrink-0 tap-44',
        'rounded-[var(--radius-control)]',
        'transition-colors duration-[var(--dur-fast)] ease-[var(--ease-out)]',
        'cursor-pointer focus-ring select-none',
        'disabled:opacity-50 disabled:cursor-not-allowed',
        box,
        VARIANT[variant],
        className,
      ].filter(Boolean).join(' ')}
      {...rest}
    >
      <Icon className={`${iconSize} shrink-0`} aria-hidden="true" />
    </button>
  );
}

export default IconButton;
