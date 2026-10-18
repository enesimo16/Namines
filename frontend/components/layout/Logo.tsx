'use client';

import React from 'react';

interface LogoProps {
  size?: 'xs' | 'sm' | 'default' | 'lg' | 'xl';
  showWordmark?: boolean;
  className?: string;
  variant?: 'light' | 'dark' | 'auto';
}

/**
 * Namines Resmi Marka Logosu
 * Proje dizinindeki namines-logo-horizontal ve namines-logo-only vektörel SVG'lerinden beslenir.
 */
export default function Logo({
  size = 'default',
  showWordmark = true,
  className = '',
}: LogoProps) {
  const heightClass = {
    xs: 'h-5',
    sm: 'h-7',
    default: 'h-8',
    lg: 'h-10',
    xl: 'h-12',
  }[size];

  return (
    <span className={`inline-flex items-center group select-none ${className}`}>
      {showWordmark ? (
        <>
          <img
            src="/brand/namines-logo-horizontal.png"
            alt="Namines"
            className={`theme-logo-dark ${heightClass} w-auto max-w-none object-contain transition-transform duration-200 group-hover:scale-[1.02]`}
            loading="eager"
            decoding="async"
          />
          <img
            src="/brand/namines-logo-horizontal-light.png"
            alt="Namines"
            className={`theme-logo-light ${heightClass} w-auto max-w-none object-contain transition-transform duration-200 group-hover:scale-[1.02]`}
            loading="eager"
            decoding="async"
          />
        </>
      ) : (
        <>
          <img
            src="/brand/namines-logo-only.png"
            alt="Namines"
            className={`theme-logo-dark ${heightClass} aspect-[1200/930] object-contain transition-transform duration-200 group-hover:scale-105`}
            loading="eager"
            decoding="async"
          />
          <img
            src="/brand/namines-logo-only-light.png"
            alt="Namines"
            className={`theme-logo-light ${heightClass} aspect-[1200/930] object-contain transition-transform duration-200 group-hover:scale-105`}
            loading="eager"
            decoding="async"
          />
        </>
      )}
    </span>
  );
}
