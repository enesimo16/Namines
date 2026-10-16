import React from 'react';

export interface SwitchProps {
  checked: boolean;
  onChange: (checked: boolean) => void;
  label?: string;
  description?: string;
  disabled?: boolean;
  className?: string;
  id?: string;
}

export function Switch({
  checked,
  onChange,
  label,
  description,
  disabled = false,
  className = '',
  id,
}: SwitchProps) {
  const switchId = id || (label ? `switch-${label.toLowerCase().replace(/\s+/g, '-')}` : undefined);

  return (
    <div className={`inline-flex items-center justify-between gap-3 ${disabled ? 'opacity-50' : ''} ${className}`}>
      {(label || description) && (
        <div className="flex flex-col text-xs leading-tight select-none">
          {label && (
            <label htmlFor={switchId} className="font-medium text-content-primary cursor-pointer">
              {label}
            </label>
          )}
          {description && <span className="text-micro text-content-muted mt-0.5">{description}</span>}
        </div>
      )}
      <button
        type="button"
        role="switch"
        id={switchId}
        aria-checked={checked}
        aria-label={label}
        disabled={disabled}
        onClick={() => onChange(!checked)}
        className={[
          'relative shrink-0 w-10 h-6 rounded-full border transition-colors cursor-pointer focus-ring',
          'disabled:cursor-not-allowed',
          checked
            ? 'bg-content-primary border-transparent'
            : 'bg-surface-600 border-line-strong',
        ].filter(Boolean).join(' ')}
      >
        <span
          className={[
            'absolute top-0.5 left-0.5 w-4.5 h-4.5 rounded-full shadow-xs transition-transform duration-[var(--dur-fast)] ease-[var(--ease-out)]',
            checked ? 'translate-x-4 bg-surface-900' : 'bg-content-primary',
          ].join(' ')}
        />
      </button>
    </div>
  );
}

export default Switch;
