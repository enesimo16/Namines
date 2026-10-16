'use client';

import React from 'react';

interface LogoProps {
  size?: 'xs' | 'sm' | 'default' | 'lg' | 'xl';
  showWordmark?: boolean;
  className?: string;
  variant?: 'light' | 'dark' | 'auto';
}

/**
 * Namines Resmi Marka Logosu — Masaüstündeki namines.png marka kılavuzundan
 * türetilmiştir. Katlanmış şerit 'N' glifi, sol alttaki karakteristik turkuaz
 * nokta (#5EE6C4) ve geniş aralıklı 'N Λ M I N E S' kelime işareti.
 */
export default function Logo({
  size = 'default',
  showWordmark = true,
  className = '',
}: LogoProps) {
  const symbolDimensions = {
    xs: 'w-4 h-4',
    sm: 'w-5 h-5',
    default: 'w-6 h-6',
    lg: 'w-8 h-8',
    xl: 'w-10 h-10',
  }[size];

  const textSize = {
    xs: 'text-xs tracking-[0.25em]',
    sm: 'text-xs tracking-[0.22em] font-semibold',
    default: 'text-sm tracking-[0.24em] font-bold',
    lg: 'text-base tracking-[0.25em] font-bold',
    xl: 'text-lg tracking-[0.26em] font-bold',
  }[size];

  return (
    <span className={`inline-flex items-center gap-2.5 group select-none ${className}`}>
      {/* Marka Sembolü: Katlanmış N + Turkuaz Nokta */}
      <span className={`relative ${symbolDimensions} shrink-0 flex items-center justify-center transition-transform group-hover:scale-105 duration-200`}>
        <svg
          viewBox="0 0 100 80"
          fill="none"
          xmlns="http://www.w3.org/2000/svg"
          className="w-full h-full drop-shadow-sm"
        >
          <defs>
            <linearGradient id="logo-ribbon-grad" x1="0%" y1="0%" x2="100%" y2="100%">
              <stop offset="0%" stopColor="var(--color-content-primary)" />
              <stop offset="50%" stopColor="var(--color-text-secondary)" />
              <stop offset="100%" stopColor="var(--color-text-muted)" />
            </linearGradient>

            <linearGradient id="logo-pillar-grad" x1="0%" y1="0%" x2="0%" y2="100%">
              <stop offset="0%" stopColor="var(--color-content-primary)" />
              <stop offset="100%" stopColor="var(--color-text-secondary)" />
            </linearGradient>

            <linearGradient id="logo-fold-shadow" x1="0%" y1="0%" x2="100%" y2="80%">
              <stop offset="0%" stopColor="var(--color-bg-base)" stopOpacity="0.4" />
              <stop offset="70%" stopColor="var(--color-bg-base)" stopOpacity="0.1" />
              <stop offset="100%" stopColor="var(--color-bg-base)" stopOpacity="0" />
            </linearGradient>
          </defs>

          {/* Sol Alttaki İkonik Turkuaz Nokta */}
          <circle cx="14" cy="65" r="9.5" fill="var(--color-namines-teal-vibrant)" />

          {/* Katlanmış N Gövdesi */}
          <g>
            {/* Çapraz Şerit + Sağ Alt Yuvarlak Uç */}
            <path
              d="M 32 30
                 L 70 68
                 A 10 10 0 0 0 86 60
                 L 48 22
                 A 10 10 0 0 0 32 30 Z"
              fill="url(#logo-ribbon-grad)"
            />

            {/* Sol Dikey Sütun + Üst Kıvrım */}
            <path
              d="M 28 28
                 C 28 14, 40 4, 55 4
                 C 64 4, 72 8, 77 15
                 L 64 28
                 C 61 24, 57 21, 52 21
                 C 45 21, 41 26, 41 33
                 L 41 64
                 A 6.5 6.5 0 0 1 28 64
                 Z"
              fill="url(#logo-ribbon-grad)"
            />

            {/* İç Katlanma Gölgesi */}
            <path
              d="M 41 30
                 C 41 24, 45 21, 52 21
                 L 64 28
                 L 44 48
                 C 42 43, 41 37, 41 30 Z"
              fill="url(#logo-fold-shadow)"
            />

            {/* Sağ Bağımsız Dikey Sütun (Hap Şekli) */}
            <path
              d="M 80 5
                 A 8.5 8.5 0 0 1 97 5
                 L 97 45
                 A 8.5 8.5 0 0 1 80 45
                 Z"
              fill="url(#logo-pillar-grad)"
              transform="rotate(-4 88.5 25)"
            />
          </g>
        </svg>
      </span>

      {/* Geometrik Wordmark: N Λ M I N E S */}
      {showWordmark && (
        <span
          className={`font-mono text-content-primary group-hover:text-content-primary-hover transition-colors ${textSize} flex items-center select-none`}
        >
          <span>N</span>
          <span className="mx-[0.08em] font-sans">Λ</span>
          <span>MINES</span>
        </span>
      )}
    </span>
  );
}
