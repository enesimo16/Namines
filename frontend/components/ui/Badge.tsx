import React from 'react';
import { type LucideIcon } from 'lucide-react';

export type BadgeTone = 'neutral' | 'accent' | 'success' | 'danger' | 'warning' | 'info';

const BADGE_TONE: Record<BadgeTone, string> = {
  neutral: 'bg-surface-700/80 border-line-strong text-content-secondary',
  accent: 'bg-accent-subtle/50 border-accent/30 text-accent-text',
  success: 'bg-success-subtle/50 border-success/30 text-success-text',
  danger: 'bg-danger-subtle/50 border-danger/30 text-danger-text',
  warning: 'bg-surface-600/80 border-line-strong text-content-primary',
  info: 'bg-accent-subtle/40 border-accent/30 text-accent-text',
};

export interface BadgeProps extends React.HTMLAttributes<HTMLSpanElement> {
  tone?: BadgeTone;
  icon?: LucideIcon;
  count?: number | string;
}

export function Badge({
  tone = 'neutral',
  icon: Icon,
  count,
  className = '',
  children,
  ...props
}: BadgeProps) {
  return (
    <span
      className={[
        'inline-flex items-center gap-1.5 h-6 px-2.5 rounded-full text-micro font-bold border whitespace-nowrap shadow-xs select-none',
        BADGE_TONE[tone],
        className,
      ].filter(Boolean).join(' ')}
      {...props}
    >
      {Icon && <Icon className="w-3 h-3 shrink-0" aria-hidden="true" />}
      {children}
      {count !== undefined && (
        <span className="inline-flex items-center justify-center min-w-4 h-4 px-1 rounded-full bg-surface-900/60 text-[10px] font-mono leading-none ml-0.5">
          {count}
        </span>
      )}
    </span>
  );
}

export default Badge;
