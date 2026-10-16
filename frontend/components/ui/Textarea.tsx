import React from 'react';

export interface TextareaProps extends React.TextareaHTMLAttributes<HTMLTextAreaElement> {
  error?: string;
  helperText?: string;
}

export const Textarea = React.forwardRef<HTMLTextAreaElement, TextareaProps>(
  ({ error, helperText, className = '', disabled, id, ...props }, ref) => {
    const errorId = id && error ? `${id}-error` : undefined;
    const helperId = id && helperText ? `${id}-helper` : undefined;

    return (
      <div className="w-full space-y-1">
        <textarea
          ref={ref}
          id={id}
          disabled={disabled}
          aria-invalid={Boolean(error)}
          aria-describedby={[errorId, helperId].filter(Boolean).join(' ') || undefined}
          className={[
            'w-full min-h-[80px] p-3 rounded-[var(--radius-control)]',
            'bg-surface-700/80 border text-xs text-content-primary',
            'placeholder:text-content-muted leading-relaxed',
            'transition-colors duration-[var(--dur-fast)] ease-[var(--ease-out)]',
            'focus:outline-none focus:ring-1',
            disabled ? 'opacity-50 cursor-not-allowed' : '',
            error
              ? 'border-danger/70 focus:border-danger focus:ring-danger/20'
              : 'border-line-strong hover:border-line-subtle focus:border-accent focus:ring-accent/20',
            className,
          ].filter(Boolean).join(' ')}
          {...props}
        />
        {error ? (
          <p id={errorId} role="alert" className="text-micro text-danger-text">
            {error}
          </p>
        ) : helperText ? (
          <p id={helperId} className="text-micro text-content-muted">
            {helperText}
          </p>
        ) : null}
      </div>
    );
  }
);

Textarea.displayName = 'Textarea';

export default Textarea;
