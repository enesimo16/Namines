'use client';

import { useSyncExternalStore } from 'react';
import { Moon, Sun } from 'lucide-react';
import { useHomeThemeStore } from '../../store/useHomeThemeStore';

const noopSubscribe = () => () => {};

function useIsHydrated(): boolean {
  return useSyncExternalStore(noopSubscribe, () => true, () => false);
}

export default function ThemeToggleButton() {
  const { theme, toggle } = useHomeThemeStore();
  const mounted = useIsHydrated();

  if (!mounted) {
    return (
      <div
        className="w-9 h-9 rounded-full opacity-0 shrink-0"
        aria-hidden="true"
      />
    );
  }

  const isDark = theme === 'dark';

  return (
    <button
      onClick={toggle}
      type="button"
      title={isDark ? 'Switch to light theme' : 'Switch to dark theme'}
      aria-label={isDark ? 'Switch to light theme' : 'Switch to dark theme'}
      className="tap-44 relative w-9 h-9 inline-flex items-center justify-center rounded-full text-content-primary/80 hover:text-content-primary hover:bg-surface-800 transition-colors duration-200 active:scale-90 cursor-pointer select-none shrink-0 group overflow-hidden"
    >
      <Sun
        className={`w-[18px] h-[18px] absolute transition-all duration-500 ease-[cubic-bezier(0.22,1,0.36,1)] ${
          isDark
            ? 'rotate-90 scale-0 opacity-0'
            : 'rotate-0 scale-100 opacity-100 group-hover:rotate-45'
        }`}
      />
      <Moon
        className={`w-[18px] h-[18px] absolute transition-all duration-500 ease-[cubic-bezier(0.22,1,0.36,1)] ${
          isDark
            ? 'rotate-0 scale-100 opacity-100 group-hover:-rotate-12'
            : '-rotate-90 scale-0 opacity-0'
        }`}
      />
    </button>
  );
}
