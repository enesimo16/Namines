import React, { useEffect, useRef, useState } from 'react';
import { Check, ChevronDown, type LucideIcon } from 'lucide-react';

export const inputClass =
  'w-full px-3.5 py-2 bg-surface-900/90 border border-line-strong rounded-[var(--radius-control)] text-xs text-content-primary placeholder:text-content-muted focus:outline-none focus:border-accent focus:ring-1 focus:ring-accent/20 transition-all font-sans';

export const cardClass =
  'bg-surface-800/80 border border-line-strong rounded-[var(--radius-card)] shadow-xs backdrop-blur-sm';

export const primaryBtnClass =
  'bg-accent hover:bg-accent-hover text-accent-on font-semibold rounded-[var(--radius-control)] shadow-sm';

interface CustomSelectOption<T> {
  value: T;
  label: string;
}

interface CustomSelectProps<T> {
  value: T;
  onChange: (value: T) => void;
  options: readonly CustomSelectOption<T>[];
  className?: string;
  openUpward?: boolean;
}

export function CustomSelect<T extends string | number>({
  value,
  onChange,
  options,
  className = '',
  openUpward = false,
}: CustomSelectProps<T>) {
  const [isOpen, setIsOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    function handleClickOutside(event: MouseEvent) {
      if (containerRef.current && !containerRef.current.contains(event.target as Node)) {
        setIsOpen(false);
      }
    }
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  const selectedOption = options.find(opt => opt.value === value);

  return (
    <div className={`relative ${className}`} ref={containerRef}>
      <button
        type="button"
        onClick={() => setIsOpen(!isOpen)}
        className="w-full flex items-center justify-between bg-surface-900/90 border border-line-strong hover:border-line-subtle text-content-primary text-xs font-medium py-2 px-3.5 rounded-[var(--radius-control)] cursor-pointer transition-all select-none focus:border-accent focus:ring-1 focus:ring-accent/20"
      >
        <span className="truncate">{selectedOption ? selectedOption.label : value}</span>
        <ChevronDown className={`w-3.5 h-3.5 text-content-muted transition-transform duration-200 shrink-0 ml-1.5 ${isOpen ? 'rotate-180' : ''}`} />
      </button>

      {isOpen && (
        <div
          className={`absolute left-0 w-full min-w-[200px] max-h-[240px] overflow-y-auto rounded-[var(--radius-card)] border border-line-strong bg-surface-900/98 backdrop-blur-2xl p-1 shadow-2xl z-dropdown flex flex-col gap-0.5 select-none ${
            openUpward ? 'bottom-full mb-2' : 'top-full mt-2'
          }`}
        >
          {options.map((opt) => {
            const isSelected = opt.value === value;
            return (
              <button
                key={opt.value}
                type="button"
                onClick={() => {
                  onChange(opt.value);
                  setIsOpen(false);
                }}
                className={`flex items-center justify-between w-full px-3 py-2 rounded-[var(--radius-control)] text-xs font-medium cursor-pointer transition-all text-left select-none ${
                  isSelected
                    ? 'bg-surface-800 text-content-primary font-semibold'
                    : 'text-content-secondary hover:bg-surface-800 hover:text-content-primary'
                }`}
              >
                <span className="truncate">{opt.label}</span>
                {isSelected && (
                  <Check className="w-3.5 h-3.5 text-content-primary shrink-0 ml-1.5" />
                )}
              </button>
            );
          })}
        </div>
      )}
    </div>
  );
}

/** Oturum gerektiren sekmelerin girişsiz hâli — Shopify Polaris Empty State. */
export function NotLoggedIn({ icon: Icon, message }: { icon: LucideIcon; message: string }) {
  return (
    <div className="flex flex-col items-center justify-center text-center py-12 space-y-4">
      <div className="p-4 bg-surface-800 border border-line-strong rounded-full">
        <Icon className="w-8 h-8 text-content-muted" />
      </div>
      <div className="space-y-1">
        <h3 className="text-sm font-semibold text-content-primary">Not Logged In</h3>
        <p className="text-xs text-content-muted max-w-xs">{message}</p>
      </div>
    </div>
  );
}
