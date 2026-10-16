import React from 'react';

export type CardTone = 'default' | 'danger';

export interface CardProps extends React.HTMLAttributes<HTMLDivElement> {
  tone?: CardTone;
  flush?: boolean;
}

export function Card({
  tone = 'default',
  flush = false,
  className = '',
  children,
  ...props
}: CardProps) {
  return (
    <div
      className={[
        'rounded-[var(--radius-card)] border overflow-hidden shadow-xs',
        tone === 'danger'
          ? 'border-danger/30 bg-danger-subtle/20'
          : 'border-line-strong bg-surface-800',
        flush ? '' : '',
        className,
      ].filter(Boolean).join(' ')}
      {...props}
    >
      {children}
    </div>
  );
}

export interface CardHeaderProps {
  title?: React.ReactNode;
  description?: React.ReactNode;
  actions?: React.ReactNode;
  className?: string;
  children?: React.ReactNode;
}

export function CardHeader({
  title,
  description,
  actions,
  className = '',
  children,
}: CardHeaderProps) {
  return (
    <div className={`flex flex-wrap items-start justify-between gap-3 px-5 py-3.5 border-b border-line bg-surface-800/60 ${className}`}>
      <div className="min-w-0">
        {title && (typeof title === 'string' ? <h2 className="text-xs font-bold uppercase tracking-wider text-content-primary">{title}</h2> : title)}
        {description && (typeof description === 'string' ? <p className="mt-0.5 text-micro leading-relaxed text-content-muted max-w-xl">{description}</p> : description)}
        {children}
      </div>
      {actions && <div className="flex items-center gap-2 shrink-0">{actions}</div>}
    </div>
  );
}

export interface CardBodyProps extends React.HTMLAttributes<HTMLDivElement> {
  flush?: boolean;
}

export function CardBody({
  flush = false,
  className = '',
  children,
  ...props
}: CardBodyProps) {
  return (
    <div className={`${flush ? '' : 'px-5 py-4'} ${className}`} {...props}>
      {children}
    </div>
  );
}

/** Alias for CardBody */
export const CardContent = CardBody;

export interface CardFooterProps {
  actions?: React.ReactNode;
  className?: string;
  children?: React.ReactNode;
}

export function CardFooter({
  actions,
  className = '',
  children,
}: CardFooterProps) {
  return (
    <div className={`flex flex-wrap items-center justify-between gap-3 px-5 py-3 border-t border-line bg-surface-800/40 text-xs text-content-muted ${className}`}>
      <div className="min-w-0">{children}</div>
      {actions && <div className="flex items-center gap-2 shrink-0">{actions}</div>}
    </div>
  );
}

export default Card;
