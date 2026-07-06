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

    const focusableElements = getFocusableElements();
    const firstElement = focusableElements[0];
    const lastElement = focusableElements[focusableElements.length - 1];

    // Auto-focus the first focusable element inside the modal
    if (firstElement) {
      firstElement.focus();
    }

    // Keep track of the active element before the modal opened to restore focus later
    const previousActiveElement = document.activeElement as HTMLElement | null;

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
