import React from 'react';
import { Info, CheckCircle2, AlertTriangle, XCircle, type LucideIcon } from 'lucide-react';

export type AlertTone = 'info' | 'success' | 'danger' | 'warning' | 'neutral';

const ALERT_CONFIG: Record<
  AlertTone,
  { box: string; iconClass: string; defaultIcon: LucideIcon; role: 'status' | 'alert' }
> = {
  info: { box: 'border-accent/30 bg-accent-subtle/40', iconClass: 'text-accent-text', defaultIcon: Info, role: 'status' },
  success: { box: 'border-success/30 bg-success-subtle/40', iconClass: 'text-success-text', defaultIcon: CheckCircle2, role: 'status' },
  danger: { box: 'border-danger/30 bg-danger-subtle/40', iconClass: 'text-danger-text', defaultIcon: XCircle, role: 'alert' },
  warning: { box: 'border-line-strong bg-surface-700/60', iconClass: 'text-content-primary', defaultIcon: AlertTriangle, role: 'status' },
  neutral: { box: 'border-line-strong bg-surface-700/60', iconClass: 'text-content-muted', defaultIcon: Info, role: 'status' },
};

export interface AlertProps {
  tone?: AlertTone;
  title?: string;
  children?: React.ReactNode;
  action?: React.ReactNode;
  icon?: LucideIcon;
  className?: string;
}

export function Alert({
  tone = 'info',
  title,
  children,
  action,
  icon,
  className = '',
}: AlertProps) {
  const cfg = ALERT_CONFIG[tone];
  const Icon = icon ?? cfg.defaultIcon;

  return (
    <div
      role={cfg.role}
      className={`flex items-start gap-3 rounded-[var(--radius-card)] border px-4 py-3 shadow-xs ${cfg.box} ${className}`}
    >
      <Icon className={`w-4 h-4 mt-0.5 shrink-0 ${cfg.iconClass}`} aria-hidden="true" />
      <div className="min-w-0 flex-1 text-xs leading-relaxed">
        {title && <p className="font-bold text-content-primary">{title}</p>}
        {children && <div className="text-content-secondary mt-0.5">{children}</div>}
      </div>
      {action && <div className="shrink-0">{action}</div>}
    </div>
  );
}

/** Alias for Alert */
export const Callout = Alert;

export default Alert;
