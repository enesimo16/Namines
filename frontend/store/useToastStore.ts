import { create } from 'zustand';

// ─── Tip Tanımları ────────────────────────────────────────────────────────────

export type ToastType = 'success' | 'info' | 'error' | 'warning' | 'loading' | 'ai';

export interface Toast {
  id: string;
  message: string;
  type: ToastType;
  /** Otomatik kapanma süresi (ms). 0 = kalıcı (kullanıcı dismiss etmeli). */
  duration: number;
  /**
   * 0-100 arası ilerleme değeri. `loading` ve `ai` tipleri için progress bar gösterir.
   * undefined ise bar gösterilmez.
   */
  progress?: number;
  /** Opsiyonel aksiyon butonu */
  action?: { label: string; onClick: () => void };
  /** Kullanıcı tarafından kapatılabilir mi? */
  dismissible: boolean;
  /** Oluşturulma zamanı (ms) — FIFO sıralama için */
  createdAt: number;
  /** İç timer ID — cleanup için */
  _timerId?: ReturnType<typeof setTimeout>;
}

export interface ToastState {
  toasts: Toast[];
  /** Aynı anda gösterilecek maksimum toast sayısı */
  maxVisible: number;

  /**
   * Yeni bir toast gösterir ve toast'un ID'sini döndürür.
   * Döndürülen ID ile `updateToast()` veya `dismissToast()` çağrılabilir
   * (örn: loading → success geçişi).
   *
   * Geriye dönük uyumluluk: `showToast(message, type)` şeklinde de çalışır.
   */
  showToast: (
    message: string,
    typeOrOptions?: ToastType | Partial<Omit<Toast, 'id' | 'createdAt' | '_timerId'>>,
    legacyDuration?: number
  ) => string;

  /**
   * Mevcut bir toast'ı ID bazında günceller.
   * Kullanım: loading → success/error geçişi.
   */
  updateToast: (id: string, updates: Partial<Omit<Toast, 'id' | 'createdAt' | '_timerId'>>) => void;

  /** Toast'ı ID ile kaldırır. */
  dismissToast: (id: string) => void;

  /** Tüm toast'ları temizler. */
  clearAll: () => void;
}

// ─── Yardımcı ─────────────────────────────────────────────────────────────────

/** Rastgele ID üretir (crypto.randomUUID SSR'da mevcut olmayabileceğinden yedekle) */
function generateId(): string {
  if (typeof crypto !== 'undefined' && crypto.randomUUID) {
    return crypto.randomUUID();
  }
  return `toast-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`;
}

/**
 * Toast tipine göre varsayılan görünür kalma süresini döndürür.
 * - `loading` ve `ai`: 0 (kalıcı — çağıran `updateToast()` ile kapatır)
 * - `error`: 7000 ms (kullanıcının okuyabilmesi için uzun)
 * - Diğerleri: 4000 ms
 */
function defaultDuration(type: ToastType): number {
  if (type === 'loading' || type === 'ai') return 0;
  if (type === 'error') return 7000;
  return 4000;
}

/** Screen reader için aria-live bölgesini günceller */
function announceToScreenReader(message: string): void {
  if (typeof document === 'undefined') return;
  const region = document.getElementById('aria-live-region');
  if (region) {
    // Aynı mesajı iki kez söylemesi için önce boşalt, sonra doldur
    region.textContent = '';
    requestAnimationFrame(() => {
      region.textContent = message;
    });
  }
}

// ─── Store ───────────────────────────────────────────────────────────────────

export const useToastStore = create<ToastState>((set, get) => ({
  toasts: [],
  maxVisible: 5,

  showToast: (message, typeOrOptions = 'info', legacyDuration) => {
    const id = generateId();

    // Geriye dönük uyumluluk: ikinci parametre string (ToastType) veya options objesi olabilir
    let opts: Partial<Omit<Toast, 'id' | 'createdAt' | '_timerId'>> = {};
    if (typeof typeOrOptions === 'string') {
      opts.type = typeOrOptions as ToastType;
      if (legacyDuration !== undefined) opts.duration = legacyDuration;
    } else {
      opts = { ...typeOrOptions };
    }

    const type: ToastType    = opts.type      ?? 'info';
    const duration: number   = opts.duration  ?? defaultDuration(type);
    const dismissible        = opts.dismissible ?? true;

    const toast: Toast = {
      id,
      message,
      type,
      duration,
      progress:    opts.progress,
      action:      opts.action,
      dismissible,
      createdAt:   Date.now(),
    };

    // Aynı mesaj zaten kuyruktaysa ekleme (duplicate spam koruması)
    const existing = get().toasts;
    const isDuplicate = existing.some(
      t => t.message === message && t.type === type
    );
    if (isDuplicate) return existing.find(t => t.message === message && t.type === type)!.id;

    // Timer kur (süre 0 ise kalıcı — timer kurma)
    let timerId: ReturnType<typeof setTimeout> | undefined;
    if (duration > 0) {
      timerId = setTimeout(() => {
        get().dismissToast(id);
      }, duration);
    }
    toast._timerId = timerId;

    set(state => ({
      // FIFO: en fazla maxVisible kadar tut. Eskiyi önce at.
      toasts: [...state.toasts, toast].slice(-state.maxVisible),
    }));

    // Screen reader'a bildir
    announceToScreenReader(message);

    return id;
  },

  updateToast: (id, updates) => {
    set(state => {
      const updated = state.toasts.map(t => {
        if (t.id !== id) return t;

        // Eski timer'ı iptal et
        if (t._timerId) clearTimeout(t._timerId);

        const newType     = updates.type     ?? t.type;
        const newDuration = updates.duration ?? defaultDuration(newType);
        const newDismissible = updates.dismissible ?? t.dismissible;

        // Yeni timer kur
        let timerId: ReturnType<typeof setTimeout> | undefined;
        if (newDuration > 0) {
          timerId = setTimeout(() => {
            get().dismissToast(id);
          }, newDuration);
        }

        const updated: Toast = {
          ...t,
          ...updates,
          type:        newType,
          duration:    newDuration,
          dismissible: newDismissible,
          _timerId:    timerId,
        };

        if (updates.message) announceToScreenReader(updates.message);
        return updated;
      });

      return { toasts: updated };
    });
  },

  dismissToast: (id) => {
    set(state => {
      const toast = state.toasts.find(t => t.id === id);
      if (toast?._timerId) clearTimeout(toast._timerId);
      return { toasts: state.toasts.filter(t => t.id !== id) };
    });
  },

  clearAll: () => {
    // Tüm timer'ları temizle
    get().toasts.forEach(t => {
      if (t._timerId) clearTimeout(t._timerId);
    });
    set({ toasts: [] });
  },
}));
