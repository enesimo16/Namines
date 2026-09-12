import { useEffect } from 'react';

/**
 * Traps keyboard focus within the elements of a given container when active.
 * 
 * @param isOpen Indicates if the trap is active.
 * @param containerRef React ref pointing to the container element.
 */
export function useFocusTrap(isOpen: boolean, containerRef: React.RefObject<HTMLElement | null>) {
  useEffect(() => {
    if (!isOpen || !containerRef.current) return;

    const container = containerRef.current;

    // Find all focusable child elements
    const focusableSelectors = [
      'a[href]',
      'area[href]',
      'input:not([disabled])',
      'select:not([disabled])',
      'textarea:not([disabled])',
      'button:not([disabled])',
      'iframe',
      'object',
      'embed',
      '[contenteditable]',
      '[tabindex]:not([tabindex="-1"])'
    ].join(',');

    const getFocusableElements = (): HTMLElement[] => {
      return Array.from(container.querySelectorAll(focusableSelectors));
    };

    // SIRA ONEMLI: geri donulecek oge, odak modala TASINMADAN once
    // yakalanmali.
    //
    // ONCEKI HALI BIR HATAYDI: `previousActiveElement`, `firstElement.focus()`
    // cagrildiktan SONRA okunuyordu. Yani "modaldan once odakta olan oge"
    // olarak modalin ICINDEKI ilk oge kaydediliyordu. Modal kapaninca o oge
    // DOM'dan kalktigi icin odak `<body>`e dusuyordu.
    //
    // OLCULDU (12.09.2026, /demo): karta Tab ile odaklanip Enter'a basildi,
    // Escape ile kapatildi -> `document.activeElement` BODY oldu, kart DEGIL.
    // Klavye kullanicisi modali kapattiktan sonra listenin basina donmek
    // zorunda kaliyordu (WCAG 2.4.3 Odak Sirasi).
    //
    // Bu kanca uygulamadaki TUM modallar tarafindan kullaniliyor, yani hata
    // her modalda vardi.
    const previousActiveElement = document.activeElement as HTMLElement | null;

    const focusableElements = getFocusableElements();
    const firstElement = focusableElements[0];

    // Auto-focus the first focusable element inside the modal
    if (firstElement) {
      firstElement.focus();
    }

    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key !== 'Tab') return;

      const elements = getFocusableElements();
      if (elements.length === 0) {
        e.preventDefault();
        return;
      }

      const first = elements[0];
      const last = elements[elements.length - 1];

      if (e.shiftKey) {
        // Shift + Tab: if on the first element, wrap to the last element
        if (document.activeElement === first) {
          last.focus();
          e.preventDefault();
        }
      } else {
        // Tab: if on the last element, wrap to the first element
        if (document.activeElement === last) {
          first.focus();
          e.preventDefault();
        }
      }
    };

    container.addEventListener('keydown', handleKeyDown);

    return () => {
      container.removeEventListener('keydown', handleKeyDown);
      // Restore focus to the element that was focused before opening the modal
      if (previousActiveElement && typeof previousActiveElement.focus === 'function') {
        previousActiveElement.focus();
      }
    };
  }, [isOpen, containerRef]);
}
