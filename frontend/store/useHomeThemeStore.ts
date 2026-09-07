'use client';

import { create } from 'zustand';
import { persist } from 'zustand/middleware';

export type AppTheme = 'dark' | 'light';

interface HomeThemeState {
  theme: AppTheme;
  setTheme: (theme: AppTheme) => void;
  toggle: () => void;
}

const applyThemeToDom = (t: AppTheme) => {
  if (typeof document === 'undefined') return;

  document.documentElement.setAttribute('data-theme', t);
  document.body.setAttribute('data-theme', t);
  document.documentElement.style.colorScheme = t;
  if (t === 'light') {
    document.documentElement.classList.add('light');
    document.documentElement.classList.remove('dark');
    document.body.classList.add('light');
    document.body.classList.remove('dark');
  } else {
    document.documentElement.classList.add('dark');
    document.documentElement.classList.remove('light');
    document.body.classList.add('dark');
    document.body.classList.remove('light');
  }
};

export const useHomeThemeStore = create<HomeThemeState>()(
  persist(
    (set, get) => ({
      theme: 'dark',
      setTheme: (theme: AppTheme) => {
        applyThemeToDom(theme);
        set({ theme });
      },
      toggle: () => {
        const current = get().theme;
        const next: AppTheme = current === 'dark' ? 'light' : 'dark';
        applyThemeToDom(next);
        set({ theme: next });
      },
    }),
    {
      name: 'namines-home-theme-preview',
      onRehydrateStorage: () => (state) => {
        if (state?.theme) {
          applyThemeToDom(state.theme);
        }
      },
    },
  ),
);

// Alias
export const useThemeStore = useHomeThemeStore;

