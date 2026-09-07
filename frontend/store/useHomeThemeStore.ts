import { create } from 'zustand';
import { persist } from 'zustand/middleware';

/**
 * Yalnızca tanıtım sayfasının (`/`) kendi açık/koyu ÖNİZLEME anahtarı.
 *
 * <b>Bu, uygulama genelinde bir tema sistemi DEĞİL.</b> Namines'in geri
 * kalanı (canvas, compile, Desk...) tamamen koyu tonlarda tasarlandı ve
 * `app/globals.css`'teki token'lar (`--surface-900` vb.) bu varsayımla
 * türetiliyor — basitçe tersine çevrilemezler (açık zeminde `content-primary`
 * formülü karanlık metin üretmez, oklch aralığının dışına taşar).
 *
 * Kullanıcının isteği render.com'un ekran görüntüsündeki sağ-alt köşe
 * anahtarını birebir yansıtmaktı: yalnızca üst gezinme + hero bölümü, açık
 * zemin ile taklit ediliyor (bkz. `Header.tsx`'in `isHome` dalı ve
 * `app/page.tsx`'in hero'su) — sayfanın geri kalanı (Features/How it works/
 * Footer) kasıtlı olarak koyu kalıyor, çünkü ekran görüntülerinde de
 * yalnızca üst kısım gösteriliyordu.
 */
interface HomeThemeState {
  theme: 'dark' | 'light';
  toggle: () => void;
}

export const useHomeThemeStore = create<HomeThemeState>()(
  persist(
    (set) => ({
      theme: 'dark',
      toggle: () => set(s => ({ theme: s.theme === 'dark' ? 'light' : 'dark' })),
    }),
    { name: 'namines-home-theme-preview' },
  ),
);
