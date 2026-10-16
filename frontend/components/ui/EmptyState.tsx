import React from 'react';
import { type LucideIcon } from 'lucide-react';

export interface EmptyStateProps {
  icon: LucideIcon;
  title: string;
  description?: string;
  action?: React.ReactNode;
  className?: string;
}

export function EmptyState({
  icon: Icon,
  title,
  description,
  action,
  className = '',
}: EmptyStateProps) {
  return (
    <div className={`flex flex-col items-center justify-center gap-3 text-center px-6 py-12 ${className}`}>
      <span className="flex items-center justify-center w-12 h-12 rounded-full bg-surface-700 border border-line-strong text-content-muted shadow-xs">
        <Icon className="w-5 h-5" aria-hidden="true" />
      </span>
      <div className="space-y-1">
        <p className="text-sm font-bold text-content-primary">{title}</p>
        {description && (
          <p className="text-xs leading-relaxed text-content-muted max-w-sm">{description}</p>
        )}
      </div>
      {action && <div className="mt-2">{action}</div>}
    </div>
  );
}

export default EmptyState;
