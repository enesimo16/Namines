'use client';

import React, { useEffect, useRef, useState } from 'react';
import { CheckCircle2, XCircle, AlertTriangle, Info, Loader2, Sparkles } from 'lucide-react';
import { useToastStore, Toast, ToastType } from '../../store/useToastStore';

// ─── Tip Mappings ─────────────────────────────────────────────────────────────
// Yalnızca iki semantik renk: başarı (yeşil) ve hata (kırmızı). Warning/info/
// loading/ai nötr off-white — sarı/mor/indigo aile tamamen kaldırıldı
// (kullanıcı talimatı: "sarı bir şey istemiyorum").

/** Sol kenar çubuğu rengi */
const ACCENT: Record<ToastType, string> = {
  success: 'var(--color-success)',
  error:   'var(--color-danger)',
  warning: 'var(--color-content-primary)',
  info:    'var(--color-content-primary)',
  loading: 'var(--color-content-primary)',
  ai:      'var(--color-content-primary)',
};

/** Arka plan sınıfları */
const BG_CLASS: Record<ToastType, string> = {
  success: 'bg-success-subtle/95 border-success/25',
  error:   'bg-danger-subtle/95 border-danger/25',
  warning: 'bg-surface-800/95 border-content-primary/12',
  info:    'bg-surface-800/95 border-content-primary/12',
  loading: 'bg-surface-800/95 border-content-primary/12',
  ai:      'bg-surface-800/95 border-content-primary/12',
};

/** Progress bar rengi */
const BAR_CLASS: Record<ToastType, string> = {
  success: 'from-success to-success-text',
  error:   'from-danger to-danger-text',
  warning: 'from-content-muted to-content-secondary',
  info:    'from-content-muted to-content-secondary',
  loading: 'from-content-muted to-content-secondary',
  ai:      'from-content-muted to-content-secondary',
};

/** Varsayılan ikonlar — emoji değil, SVG (bkz. FRONTEND.md) */
const ICON: Record<ToastType, React.ComponentType<{ className?: string }>> = {
  success: CheckCircle2,
  error:   XCircle,
  warning: AlertTriangle,
  info:    Info,
  loading: Loader2,
  ai:      Sparkles,
};

const ICON_COLOR: Record<ToastType, string> = {
  success: 'text-success-text',
  error:   'text-danger-text',
  warning: 'text-content-secondary',
  info:    'text-content-muted',
  loading: 'text-content-muted animate-spin',
  ai:      'text-content-secondary',
};

// ─── Tek Toast Kartı ──────────────────────────────────────────────────────────

interface ToastItemProps {
  toast: Toast;
  onDismiss: (id: string) => void;
}

function ToastItem({ toast, onDismiss }: ToastItemProps) {
  const [visible, setVisible]   = useState(false); // giriş animasyonu
  const [exiting, setExiting]   = useState(false); // çıkış animasyonu
  const [elapsed, setElapsed]   = useState(0);     // elapsed ms (progress bar)
  const startRef                = useRef(Date.now());
  const rafRef                  = useRef<number | null>(null);

  // ── Giriş animasyonu: mount sonrası 1 frame gecikmeli tetikle
  useEffect(() => {
    const id = requestAnimationFrame(() => setVisible(true));
    return () => cancelAnimationFrame(id);
  }, []);

  // ── Progress bar: loading/ai için elapsed time sayacı
  useEffect(() => {
    if (toast.type !== 'loading' && toast.type !== 'ai') return;
    if (toast.progress !== undefined) return; // manuel progress varsa dış kontrole bırak

    const tick = () => {
      setElapsed(Date.now() - startRef.current);
      rafRef.current = requestAnimationFrame(tick);
    };
    rafRef.current = requestAnimationFrame(tick);
    return () => {
      if (rafRef.current) cancelAnimationFrame(rafRef.current);
    };
  }, [toast.type, toast.progress]);

  // ── Çıkış: dismiss triggerı
  const handleDismiss = () => {
    setExiting(true);
    setTimeout(() => onDismiss(toast.id), 220);
  };

  // ── Toast dışarıdan kaldırıldığında çıkış animasyonu koy
  const isActive = useToastStore(s => s.toasts.some(t => t.id === toast.id));
  const prevActive = useRef(true);
  useEffect(() => {
    if (prevActive.current && !isActive && !exiting) {
      setExiting(true);
    }
    prevActive.current = isActive;
  }, [isActive, exiting]);

  // ── Determinate progress hesapla
  const progressPct = (() => {
    if (toast.progress !== undefined) return Math.min(100, toast.progress);
    if (toast.duration > 0 && (toast.type === 'loading' || toast.type === 'ai')) {
      return Math.min(100, (elapsed / Math.max(toast.duration, 1)) * 100);
    }
    return null; // bar gösterme
  })();

  const isPulse = (toast.type === 'loading' || toast.type === 'ai') && progressPct === null;

  return (
    <div
      role="status"
      aria-live="polite"
      aria-atomic="true"
      style={{
        transform:  exiting ? 'translateX(calc(100% + 20px))' : visible ? 'translateX(0)' : 'translateX(calc(100% + 20px))',
        opacity:    exiting ? 0 : visible ? 1 : 0,
        transition: exiting
          ? 'transform 200ms cubic-bezier(0.4, 0, 1, 1), opacity 200ms ease-in'
          : 'transform 280ms cubic-bezier(0.16, 1, 0.3, 1), opacity 250ms ease-out',
        willChange: 'transform, opacity',
      }}
      className={`
        relative w-[360px] max-w-[calc(100vw-32px)] rounded-[var(--radius-modal)] border
        backdrop-blur-xl shadow-[0_8px_32px_color-mix(in srgb, var(--color-scrim) 45%, transparent)]
        overflow-hidden pointer-events-auto
        ${BG_CLASS[toast.type]}
      `}
    >
      {/* Sol renk çubuğu */}
      <div
        className="absolute left-0 top-0 bottom-0 w-[3px] rounded-l-2xl"
        style={{ background: ACCENT[toast.type] }}
      />

      {/* İçerik */}
      <div className="flex items-start gap-3 px-4 py-3 pl-5">
        {/* İkon */}
        {(() => {
          const IconComp = ICON[toast.type];
          return <IconComp className={`w-4 h-4 mt-0.5 flex-shrink-0 ${ICON_COLOR[toast.type]}`} />;
        })()}

        {/* Mesaj */}
        <p className="flex-1 text-sm font-medium text-content-primary leading-snug break-words min-w-0">
          {toast.message}
        </p>

        {/* Aksiyon + Kapat */}
        <div className="flex items-center gap-1.5 flex-shrink-0 ml-1">
          {toast.action && (
            <button
              onClick={toast.action.onClick}
              className="text-xs font-bold px-2 py-1 rounded-[var(--radius-control)] border border-white/20 text-white/80
                         hover:bg-white/10 hover:text-content-primary transition-colors"
            >
              {toast.action.label}
            </button>
          )}
          {toast.dismissible && (
            <button
              onClick={handleDismiss}
              aria-label="Dismiss notification"
              className="w-6 h-6 flex items-center justify-center rounded-[var(--radius-control)] text-white/40
                         hover:text-white/80 hover:bg-white/10 transition-colors text-base leading-none"
            >
              ×
            </button>
          )}
        </div>
      </div>

      {/* Progress / Pulse Bar */}
      {(progressPct !== null || isPulse) && (
        <div className="relative h-[3px] w-full overflow-hidden">
          {/* Arka plan track */}
          <div className="absolute inset-0 bg-white/5" />

          {isPulse ? (
            /* Sonsuz pulse efekti — loading/ai kalıcı */
            <div
              className={`absolute top-0 h-full w-1/3 bg-gradient-to-r ${BAR_CLASS[toast.type]} rounded-full`}
              style={{
                animation: 'namines-toast-slide 1.4s ease-in-out infinite',
              }}
            />
          ) : (
            /* Determinate bar */
            <div
              className={`absolute top-0 left-0 h-full bg-gradient-to-r ${BAR_CLASS[toast.type]} rounded-full`}
              style={{
                width:      `${progressPct}%`,
                transition: 'width 150ms linear',
                boxShadow:  `0 0 6px ${ACCENT[toast.type]}80`,
              }}
            />
          )}
        </div>
      )}

    </div>
  );
}

// ─── Animasyon CSS Injection ──────────────────────────────────────────────────

const ANIMATION_STYLES = `
@keyframes namines-toast-slide {
  0%   { left: -33%; }
  50%  { left: 50%; }
  100% { left: 133%; }
}
@keyframes namines-toast-glow {
  0%, 100% { opacity: 0.6; }
  50%       { opacity: 1; }
}
`;

function InjectStyles() {
  useEffect(() => {
    if (document.getElementById('namines-toast-styles')) return;
    const style = document.createElement('style');
    style.id = 'namines-toast-styles';
    style.textContent = ANIMATION_STYLES;
    document.head.appendChild(style);
    return () => style.remove();
  }, []);
  return null;
}

// ─── Container ───────────────────────────────────────────────────────────────

export default function ToastContainer() {
  const toasts      = useToastStore(s => s.toasts);
  const dismissToast = useToastStore(s => s.dismissToast);

  return (
    <>
      <InjectStyles />
      {/*
       * Sabit konum: sağ alt köşe, tüm içerikler üstünde (z-[9999]).
       * pointer-events-none: container tıklamaları tüketemez.
       * Her ToastItem kendi pointer-events-auto'sunu açar.
       */}
      <div
        aria-label="Notifications"
        aria-live="polite"
        className="fixed bottom-4 right-4 z-[9999] flex flex-col-reverse gap-2.5 pointer-events-none"
        style={{ maxHeight: 'calc(100vh - 32px)', overflowY: 'hidden' }}
      >
        {toasts.map(toast => (
          <ToastItem
            key={toast.id}
            toast={toast}
            onDismiss={dismissToast}
          />
        ))}
      </div>
    </>
  );
}
