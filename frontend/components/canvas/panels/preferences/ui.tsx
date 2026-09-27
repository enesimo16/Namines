import React, { useEffect, useRef, useState } from 'react';
import { Check, ChevronDown, type LucideIcon } from 'lucide-react';

export const inputClass = "w-full px-3.5 py-2 bg-surface-600 border border-content-primary/15 rounded-[var(--radius-control)] text-xs text-content-primary placeholder-content-subtle focus:outline-none focus:border-focus-ring transition-all";
export const cardClass = "bg-surface-700 border border-content-primary/15 rounded-[var(--radius-card)]";
export const primaryBtnClass = "bg-content-primary hover:bg-content-primary-hover text-surface-900 font-semibold";

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
  openUpward = false
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
        className="w-full flex items-center justify-between bg-surface-600 border border-content-primary/15 hover:border-content-primary/25 text-content-secondary text-xs font-semibold py-2 px-3.5 rounded-[var(--radius-control)] cursor-pointer transition-all select-none focus:border-focus-ring"
      >
        <span className="truncate">{selectedOption ? selectedOption.label : value}</span>
        <ChevronDown className={`w-3.5 h-3.5 text-content-subtle transition-transform duration-200 shrink-0 ml-1.5 ${isOpen ? 'rotate-180' : ''}`} />
      </button>

      {isOpen && (
        <div
          className={`absolute left-0 w-full min-w-[200px] max-h-[240px] overflow-y-auto rounded-[var(--radius-control)] border border-content-primary/15 bg-surface-600 p-1.5 shadow-2xl z-[999] flex flex-col gap-0.5 select-none ${
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
                className={`flex items-center justify-between w-full px-3 py-2 rounded-[var(--radius-control)] text-xs font-semibold cursor-pointer transition-all text-left select-none ${
                  isSelected
                    ? 'bg-white/[0.1] text-content-primary'
                    : 'text-content-secondary hover:bg-white/[0.05] hover:text-content-primary'
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

/** Oturum gerektiren sekmelerin girişsiz hâli. */
export function NotLoggedIn({ icon: Icon, message }: { icon: LucideIcon; message: string }) {
  return (
    <div className="flex flex-col items-center justify-center text-center py-12 space-y-4">
      <div className="p-4 bg-white/[0.06] rounded-full">
        <Icon className="w-10 h-10 text-content-subtle" />
      </div>
      <div className="space-y-1">
        <h3 className="text-sm font-bold text-content-primary">Not Logged In</h3>
        <p className="text-xs text-content-muted max-w-xs">{message}</p>
      </div>
    </div>
  );
}
