'use client';

import { useEffect } from 'react';

/**
 * Açıkken Esc tuşuyla kapanma — `aria-modal` bir diyaloğun klavye çıkışı.
 *
 * Neden ortak bir kanca: elle çizilmiş (Radix olmayan) yirmiye yakın modal/panel
 * yalnızca fareyle kapanıyordu; klavye kullanıcısı ve ekran okuyucu için diyalog bir
 * tuzaktı (skill kuralı `escape-routes` / `keyboard-nav`, FRONTEND.md §6).
 *
 * `defaultPrevented` denetimi: iç içe bir bileşen (açılır menü, seçici) Esc'i kendi
 * işi için tüketmişse dış modal onunla birlikte kapanmaz.
 *
 * Bilerek KAPATMAYAN modallar var: kullanıcıdan bir KARAR isteyenler (ör. misafir şemasını
 * hesaba taşıma, birleştirme çakışması). Onlarda Esc, seçim yapılmadan çıkış olur ve
 * veri kaybına yol açabilir — bu kanca oralara eklenmedi.
 */
export function useEscapeKey(active: boolean, onEscape: () => void) {
  useEffect(() => {
    if (!active) return;
    const handler = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !event.defaultPrevented) onEscape();
    };
    document.addEventListener('keydown', handler);
    return () => document.removeEventListener('keydown', handler);
  }, [active, onEscape]);
}
