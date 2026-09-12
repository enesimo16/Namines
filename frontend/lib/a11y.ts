import type { KeyboardEvent } from 'react';

/**
 * Kart biçimli tıklanabilir öğeler için klavye desteği (B-42 / WCAG 2.1.1).
 *
 * **Neden `<button>` kullanılmıyor:** bu öğeler içlerinde `<h3>`, `<div>` gibi
 * akış içeriği (flow content) taşıyor. HTML, `<button>` içinde yalnızca
 * ifade içeriğine (phrasing content) izin veriyor; başlık koymak geçersiz
 * işaretleme üretir ve ekran okuyucular bunu tutarsız biçimde duyurur.
 * Bu durumda doğru çözüm ARIA'nın belgelediği "custom button" deseni:
 * `role="button"` + `tabIndex={0}` + Enter/Space işleyicisi.
 *
 * **Space için `preventDefault` şart:** varsayılan davranışı sayfayı
 * kaydırmak. Engellenmezse kullanıcı kartı etkinleştirirken sayfa da bir
 * ekran aşağı kayar ve odağın nereye gittiği kaybolur.
 *
 * ÖLÇÜLDÜ (12.09.2026): denetim, React'in kendi `onClick` prop'unu okuyarak
 * klavyeyle erişilemeyen 29 tıklanabilir öğe buldu — tanıtım sayfasındaki 8
 * ekosistem kartı, demo sayfasındaki 20 şablon kartı ve başlıktaki kullanıcı
 * menüsü. Hiçbiri Tab ile erişilemiyordu, yani klavye kullanıcısı için bu
 * özellikler YOKTU.
 */
export function activateOnKey(activate: () => void) {
  return (e: KeyboardEvent<HTMLElement>) => {
    // Kartın İÇİNDEKİ gerçek bir kontrolden gelen tuşu yutmuyoruz.
    if (e.target !== e.currentTarget) return;
    if (e.key === 'Enter' || e.key === ' ' || e.key === 'Spacebar') {
      e.preventDefault();
      activate();
    }
  };
}
