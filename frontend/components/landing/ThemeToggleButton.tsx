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
        className="w-[72px] h-8 rounded-full border border-line-strong bg-surface-800/90 opacity-0 shrink-0"
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
      className="tap-44 h-8 w-[72px] inline-flex items-center justify-center gap-1.5 rounded-full border border-line-strong bg-surface-800/80 hover:bg-surface-700/80 text-content-primary shadow-sm transition-all duration-300 ease-[cubic-bezier(0.2,0.8,0.2,1)] active:scale-95 cursor-pointer select-none text-[10px] font-mono font-bold tracking-wider shrink-0 group"
    >
      {isDark ? (
        <>
          <Moon className="w-3.5 h-3.5 text-content-secondary shrink-0 transition-transform duration-300 ease-[cubic-bezier(0.2,0.8,0.2,1)] group-hover:rotate-12" />
          <span className="text-content-secondary uppercase transition-colors">Dark</span>
        </>
      ) : (
        <>
          <Sun className="w-3.5 h-3.5 text-content-secondary shrink-0 transition-transform duration-300 ease-[cubic-bezier(0.2,0.8,0.2,1)] group-hover:rotate-45" />
          <span className="text-content-secondary uppercase transition-colors">Light</span>
        </>
      )}
    </button>
  );
}

