'use client';

import React, { useEffect } from 'react';
import { AlertTriangle, HelpCircle } from 'lucide-react';
import { useConfirmStore } from '../../store/useConfirmStore';

// Uygulama temasıyla uyumlu, native confirm() yerine geçen estetik onay modalı.
export default function ConfirmDialog() {
  const { isOpen, options, respond } = useConfirmStore();

  // Esc = iptal, Enter = onayla. Odak bir düğmedeyse Enter'ı tarayıcının yerel
  // "tıkla" davranışına bırak: yoksa "İptal" odaktayken Enter yıkıcı işlemi onaylıyordu.
  useEffect(() => {
    if (!isOpen) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') respond(false);
      if (e.key === 'Enter') {
        const target = e.target as HTMLElement | null;
        if (target && (target.tagName === 'BUTTON' || target.tagName === 'A')) return;
        respond(true);
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [isOpen, respond]);

  if (!isOpen || !options) return null;

  const danger = options.danger ?? false;

  return (
    <div className="fixed inset-0 z-max flex items-center justify-center p-4 bg-scrim/70 backdrop-blur-sm animate-fade-in">
      <div
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="confirm-title"
        className="relative w-full max-w-sm bg-surface-800/95 backdrop-blur-2xl border border-line-strong rounded-[var(--radius-modal)] shadow-2xl overflow-hidden animate-in zoom-in-95 duration-150"
      >
        <div className="p-6 flex flex-col items-center text-center gap-3">
          <div
            className={`w-12 h-12 rounded-full flex items-center justify-center ${
              danger ? 'bg-danger/15 text-danger-text' : 'bg-accent/15 text-accent-text'
            }`}
          >
            {danger ? <AlertTriangle className="w-6 h-6" /> : <HelpCircle className="w-6 h-6" />}
          </div>
          <h3 id="confirm-title" className="text-lg font-bold text-content-primary">
            {options.title}
          </h3>
          <p className="text-sm text-content-muted leading-relaxed">{options.message}</p>
        </div>

        <div className="flex gap-2.5 px-6 pb-6">
          <button
            onClick={() => respond(false)}
            className="flex-1 py-2.5 rounded-[var(--radius-control)] text-sm font-semibold text-content-secondary bg-surface-700/60 border border-line-strong hover:bg-surface-700 transition-all cursor-pointer focus-ring"
          >
            {options.cancelLabel ?? 'Cancel'}
          </button>
          <button
            autoFocus
            onClick={() => respond(true)}
            className={`flex-1 py-2.5 rounded-[var(--radius-control)] text-sm font-bold text-accent-on transition-all cursor-pointer active:scale-[0.98] focus-ring ${
              danger
                ? 'bg-danger hover:bg-danger shadow-sm'
                : 'bg-accent hover:bg-accent-hover shadow-sm'
            }`}
          >
            {options.confirmLabel ?? 'Confirm'}
          </button>
        </div>
      </div>
    </div>
  );
}
