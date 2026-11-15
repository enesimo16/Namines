'use client';

import React from 'react';

interface LogoProps {
  size?: 'xs' | 'sm' | 'md' | 'default' | 'lg' | 'xl';
  showWordmark?: boolean;
  className?: string;
  variant?: 'light' | 'dark' | 'auto';
}

const SIZE_MAP = {
  xs: { box: 20, wordmarkClass: 'h-3' },
  sm: { box: 24, wordmarkClass: 'h-3.5' },
  md: { box: 28, wordmarkClass: 'h-4' },
  default: { box: 28, wordmarkClass: 'h-4' },
  lg: { box: 34, wordmarkClass: 'h-5' },
  xl: { box: 40, wordmarkClass: 'h-6' },
} as const;

/**
 * Namines Resmi Marka Logosu (U3-B Open-Corner Strata + Node + Orijinal Wordmark)
 */
export default function Logo({
  size = 'default',
  showWordmark = true,
  className = '',
  variant = 'auto',
}: LogoProps) {
  const { box, wordmarkClass } = SIZE_MAP[size] ?? SIZE_MAP.default;

  const markColorClass =
    variant === 'light'
      ? 'text-white'
      : variant === 'dark'
        ? 'text-slate-950'
        : 'text-content-primary';

  return (
    <span
      className={`inline-flex items-center gap-2.5 shrink-0 select-none group ${markColorClass} ${className}`}
    >
      <svg
        width={box}
        height={box}
        viewBox="0 0 100 100"
        fill="none"
        xmlns="http://www.w3.org/2000/svg"
        aria-hidden="true"
        className="shrink-0 transition-transform duration-200 group-hover:scale-[1.03]"
      >
        <path
          d="M62 16H34C24.06 16 16 24.06 16 34V66C16 75.94 24.06 84 34 84H66C75.94 84 84 75.94 84 66V38"
          stroke="currentColor"
          strokeWidth="8"
          strokeLinecap="round"
        />
        <circle cx="80" cy="20" r="7.5" fill="#00F5C4" />
        <rect x="30" y="32" width="38" height="10" rx="5" fill="#6366F1" />
        <rect x="30" y="47.5" width="24" height="10" rx="5" fill="currentColor" />
        <rect x="30" y="63" width="38" height="7" rx="3.5" fill="#00F5C4" />
      </svg>

      {showWordmark && (
        <>
          <img
            src="/brand/namines-wordmark-dark.png"
            alt="Namines"
            className={`theme-logo-dark ${wordmarkClass} w-auto max-w-none object-contain transition-transform duration-200 group-hover:scale-[1.02]`}
            loading="eager"
            decoding="async"
          />
          <img
            src="/brand/namines-wordmark-light.png"
            alt="Namines"
            className={`theme-logo-light ${wordmarkClass} w-auto max-w-none object-contain transition-transform duration-200 group-hover:scale-[1.02]`}
            loading="eager"
            decoding="async"
          />
        </>
      )}
    </span>
  );
}
