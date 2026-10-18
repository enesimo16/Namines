'use client';

import { useState, useEffect } from 'react';
import { ArrowUp } from 'lucide-react';

/**
 * Shopify Polaris uyumlu, temaya duyarlı ve erişilebilir "Başa Dön" (Back to Top) butonu.
 */
export default function BackToTop() {
  const [isVisible, setIsVisible] = useState(false);

  useEffect(() => {
    const handleScroll = () => {
      // 320px'den fazla kaydırıldığında butonu göster
      if (window.scrollY > 320) {
        setIsVisible(true);
      } else {
        setIsVisible(false);
      }
    };

    window.addEventListener('scroll', handleScroll, { passive: true });
    handleScroll();
    return () => window.removeEventListener('scroll', handleScroll);
  }, []);

  const scrollToTop = () => {
    window.scrollTo({ top: 0, behavior: 'smooth' });
  };

  return (
    <button
      type="button"
      onClick={scrollToTop}
      aria-label="Back to top"
      title="Back to top"
      className={`tap-44 fixed bottom-5 right-5 z-sticky h-10 w-10 rounded-full bg-surface-800/90 hover:bg-surface-700/95 border border-surface-500/80 text-content-primary shadow-xl backdrop-blur-md transition-all duration-300 flex items-center justify-center cursor-pointer hover:border-accent hover:scale-105 active:scale-95 group ${
        isVisible
          ? 'opacity-100 translate-y-0 pointer-events-auto'
          : 'opacity-0 translate-y-4 pointer-events-none'
      }`}
    >
      <ArrowUp className="w-4 h-4 text-accent-text group-hover:text-content-primary transition-transform duration-200 group-hover:-translate-y-0.5" />
    </button>
  );
}
