import { create } from 'zustand';

// Native confirm() yerine estetik, promise-tabanlı onay dialogu.
// Kullanım: const ok = await confirmDialog({ title, message, danger: true });
export interface ConfirmOptions {
  title: string;
  message: string;
  confirmLabel?: string;
  cancelLabel?: string;
  danger?: boolean;
}

interface ConfirmState {
  isOpen: boolean;
  options: ConfirmOptions | null;
  _resolve: ((value: boolean) => void) | null;
  confirm: (options: ConfirmOptions) => Promise<boolean>;
  respond: (value: boolean) => void;
}

export const useConfirmStore = create<ConfirmState>((set, get) => ({
  isOpen: false,
  options: null,
  _resolve: null,
  confirm: (options) =>
    new Promise<boolean>((resolve) => {
      set({ isOpen: true, options, _resolve: resolve });
    }),
  respond: (value) => {
    get()._resolve?.(value);
    set({ isOpen: false, options: null, _resolve: null });
  },
}));

/** React dışından da çağrılabilen kısayol. */
export const confirmDialog = (options: ConfirmOptions) =>
  useConfirmStore.getState().confirm(options);
