'use client';

import React, { useEffect, useRef } from 'react';
import { X } from 'lucide-react';
import IconButton from './IconButton';

export type ModalSize = 'sm' | 'md' | 'lg' | 'xl';

const MODAL_SIZE: Record<ModalSize, string> = {
  sm: 'max-w-sm',
  md: 'max-w-md',
  lg: 'max-w-xl',
  xl: 'max-w-3xl',
};

export interface ModalProps {
  isOpen: boolean;
  onClose: () => void;
  title?: React.ReactNode;
  description?: React.ReactNode;
  children: React.ReactNode;
  size?: ModalSize;
  className?: string;
}

export function Modal({
  isOpen,
  onClose,
  title,
  description,
  children,
  size = 'md',
  className = '',
}: ModalProps) {
  const modalRef = useRef<HTMLDivElement>(null);

  // Esc tuşu ile kapatma
  useEffect(() => {
    if (!isOpen) return;
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [isOpen, onClose]);

  // Arka plan kaydırmasını engelleme
  useEffect(() => {
    if (isOpen) {
      document.body.style.overflow = 'hidden';
    } else {
      document.body.style.overflow = '';
    }
    return () => {
      document.body.style.overflow = '';
    };
  }, [isOpen]);

  if (!isOpen) return null;

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby={title ? 'modal-title' : undefined}
      aria-describedby={description ? 'modal-description' : undefined}
      className="fixed inset-0 z-modal-backdrop flex items-center justify-center p-4 bg-scrim/70 backdrop-blur-sm animate-in fade-in duration-[var(--dur-fast)]"
      onClick={e => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div
        ref={modalRef}
        className={[
          'relative w-full z-modal bg-surface-800 border border-line-strong',
          'rounded-[var(--radius-modal)] shadow-2xl overflow-hidden',
          'animate-in zoom-in-95 duration-[var(--dur-base)] ease-[var(--ease-out)]',
          MODAL_SIZE[size],
          className,
        ].filter(Boolean).join(' ')}
      >
        {(title || description) && (
          <div className="flex items-start justify-between gap-4 p-5 border-b border-line">
            <div className="min-w-0">
              {title && (
                <h3 id="modal-title" className="text-base font-bold text-content-primary leading-tight">
                  {title}
                </h3>
              )}
              {description && (
                <p id="modal-description" className="mt-1 text-xs text-content-muted leading-relaxed">
                  {description}
                </p>
              )}
            </div>
            <IconButton icon={X} aria-label="Kapat" onClick={onClose} size="sm" variant="ghost" />
          </div>
        )}
        <div className="p-5">{children}</div>
      </div>
    </div>
  );
}

export default Modal;
