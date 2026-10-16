'use client';

import React, { useEffect } from 'react';
import { X } from 'lucide-react';
import IconButton from './IconButton';

export type DrawerSide = 'left' | 'right';

export interface DrawerProps {
  isOpen: boolean;
  onClose: () => void;
  title?: React.ReactNode;
  description?: React.ReactNode;
  children: React.ReactNode;
  side?: DrawerSide;
  className?: string;
  width?: string;
}

export function Drawer({
  isOpen,
  onClose,
  title,
  description,
  children,
  side = 'right',
  className = '',
  width = 'w-[400px] max-w-[92vw]',
}: DrawerProps) {
  useEffect(() => {
    if (!isOpen) return;
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [isOpen, onClose]);

  if (!isOpen) return null;

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby={title ? 'drawer-title' : undefined}
      className="fixed inset-0 z-drawer-backdrop flex bg-scrim/60 backdrop-blur-sm animate-in fade-in duration-[var(--dur-fast)]"
      onClick={e => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div
        className={[
          'fixed top-0 bottom-0 z-drawer bg-surface-800 border-line shadow-2xl flex flex-col',
          'animate-in duration-[var(--dur-base)] ease-[var(--ease-out)]',
          side === 'right' ? 'right-0 border-l slide-in-from-right' : 'left-0 border-r slide-in-from-left',
          width,
          className,
        ].filter(Boolean).join(' ')}
      >
        <div className="flex items-start justify-between gap-4 p-5 border-b border-line shrink-0">
          <div className="min-w-0">
            {title && (
              <h2 id="drawer-title" className="text-sm font-bold text-content-primary leading-tight">
                {title}
              </h2>
            )}
            {description && (
              <p className="mt-1 text-micro text-content-muted leading-relaxed">
                {description}
              </p>
            )}
          </div>
          <IconButton icon={X} aria-label="Kapat" onClick={onClose} size="sm" variant="ghost" />
        </div>
        <div className="flex-1 overflow-y-auto p-5">{children}</div>
      </div>
    </div>
  );
}

export default Drawer;
