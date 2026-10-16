'use client';

import React, { useEffect, useRef, useState } from 'react';
import { useToastStore, Toast, ToastType } from '../../store/useToastStore';
import {
  CheckCircle2,
  AlertCircle,
  AlertTriangle,
  Info,
  Loader2,
  Sparkles,
  X
} from 'lucide-react';

// ─── Shopify Polaris Durum Eşlemeleri ──────────────────────────────────────────

interface TypeConfig {
  label: string;
  icon: React.ReactNode;
  iconBg: string;
  iconColor: string;
}

const TYPE_CONFIGS: Record<ToastType, TypeConfig> = {
  success: {
    label: 'Success',
    icon: <CheckCircle2 className="w-4 h-4" />,
    iconBg: 'bg-surface-800 border border-line-strong',
    iconColor: 'text-success-text',
  },
  error: {
    label: 'Error',
    icon: <AlertCircle className="w-4 h-4" />,
    iconBg: 'bg-surface-800 border border-line-strong',
    iconColor: 'text-danger-text',
  },
  warning: {
    label: 'Notice',
    icon: <AlertTriangle className="w-4 h-4" />,
    iconBg: 'bg-surface-800 border border-line-strong',
    iconColor: 'text-accent-text',
  },
  info: {
    label: 'Info',
    icon: <Info className="w-4 h-4" />,
    iconBg: 'bg-surface-800 border border-line-strong',
    iconColor: 'text-content-secondary',
  },
  loading: {
    label: 'Processing',
    icon: <Loader2 className="w-4 h-4 animate-spin" />,
    iconBg: 'bg-surface-800 border border-line-strong',
    iconColor: 'text-content-muted',
  },
  ai: {
    label: 'AI Copilot',
    icon: <Sparkles className="w-4 h-4" />,
    iconBg: 'bg-surface-800 border border-line-strong',
    iconColor: 'text-accent-text',
  },
};

// ─── Tek Toast Kartı (Shopify Polaris Toast) ──────────────────────────────────

interface ToastItemProps {
  toast: Toast;
  onDismiss: (id: string) => void;
}

function ToastItem({ toast, onDismiss }: ToastItemProps) {
  const [visible, setVisible] = useState(false);
  const [exiting, setExiting] = useState(false);
  const [elapsed, setElapsed] = useState(0);
  const startRef = useRef(0);
  const rafRef = useRef<number | null>(null);

  // Mount sonrası 1 frame gecikmeyle sağ-alttan yukarı süzülme animasyonu
  useEffect(() => {
    const id = requestAnimationFrame(() => setVisible(true));
    return () => cancelAnimationFrame(id);
  }, []);

  // Loading/AI için zaman sayacı
  useEffect(() => {
    if (toast.type !== 'loading' && toast.type !== 'ai') return;
    if (toast.progress !== undefined) return;

    startRef.current = Date.now();
    const tick = () => {
      setElapsed(Date.now() - startRef.current);
      rafRef.current = requestAnimationFrame(tick);
    };
    rafRef.current = requestAnimationFrame(tick);
    return () => {
      if (rafRef.current) cancelAnimationFrame(rafRef.current);
    };
  }, [toast.type, toast.progress]);

  const handleDismiss = () => {
    setExiting(true);
    setTimeout(() => onDismiss(toast.id), 220);
  };

  const isActive = useToastStore(s => s.toasts.some(t => t.id === toast.id));
  const prevActive = useRef(true);
  useEffect(() => {
    if (prevActive.current && !isActive && !exiting) {
      setExiting(true);
    }
    prevActive.current = isActive;
  }, [isActive, exiting]);

  const progressPct = (() => {
    if (toast.progress !== undefined) return Math.min(100, toast.progress);
    if (toast.duration > 0 && (toast.type === 'loading' || toast.type === 'ai')) {
      return Math.min(100, (elapsed / Math.max(toast.duration, 1)) * 100);
    }
    return null;
  })();

  const isPulse = (toast.type === 'loading' || toast.type === 'ai') && progressPct === null;
  const config = TYPE_CONFIGS[toast.type] || TYPE_CONFIGS.info;

  return (
    <div
      role="status"
      aria-live="polite"
      aria-atomic="true"
      style={{
        transform: exiting
          ? 'translateY(12px) scale(0.96)'
          : visible
          ? 'translateY(0) scale(1)'
          : 'translateY(24px) scale(0.94)',
        opacity: exiting ? 0 : visible ? 1 : 0,
        transition: exiting
          ? 'transform 200ms cubic-bezier(0.4, 0, 1, 1), opacity 180ms ease-in'
          : 'transform 280ms cubic-bezier(0.16, 1, 0.3, 1), opacity 240ms ease-out',
        willChange: 'transform, opacity',
      }}
      className="relative w-[340px] max-w-[calc(100vw-32px)] rounded-[var(--radius-card)]
                 bg-surface-900 border border-line-strong shadow-2xl
                 overflow-hidden pointer-events-auto select-none"
    >
      <div className="flex items-start gap-3 p-3">
        {/* Polaris Durum İkonu */}
        <div
          className={`shrink-0 w-7 h-7 rounded-[var(--radius-control)] flex items-center justify-center ${config.iconBg} ${config.iconColor}`}
        >
          {config.icon}
        </div>

        {/* Mesaj İçeriği */}
        <div className="flex-1 min-w-0 pt-0.5">
          <div className="flex items-center gap-1.5 mb-0.5">
            <span className="text-micro font-bold uppercase tracking-wider text-content-muted">
              {config.label}
            </span>
          </div>
          <p className="text-xs text-content-primary leading-relaxed break-words font-medium">
            {toast.message}
          </p>
        </div>

        {/* Aksiyon & Kapat Düğmeleri */}
        <div className="flex items-center gap-1 shrink-0 pt-0.5">
          {toast.action && (
            <button
              onClick={toast.action.onClick}
              className="text-xs font-semibold px-2 py-1 rounded-[var(--radius-control)]
                         bg-surface-700 hover:bg-surface-600 text-content-primary
                         border border-line hover:border-line-strong transition-colors cursor-pointer"
            >
              {toast.action.label}
            </button>
          )}

          {toast.dismissible && (
            <button
              onClick={handleDismiss}
              aria-label="Dismiss notification"
              className="w-6 h-6 flex items-center justify-center rounded-[var(--radius-control)]
                         text-content-muted hover:text-content-primary hover:bg-surface-700/60
                         transition-colors cursor-pointer"
            >
              <X className="w-3.5 h-3.5" />
            </button>
          )}
        </div>
      </div>

      {/* Polaris İlerleme Çizgisi */}
      {(progressPct !== null || isPulse) && (
        <div className="relative h-1 w-full overflow-hidden bg-surface-800">
          {isPulse ? (
            <div
              className="absolute top-0 h-full w-1/3 bg-content-muted"
              style={{ animation: 'namines-toast-slide 1.4s ease-in-out infinite' }}
            />
          ) : (
            <div
              className="absolute top-0 left-0 h-full bg-content-muted"
              style={{ width: `${progressPct}%`, transition: 'width 150ms linear' }}
            />
          )}
        </div>
      )}
    </div>
  );
}

// ─── Animasyon CSS ────────────────────────────────────────────────────────────

const ANIMATION_STYLES = `
@keyframes namines-toast-slide {
  0%   { left: -33%; }
  50%  { left: 50%; }
  100% { left: 133%; }
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

// ─── Container (Sağ-Alt Köşe) ──────────────────────────────────────────────────

export default function ToastContainer() {
  const toasts = useToastStore(s => s.toasts);
  const dismissToast = useToastStore(s => s.dismissToast);

  return (
    <>
      <InjectStyles />
      <div
        aria-label="Notifications"
        aria-live="polite"
        className="fixed bottom-5 right-5 z-toast flex flex-col gap-2 pointer-events-none"
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
