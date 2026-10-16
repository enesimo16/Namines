import React from 'react';

export interface RadioProps extends Omit<React.InputHTMLAttributes<HTMLInputElement>, 'type'> {
  label?: React.ReactNode;
  description?: React.ReactNode;
}

export const Radio = React.forwardRef<HTMLInputElement, RadioProps>(
  ({ label, description, className = '', disabled, id, checked, defaultChecked, onChange, ...props }, ref) => {
    const inputId = id || (typeof label === 'string' ? `radio-${label.toLowerCase().replace(/\s+/g, '-')}` : undefined);

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
            type="radio"
            ref={ref}
            id={inputId}
            disabled={disabled}
            checked={checked}
            defaultChecked={defaultChecked}
            onChange={onChange}
            className="peer sr-only"
            {...props}
          />
          <div className="w-4 h-4 rounded-full border border-line-strong bg-surface-700/80 transition-colors peer-checked:border-content-primary peer-focus-visible:ring-2 peer-focus-visible:ring-accent flex items-center justify-center">
            <div className="w-2 h-2 rounded-full bg-content-primary opacity-0 peer-checked:opacity-100 transition-opacity" />
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

Radio.displayName = 'Radio';

export default Radio;
