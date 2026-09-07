'use client';

import { Moon, Sun } from 'lucide-react';
import { useHomeThemeStore } from '../../store/useHomeThemeStore';

/**
 * Sağ-alt köşedeki açık/koyu önizleme anahtarı — render.com'un ekran
 * görüntüsündeki köşe düğmesinin birebir karşılığı. Kapsamı
 * `useHomeThemeStore`'da açıklanıyor: yalnızca üst gezinme + hero.
 */
export default function ThemeToggleButton() {
  const theme = useHomeThemeStore(s => s.theme);
  const toggle = useHomeThemeStore(s => s.toggle);

  return (
    <button
      onClick={toggle}
      aria-label={theme === 'dark' ? 'Switch to light preview' : 'Switch to dark preview'}
      title={theme === 'dark' ? 'Light preview' : 'Dark preview'}
      className={`fixed bottom-5 right-5 z-40 w-10 h-10 flex items-center justify-center rounded-full border transition-colors cursor-pointer ${
        theme === 'dark'
          ? 'bg-surface-800 border-content-primary/15 text-content-primary hover:bg-surface-700'
          : 'bg-white border-black/10 text-black hover:bg-black/5'
      }`}
    >
      {theme === 'dark' ? <Moon className="w-4 h-4" /> : <Sun className="w-4 h-4" />}
    </button>
  );
}
