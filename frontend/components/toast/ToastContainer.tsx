'use client';

import React, { useEffect, useRef, useState } from 'react';
import { useToastStore, Toast, ToastType } from '../../store/useToastStore';

// ─── Tip Eşlemeleri ──────────────────────────────────────────────────────────
/*
 * <b>Yeniden tasarım: minimum renk, terminal etiketi.</b>
 *
 * Eski tasarımda her bildirim şunları taşıyordu: renkli zemin
 * (`bg-success-subtle`), 3px sol renk çubuğu, renkli ikon ve parlayan
 * gradyanlı ilerleme çubuğu. Yani tek bir bildirimde DÖRT ayrı renkli öğe
 * vardı; 143 çağrı noktasıyla birleşince ekran sürekli renk yakıp söndüren
 * bir yüzeye dönüşüyordu (bkz. UI_UX_PRODUCT_AUDIT.md §4/O1).
 *
 * Yeni kural: <b>yüzey her zaman nötr</b>, renk yalnızca 2-4 karakterlik
 * monospace etikette. Etiket hem tipi söylüyor hem ikonun işini yapıyor —
 * ayrı bir ikona gerek kalmıyor. Terminal çıktısı hissi, ürünün kimliğiyle
 * (veritabanı/geliştirici aracı, JetBrains Mono zaten yüklü) tutarlı ve
 * jenerik "renkli kart + ikon + progress" bildiriminden ayrışıyor.
 *
 * Renk yalnızca ANLAM taşıdığı yerde: başarı ve hata. Uyarı/bilgi/yükleme
 * nötr — FRONTEND.md §2'nin "warning ayrı bir renk değil" kuralı.
 */

/** 2-4 karakterlik durum etiketi — ikonun yerine geçiyor. */
const TAG: Record<ToastType, string> = {
  success: 'OK',
  error:   'ERR',
  warning: 'WARN',
  info:    'INFO',
  loading: '···',
  ai:      'AI',
};

/** Etiket rengi — TEK renkli öğe. Yalnızca başarı/hata renkli. */
const TAG_COLOR: Record<ToastType, string> = {
  success: 'text-success-text',
  error:   'text-danger-text',
  warning: 'text-content-secondary',
  info:    'text-content-muted',
  loading: 'text-content-muted',
  ai:      'text-accent-text',
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
  // `useRef(Date.now())` DEĞİL: argüman her render'da yeniden hesaplanıyordu
  // (yalnızca ilki saklansa da) ve render'ı saf olmaktan çıkarıyordu.
  // Başlangıç zamanı, sayacın gerçekten başladığı efektte damgalanıyor.
  const startRef                = useRef(0);
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
          ? 'transform var(--dur-base) cubic-bezier(0.4, 0, 1, 1), opacity var(--dur-base) ease-in'
          : 'transform var(--dur-slow) var(--ease-out), opacity var(--dur-base) var(--ease-out)',
        willChange: 'transform, opacity',
      }}
      /*
       * Tek nötr yüzey — tipe göre zemin/kenarlık DEĞİŞMİYOR.
       * `backdrop-blur` kaldırıldı: her karede yeniden hesaplanan pahalı bir
       * filtre ve düz bir yüzeyde görsel karşılığı yok (bkz. §19 performans).
       * Genişlik 360 → 320px: bildirim asıl işin önüne geçmemeli.
       */
      className="relative w-[320px] max-w-[calc(100vw-32px)] rounded-[var(--radius-card)]
                 border border-[var(--color-border-hairline)] bg-surface-800
                 shadow-[0_4px_16px_color-mix(in_srgb,var(--color-scrim)_50%,transparent)]
                 overflow-hidden pointer-events-auto"
    >
      <div className="flex items-start gap-2.5 px-3 py-2.5">
        {/* Durum etiketi — ikonun yerine geçiyor, tek renkli öğe. */}
        <span
          className={`font-mono text-micro shrink-0 w-9 pt-px tabular-nums ${TAG_COLOR[toast.type]} ${
            toast.type === 'loading' ? 'animate-pulse' : ''
          }`}
        >
          {TAG[toast.type]}
        </span>

        <p className="flex-1 text-caption text-content-secondary leading-snug break-words min-w-0">
          {toast.message}
        </p>

        <div className="flex items-center gap-1 shrink-0">
          {toast.action && (
            <button
              onClick={toast.action.onClick}
              className="focus-ring text-micro font-semibold px-2 py-1 rounded-[var(--radius-control)]
                         border border-[var(--color-border-hairline)] text-content-secondary
                         hover:text-content-primary hover:border-[var(--color-border-strong)]
                         transition-colors cursor-pointer"
            >
              {toast.action.label}
            </button>
          )}
          {toast.dismissible && (
            <button
              onClick={handleDismiss}
              aria-label="Dismiss notification"
              className="focus-ring w-6 h-6 flex items-center justify-center rounded-[var(--radius-control)]
                         text-content-subtle hover:text-content-primary transition-colors
                         cursor-pointer text-sm leading-none"
            >
              ×
            </button>
          )}
        </div>
      </div>

      {/*
       * İlerleme: 1px NÖTR çizgi. Eski hâli gradyan + `boxShadow` parıltıydı;
       * ilerleme bilgisi renk gerektirmiyor — konum zaten söylüyor.
       */}
      {(progressPct !== null || isPulse) && (
        <div className="relative h-px w-full overflow-hidden bg-[var(--color-border-hairline)]">
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

// ─── Animasyon CSS Injection ──────────────────────────────────────────────────

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
