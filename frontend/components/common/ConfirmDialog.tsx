'use client';

import React, { useEffect } from 'react';
import { AlertTriangle, HelpCircle } from 'lucide-react';
import { useConfirmStore } from '../../store/useConfirmStore';

// Uygulama temasıyla uyumlu, native confirm() yerine geçen estetik onay modalı.
export default function ConfirmDialog() {
  const { isOpen, options, respond } = useConfirmStore();

  // Esc = iptal, Enter = onayla
  useEffect(() => {
    if (!isOpen) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') respond(false);
      if (e.key === 'Enter') respond(true);
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [isOpen, respond]);

  if (!isOpen || !options) return null;

  const danger = options.danger ?? false;
  const accent = danger ? 'rose' : 'indigo';

  return (
    <div className="fixed inset-0 z-[10000] flex items-center justify-center p-4 bg-scrim/70 backdrop-blur-sm animate-fade-in">
      <div
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="confirm-title"
        className="relative w-full max-w-sm bg-surface-800/95 backdrop-blur-2xl border border-white/10 rounded-2xl shadow-[0_20px_60px_rgba(0,0,0,0.8)] overflow-hidden animate-in zoom-in-95 duration-150"
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
            className="flex-1 py-2.5 rounded-xl text-sm font-semibold text-content-secondary bg-white/5 border border-white/10 hover:bg-white/10 transition-all cursor-pointer"
          >
            {options.cancelLabel ?? 'Cancel'}
          </button>
          <button
            autoFocus
            onClick={() => respond(true)}
            className={`flex-1 py-2.5 rounded-xl text-sm font-bold text-content-primary transition-all cursor-pointer active:scale-[0.98] ${
              danger
                ? 'bg-danger hover:bg-danger shadow-[0_4px_15px_rgba(225,29,72,0.35)]'
                : 'bg-accent hover:bg-accent-hover shadow-[0_4px_15px_rgba(79,70,229,0.35)]'
            }`}
          >
            {options.confirmLabel ?? 'Confirm'}
          </button>
        </div>
      </div>
    </div>
  );
}
