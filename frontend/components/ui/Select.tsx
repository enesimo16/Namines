import React from 'react';
import { ChevronDown } from 'lucide-react';

export interface SelectOption {
  value: string | number;
  label: string;
  disabled?: boolean;
}

export interface SelectProps extends React.SelectHTMLAttributes<HTMLSelectElement> {
  options?: readonly SelectOption[];
  error?: string;
  helperText?: string;
}

export const Select = React.forwardRef<HTMLSelectElement, SelectProps>(
  ({ options, error, helperText, className = '', disabled, id, children, ...props }, ref) => {
    const errorId = id && error ? `${id}-error` : undefined;
    const helperId = id && helperText ? `${id}-helper` : undefined;

    return (
      <div className="w-full space-y-1">
        <div className="relative w-full flex items-center">
          <select
            ref={ref}
            id={id}
            disabled={disabled}
            aria-invalid={Boolean(error)}
            aria-describedby={[errorId, helperId].filter(Boolean).join(' ') || undefined}
            className={[
              'w-full h-9 pl-3 pr-8 rounded-[var(--radius-control)] appearance-none',
              'bg-surface-700/80 border text-xs text-content-primary',
              'transition-colors duration-[var(--dur-fast)] ease-[var(--ease-out)] cursor-pointer',
              'focus:outline-none focus:ring-1',
              disabled ? 'opacity-50 cursor-not-allowed' : '',
              error
                ? 'border-danger/70 focus:border-danger focus:ring-danger/20'
                : 'border-line-strong hover:border-line-subtle focus:border-accent focus:ring-accent/20',
              className,
            ].filter(Boolean).join(' ')}
            {...props}
          >
            {options
              ? options.map(opt => (
                  <option key={opt.value} value={opt.value} disabled={opt.disabled} className="bg-surface-800 text-content-primary">
                    {opt.label}
                  </option>
                ))
              : children}
          </select>
          <ChevronDown className="w-4 h-4 text-content-muted absolute right-2.5 pointer-events-none shrink-0" aria-hidden="true" />
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

Select.displayName = 'Select';

export default Select;
