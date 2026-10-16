import React from 'react';
import { Check } from 'lucide-react';

export interface CheckboxProps extends Omit<React.InputHTMLAttributes<HTMLInputElement>, 'type'> {
  label?: React.ReactNode;
  description?: React.ReactNode;
}

export const Checkbox = React.forwardRef<HTMLInputElement, CheckboxProps>(
  ({ label, description, className = '', disabled, id, checked, defaultChecked, onChange, ...props }, ref) => {
    const inputId = id || (typeof label === 'string' ? `checkbox-${label.toLowerCase().replace(/\s+/g, '-')}` : undefined);

    return (
      <label
        htmlFor={inputId}
        className={[
          'inline-flex items-start gap-2.5 cursor-pointer select-none',
          disabled ? 'opacity-50 cursor-not-allowed' : '',
          className,
        ].filter(Boolean).join(' ')}
      >
        <div className="relative flex items-center justify-center shrink-0 mt-0.5">
          <input
            type="checkbox"
            ref={ref}
            id={inputId}
            disabled={disabled}
            checked={checked}
            defaultChecked={defaultChecked}
            onChange={onChange}
            className="peer sr-only"
            {...props}
          />
          <div className="w-4 h-4 rounded-[var(--radius-sm)] border border-line-strong bg-surface-700/80 transition-colors peer-checked:bg-content-primary peer-checked:border-content-primary peer-focus-visible:ring-2 peer-focus-visible:ring-accent flex items-center justify-center">
            <Check className="w-3 h-3 text-surface-900 opacity-0 peer-checked:opacity-100 transition-opacity stroke-[3]" aria-hidden="true" />
          </div>
        </div>
        {(label || description) && (
          <div className="flex flex-col text-xs leading-tight">
            {label && <span className="font-medium text-content-primary">{label}</span>}
            {description && <span className="text-micro text-content-muted mt-0.5">{description}</span>}
          </div>
        )}
      </label>
    );
  }
);

Checkbox.displayName = 'Checkbox';

export default Checkbox;
