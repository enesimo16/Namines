import { create } from 'zustand';

export type ToastType = 'success' | 'info' | 'error' | 'warning';

interface ToastState {
  message: string | null;
  type: ToastType;
  showToast: (message: string, type?: ToastType) => void;
  hideToast: () => void;
}

export const useToastStore = create<ToastState>((set) => ({
  message: null,
  type: 'info',
  showToast: (message, type = 'info') => {
    set({ message, type });
    // Auto-hide after 4 seconds
    const timer = setTimeout(() => {
      set({ message: null });
    }, 4000);
    return () => clearTimeout(timer);
  },
  hideToast: () => set({ message: null }),
}));
