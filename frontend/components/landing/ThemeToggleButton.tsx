'use client';

import { useSyncExternalStore } from 'react';
import { Moon, Sun } from 'lucide-react';
import { useHomeThemeStore } from '../../store/useHomeThemeStore';

/** Abonelik gerektirmeyen kaynak; deger hic degismedigi icin bos abone. */
const noopSubscribe = () => () => {};

/**
 * Istemci hidrasyonunun tamamlanip tamamlanmadigi.
 *
 * **Neden efekt + `setState` DEGIL:** `useEffect(() => setMounted(true), [])`
 * ayni isi iki render ile yapiyor ve React'in `set-state-in-effect` kurali
 * bunu hakli olarak isaretliyor. `useSyncExternalStore`, sunucu anlik
 * goruntusu olarak `false`, istemci anlik goruntusu olarak `true` dondurerek
 * ayni sonucu TEK render'da veriyor -- React ekibinin bu is icin onerdigi
 * desen budur.
 */
function useIsHydrated(): boolean {
  return useSyncExternalStore(noopSubscribe, () => true, () => false);
}

export default function ThemeToggleButton() {
  const { theme, toggle } = useHomeThemeStore();
  const mounted = useIsHydrated();

  if (!mounted) {
    return (
      <div
        className="w-8 h-8 rounded-full border border-surface-500/80 bg-surface-800/90 opacity-0"
        aria-hidden="true"
      />
    );
  }

  const isDark = theme === 'dark';

  return (
    <button
      onClick={toggle}
      type="button"
      title={isDark ? 'Switch to light theme (Beyaz mod)' : 'Switch to dark theme (Siyah mod)'}
      aria-label={isDark ? 'Switch to light theme' : 'Switch to dark theme'}
      className="tap-44 h-8 px-2.5 flex items-center gap-1.5 rounded-full border border-surface-500/80 bg-surface-800/95 hover:bg-surface-700/90 text-content-primary shadow-lg backdrop-blur-md transition-all hover:scale-105 active:scale-95 cursor-pointer select-none text-xs font-mono font-medium"
    >
      {isDark ? (
        <>
          <Moon className="w-3.5 h-3.5 text-accent-text shrink-0" />
          <span className="text-content-secondary hidden sm:inline">Dark</span>
        </>
      ) : (
        <>
          <Sun className="w-3.5 h-3.5 text-accent-text shrink-0" />
          <span className="text-content-secondary hidden sm:inline">Light</span>
        </>
      )}
    </button>
  );
}

