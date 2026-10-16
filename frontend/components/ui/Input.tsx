import React from 'react';
import { type LucideIcon } from 'lucide-react';

export interface InputProps extends React.InputHTMLAttributes<HTMLInputElement> {
  leftIcon?: LucideIcon;
  rightIcon?: LucideIcon;
  error?: string;
  helperText?: string;
}

export const Input = React.forwardRef<HTMLInputElement, InputProps>(
  ({ leftIcon: LeftIcon, rightIcon: RightIcon, error, helperText, className = '', disabled, id, ...props }, ref) => {
    const errorId = id && error ? `${id}-error` : undefined;
    const helperId = id && helperText ? `${id}-helper` : undefined;

    return (
      <div className="w-full space-y-1">
        <div className="relative w-full flex items-center">
          {LeftIcon && (
            <span className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none text-content-muted">
              <LeftIcon className="w-4 h-4" aria-hidden="true" />
            </span>
          )}
          <input
            ref={ref}
            id={id}
            disabled={disabled}
            aria-invalid={Boolean(error)}
            aria-describedby={[errorId, helperId].filter(Boolean).join(' ') || undefined}
            className={[
              'w-full h-9 rounded-[var(--radius-control)]',
              'bg-surface-700/80 border text-xs text-content-primary',
              'placeholder:text-content-muted',
              'transition-colors duration-[var(--dur-fast)] ease-[var(--ease-out)]',
              'focus:outline-none focus:ring-1',
              disabled ? 'opacity-50 cursor-not-allowed' : '',
              LeftIcon ? 'pl-9' : 'px-3',
              RightIcon ? 'pr-9' : 'px-3',
              error
                ? 'border-danger/70 focus:border-danger focus:ring-danger/20'
                : 'border-line-strong hover:border-line-subtle focus:border-accent focus:ring-accent/20',
              className,
            ].filter(Boolean).join(' ')}
            {...props}
          />
          {RightIcon && (
            <span className="absolute inset-y-0 right-0 pr-3 flex items-center pointer-events-none text-content-muted">
              <RightIcon className="w-4 h-4" aria-hidden="true" />
            </span>
          )}
        </div>
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

Input.displayName = 'Input';

export default Input;
