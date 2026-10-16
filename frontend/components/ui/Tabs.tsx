'use client';

import React from 'react';
import { type LucideIcon } from 'lucide-react';

export interface TabItem<T extends string = string> {
  value: T;
  label: string;
  count?: number;
  icon?: LucideIcon;
  disabled?: boolean;
}

export interface TabsProps<T extends string = string> {
  value: T;
  onChange: (value: T) => void;
  tabs: readonly TabItem<T>[];
  ariaLabel: string;
  className?: string;
}

export function Tabs<T extends string = string>({
  value,
  onChange,
  tabs,
  ariaLabel,
  className = '',
}: TabsProps<T>) {
  const handleKeyDown = (event: React.KeyboardEvent, index: number) => {
    let next = index;
    if (event.key === 'ArrowRight') next = (index + 1) % tabs.length;
    else if (event.key === 'ArrowLeft') next = (index - 1 + tabs.length) % tabs.length;
    else if (event.key === 'Home') next = 0;
    else if (event.key === 'End') next = tabs.length - 1;
    else return;

    event.preventDefault();
    onChange(tabs[next].value);
    document.getElementById(`tab-${tabs[next].value}`)?.focus();
  };

  return (
    <div
      role="tablist"
      aria-label={ariaLabel}
      className={`flex items-center gap-1 border-b border-line overflow-x-auto ${className}`}
    >
      {tabs.map((tab, index) => {
        const active = tab.value === value;
        const Icon = tab.icon;

        return (
          <button
            key={tab.value}
            id={`tab-${tab.value}`}
            role="tab"
            type="button"
            aria-selected={active}
            aria-controls={`tabpanel-${tab.value}`}
            tabIndex={active ? 0 : -1}
            disabled={tab.disabled}
            onClick={() => onChange(tab.value)}
            onKeyDown={e => handleKeyDown(e, index)}
            className={[
              'relative shrink-0 inline-flex items-center gap-2 h-10 px-4 text-xs font-semibold whitespace-nowrap transition-colors cursor-pointer focus-ring',
              'disabled:opacity-50 disabled:cursor-not-allowed',
              active ? 'text-content-primary font-bold' : 'text-content-muted hover:text-content-secondary font-medium',
            ].filter(Boolean).join(' ')}
          >
            {Icon && <Icon className="w-4 h-4" aria-hidden="true" />}
            {tab.label}
            {tab.count !== undefined && (
              <span className="inline-flex items-center justify-center min-w-5 h-5 px-1.5 rounded-full bg-surface-600 text-micro text-content-secondary font-mono">
                {tab.count}
              </span>
            )}
            {active && (
              <span className="absolute left-3 right-3 -bottom-px h-0.5 rounded-full bg-content-primary" />
            )}
          </button>
        );
      })}
    </div>
  );
}

export default Tabs;
