'use client';

import React, { useState, useRef, useEffect, useCallback, useId } from 'react';
import { ChevronDown, Check } from 'lucide-react';

export interface SelectOption {
  value: string | number;
  label: string;
  disabled?: boolean;
}

export interface SelectProps extends Omit<React.SelectHTMLAttributes<HTMLSelectElement>, 'size'> {
  options?: readonly SelectOption[];
  error?: string;
  helperText?: string;
  placeholder?: string;
  placement?: 'bottom' | 'top' | 'auto';
}

/** React düğümünden düz metin çıkarır; `<option>{a}{b}</option>` gibi dizi çocuklar "a,b" olmasın. */
function nodeText(node: React.ReactNode): string {
  if (node === null || node === undefined || typeof node === 'boolean') return '';
  if (typeof node === 'string' || typeof node === 'number') return String(node);
  if (Array.isArray(node)) return node.map(nodeText).join('');
  if (React.isValidElement(node)) {
    return nodeText((node.props as { children?: React.ReactNode }).children);
  }
  return '';
}

/**
 * Shopify Polaris-tarzı Özel Açılır Menü (Custom Dropdown).
 *
 * Standart HTML <select> menüsü tarayıcı/işletim sistemi tarafından render edilir
 * ve CSS ile biçimlendirilemez (Windows'ta mavi Win32 listesi). Bu bileşen,
 * tam özel Polaris koyu tema, onay işareti (checkmark), klavye/tıklama yönetimi
 * ve yumuşak geçişler sunar.
 *
 * Hem `options` prop'u hem de `<option>` çocuklarını (children) destekler.
 */
export const Select = React.forwardRef<HTMLSelectElement, SelectProps>(
  (
    {
      options: optionsProp,
      error,
      helperText,
      placeholder = 'Select an option',
      placement = 'auto',
      className = '',
      disabled,
      id,
      value,
      defaultValue,
      onChange,
      children,
      name,
      ...props
    },
    ref
  ) => {
    const generatedId = useId();
    const selectId = id || generatedId;
    const errorId = error ? `${selectId}-error` : undefined;
    const helperId = helperText ? `${selectId}-helper` : undefined;

    const [isOpen, setIsOpen] = useState(false);
    const [actualPlacement, setActualPlacement] = useState<'top' | 'bottom'>('bottom');
    const containerRef = useRef<HTMLDivElement>(null);
    const hiddenSelectRef = useRef<HTMLSelectElement | null>(null);

    useEffect(() => {
      if (!isOpen) return;
      if (placement === 'top') {
        setActualPlacement('top');
        return;
      }
      if (placement === 'bottom') {
        setActualPlacement('bottom');
        return;
      }
      if (containerRef.current && typeof window !== 'undefined') {
        const rect = containerRef.current.getBoundingClientRect();
        const spaceBelow = window.innerHeight - rect.bottom;
        if (spaceBelow < 220 && rect.top > 220) {
          setActualPlacement('top');
        } else {
          setActualPlacement('bottom');
        }
      }
    }, [isOpen, placement]);

    // Parse options from props or children
    const parsedOptions: SelectOption[] = React.useMemo(() => {
      if (optionsProp && optionsProp.length > 0) {
        return [...optionsProp];
      }
      const list: SelectOption[] = [];
      React.Children.forEach(children, child => {
        if (React.isValidElement(child) && child.props) {
          const p = child.props as { value?: string | number; children?: React.ReactNode; disabled?: boolean };
          const text = nodeText(p.children);
          const val = p.value !== undefined ? p.value : text;
          const lbl = p.children !== undefined ? text : String(val);
          list.push({
            value: val,
            label: lbl,
            disabled: Boolean(p.disabled),
          });
        }
      });
      return list;
    }, [optionsProp, children]);

    const toScalar = (v: unknown): string | number =>
      Array.isArray(v) ? (v[0] ?? '') : typeof v === 'number' ? v : String(v ?? '');

    // Current internal value
    const [internalValue, setInternalValue] = useState<string | number>(
      toScalar(value !== undefined ? value : defaultValue !== undefined ? defaultValue : (parsedOptions[0]?.value ?? ''))
    );

    useEffect(() => {
      if (value !== undefined) {
        setInternalValue(toScalar(value));
      }
    }, [value]);

    // Sync forwarded ref with hidden select
    const setRefs = useCallback(
      (node: HTMLSelectElement | null) => {
        hiddenSelectRef.current = node;
        if (typeof ref === 'function') {
          ref(node);
        } else if (ref) {
          (ref as React.MutableRefObject<HTMLSelectElement | null>).current = node;
        }
      },
      [ref]
    );

    // Selected option object
    const selectedOption = parsedOptions.find(opt => String(opt.value) === String(internalValue));
    const displayText = selectedOption ? selectedOption.label : (placeholder || String(internalValue || ''));

    // Handle clicks outside
    useEffect(() => {
      if (!isOpen) return;
      const handleClickOutside = (e: MouseEvent | TouchEvent) => {
        if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
          setIsOpen(false);
        }
      };
      const handleKeyDown = (e: KeyboardEvent) => {
        if (e.key === 'Escape') {
          setIsOpen(false);
        }
      };
      document.addEventListener('mousedown', handleClickOutside);
      document.addEventListener('touchstart', handleClickOutside);
      document.addEventListener('keydown', handleKeyDown);
      return () => {
        document.removeEventListener('mousedown', handleClickOutside);
        document.removeEventListener('touchstart', handleClickOutside);
        document.removeEventListener('keydown', handleKeyDown);
      };
    }, [isOpen]);

    const handleSelectOption = (opt: SelectOption) => {
      if (opt.disabled || disabled) return;
      setInternalValue(opt.value);
      setIsOpen(false);

      if (hiddenSelectRef.current) {
        hiddenSelectRef.current.value = String(opt.value);
      }

      if (onChange) {
        const syntheticEvent = {
          target: {
            name: name || selectId,
            value: opt.value,
          },
          currentTarget: {
            name: name || selectId,
            value: opt.value,
          },
        } as unknown as React.ChangeEvent<HTMLSelectElement>;
        onChange(syntheticEvent);
      }
    };

    return (
      <div className="w-full space-y-1">
        <div ref={containerRef} className="relative w-full">
          {/* Hidden native select for form accessibility and automation */}
          <select
            ref={setRefs}
            id={selectId}
            name={name}
            disabled={disabled}
            value={internalValue}
            onChange={onChange}
            aria-hidden="true"
            tabIndex={-1}
            className="sr-only pointer-events-none opacity-0 absolute"
            {...props}
          >
            {parsedOptions.map(opt => (
              <option key={String(opt.value)} value={opt.value} disabled={opt.disabled}>
                {opt.label}
              </option>
            ))}
          </select>

          {/* Custom Trigger Button */}
          <button
            type="button"
            id={`${selectId}-button`}
            aria-haspopup="listbox"
            aria-expanded={isOpen}
            aria-invalid={Boolean(error)}
            aria-describedby={[errorId, helperId].filter(Boolean).join(' ') || undefined}
            disabled={disabled}
            onClick={() => !disabled && setIsOpen(prev => !prev)}
            className={[
              'w-full min-h-[34px] px-3 py-1.5 rounded-[var(--radius-control)]',
              'flex items-center justify-between gap-2',
              'bg-surface-800 border text-xs font-medium text-content-primary',
              'transition-all duration-[var(--dur-fast)] ease-[var(--ease-out)] cursor-pointer text-left',
              'focus:outline-none focus:ring-1 focus:ring-accent-text/30',
              disabled ? 'opacity-50 cursor-not-allowed' : 'hover:border-line-subtle',
              error
                ? 'border-danger/70 focus:border-danger'
                : isOpen
                ? 'border-accent-text ring-1 ring-accent-text/30 shadow-md'
                : 'border-line-strong shadow-xs',
              className,
            ]
              .filter(Boolean)
              .join(' ')}
          >
            <span className="truncate flex-1 font-medium select-none">{displayText}</span>
            <ChevronDown
              className={`w-3.5 h-3.5 text-content-muted shrink-0 transition-transform duration-150 ${
                isOpen ? 'rotate-180 text-content-primary' : ''
              }`}
              aria-hidden="true"
            />
          </button>

          {/* Custom Polaris Popover Menu */}
          {isOpen && (
            <div
              role="listbox"
              aria-labelledby={`${selectId}-button`}
              className={`absolute left-0 min-w-full w-max max-w-[min(100vw-2rem,360px)] ${
                actualPlacement === 'top' ? 'bottom-full mb-1.5' : 'top-full mt-1.5'
              } z-[250] max-h-60 overflow-y-auto rounded-[var(--radius-card)] border border-line-strong bg-surface-800/98 backdrop-blur-xl shadow-2xl p-1 animate-in fade-in zoom-in-95 duration-100`}
            >
              {parsedOptions.length === 0 ? (
                <div className="py-2.5 px-3 text-center text-xs text-content-muted">No options available</div>
              ) : (
                parsedOptions.map(opt => {
                  const isSelected = String(opt.value) === String(internalValue);
                  return (
                    <button
                      key={String(opt.value)}
                      type="button"
                      role="option"
                      aria-selected={isSelected}
                      disabled={opt.disabled}
                      onClick={() => handleSelectOption(opt)}
                      className={[
                        'w-full flex items-center justify-between gap-2 px-2.5 py-1.5 rounded-[var(--radius-control)]',
                        'text-xs font-medium transition-colors text-left select-none cursor-pointer',
                        opt.disabled
                          ? 'opacity-40 cursor-not-allowed text-content-muted'
                          : isSelected
                          ? 'bg-surface-700 text-content-primary font-semibold'
                          : 'text-content-secondary hover:text-content-primary hover:bg-surface-700/60',
                      ]
                        .filter(Boolean)
                        .join(' ')}
                    >
                      <span className="truncate flex-1">{opt.label}</span>
                      {isSelected && (
                        <Check className="w-3.5 h-3.5 text-accent-text shrink-0" aria-hidden="true" />
                      )}
                    </button>
                  );
                })
              )}
            </div>
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

Select.displayName = 'Select';

export default Select;
