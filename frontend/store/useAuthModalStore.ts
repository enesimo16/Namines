import { create } from 'zustand';

// Login/Sign-up modalını her yerden açabilmek için global durum
// (ör. 401 alındığında guest'e giriş yaptırmak).
interface AuthModalState {
  isOpen: boolean;
  open: () => void;
  close: () => void;
}

export const useAuthModalStore = create<AuthModalState>((set) => ({
  isOpen: false,
  open: () => set({ isOpen: true }),
  close: () => set({ isOpen: false }),
}));
