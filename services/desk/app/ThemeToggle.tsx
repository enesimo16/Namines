'use client';

import { useEffect, useState } from 'react';
import { Moon, Sun } from 'lucide-react';

const KEY = 'namines-desk-theme';
type Theme = 'light' | 'dark';

/**
 * Açık/koyu tema anahtarı — üst şeridin sağında.
 *
 * Tema `<html data-theme>` üzerinden uygulanıyor; hiçbir bileşen "hangi
 * temadayım" diye sormuyor, yalnızca token okuyor (bkz. globals.css'in
 * koyu tema bölümü). Bu, tema eklemenin bileşenlere tek satır bile
 * dokunmadan yapılabilmesini sağlıyor.
 *
 * Sıra: kullanıcının kayıtlı tercihi → işletim sisteminin tercihi →
 * açık. Kayıtlı tercih varken sistem tercihi YOK SAYILIR: kullanıcı
 * açıkça seçmişse, sistemi değiştirdiğinde seçimi altından kaymasın.
 */
export default function ThemeToggle() {
  const [theme, setTheme] = useState<Theme>('light');

  useEffect(() => {
    let initial: Theme = 'light';
    try {
      const saved = localStorage.getItem(KEY);
      if (saved === 'dark' || saved === 'light') initial = saved;
      else if (window.matchMedia?.('(prefers-color-scheme: dark)').matches) initial = 'dark';
    } catch { /* gizli mod */ }
    setTheme(initial);
    document.documentElement.dataset.theme = initial;
  }, []);

  function toggle() {
    setTheme(prev => {
      const next: Theme = prev === 'dark' ? 'light' : 'dark';
      document.documentElement.dataset.theme = next;
      try { localStorage.setItem(KEY, next); } catch { /* gizli mod */ }
      return next;
    });
  }

  return (
    <button
      className="icon-btn"
      onClick={toggle}
      title={theme === 'dark' ? 'Açık temaya geç' : 'Koyu temaya geç'}
      aria-label={theme === 'dark' ? 'Açık temaya geç' : 'Koyu temaya geç'}
    >
      {theme === 'dark' ? <Sun size={15} /> : <Moon size={15} />}
    </button>
  );
}
