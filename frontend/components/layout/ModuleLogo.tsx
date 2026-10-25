'use client';

import React from 'react';

export type ModuleKind =
  | 'namines'
  | 'diagram'
  | 'build'
  | 'flow'
  | 'ground'
  | 'vault'
  | 'desk'
  | 'hub';

interface ModuleLogoProps {
  module: ModuleKind;
  size?: 'xs' | 'sm' | 'default' | 'lg' | 'xl';
  showWordmark?: boolean;
  className?: string;
  badge?: boolean;
}

const SIZE_MAP = {
  xs: { icon: 16, text: 'text-[11px]' },
  sm: { icon: 18, text: 'text-xs' },
  default: { icon: 22, text: 'text-sm' },
  lg: { icon: 30, text: 'text-base' },
  xl: { icon: 38, text: 'text-lg' },
};

const MODULE_META: Record<
  ModuleKind,
  {
    name: string;
    sub: string;
    code: string;
    tagline: string;
  }
> = {
  namines: {
    name: 'NAMINES',
    sub: 'PLATFORM',
    code: '.n',
    tagline: 'Database Lifecycle Platform',
  },
  diagram: {
    name: 'NAMINES',
    sub: 'DIAGRAM',
    code: 'Diagram',
    tagline: 'Relational Schema Visualizer',
  },
  build: {
    name: 'NAMINES',
    sub: 'BUILD',
    code: 'Build',
    tagline: 'Scaffolding & Migration Compiler',
  },
  flow: {
    name: 'NAMINES',
    sub: 'FLOW',
    code: '.nf',
    tagline: 'Event & Workflow Engine',
  },
  ground: {
    name: 'NAMINES',
    sub: 'GROUND',
    code: '.ng',
    tagline: 'Managed DB Fleet & Sandboxes',
  },
  vault: {
    name: 'NAMINES',
    sub: 'VAULT',
    code: '.nv',
    tagline: 'Zero-Knowledge PITR & Secrets',
  },
  desk: {
    name: 'NAMINES',
    sub: 'DESK',
    code: '.nd',
    tagline: 'Data Studio & SQL Console',
  },
  hub: {
    name: 'NAMINES',
    sub: 'HUB',
    code: '.nh',
    tagline: 'Database Change Repositories',
  },
};

/**
 * Namines Vektörel Modül Sembolü (Transparent, Theme-Adaptive, Crisp 100x100 ViewBox)
 * İkonik Namines (.n) marka DNA'sını temel alan monografik çift harf (.nf, .ng, .nv, .nd, .nh, .nb, .ndg).
 * Arka planı tamamen şeffaftır (siyah kutu YOKTUR), açık ve koyu temalarda metin rengine (currentColor) ve canlı teal noktaya uyum sağlar.
 */
export function ModuleSymbol({
  module,
  size = 18,
  className = '',
}: {
  module: ModuleKind;
  size?: number | string;
  className?: string;
}) {
  const meta = MODULE_META[module];

  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 100 100"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      className={`shrink-0 select-none ${className}`}
      role="img"
      aria-label={`${meta.name} ${meta.sub} Symbol`}
    >
      {/* 1. Namines (.n) */}
      {module === 'namines' && (
        <g>
          <circle cx="16" cy="74" r="7" fill="var(--namines-teal-vibrant)" />
          <path
            d="M36 74V48C36 38 43 32 55 32C67 32 74 38 74 48V74"
            stroke="currentColor"
            strokeWidth="8"
            strokeLinecap="round"
            strokeLinejoin="round"
          />
        </g>
      )}

      {/* 2. Diagram (Namines İç Çekirdek — İlişkisel Şema Düğümleri) */}
      {module === 'diagram' && (
        <g stroke="currentColor" strokeWidth="6" strokeLinecap="round" strokeLinejoin="round">
          <rect x="18" y="22" width="28" height="24" rx="5" />
          <line x1="18" y1="33" x2="46" y2="33" stroke="var(--namines-teal-vibrant)" strokeWidth="5" />
          <rect x="54" y="54" width="28" height="24" rx="5" />
          <line x1="54" y1="65" x2="82" y2="65" stroke="var(--namines-teal-vibrant)" strokeWidth="5" />
          <path d="M46 33H58C64 33 64 54 54 54" stroke="var(--namines-teal-vibrant)" strokeWidth="5" />
        </g>
      )}

      {/* 3. Build (Namines İç Çekirdek — Derleyici Çekiç & Kıvılcım) */}
      {module === 'build' && (
        <g stroke="currentColor" strokeWidth="6" strokeLinecap="round" strokeLinejoin="round">
          <path d="M52 24L74 46L64 56L42 34L52 24Z" fill="currentColor" fillOpacity="0.15" />
          <path d="M42 34L22 54C19 57 19 62 22 65C25 68 30 68 33 65L53 45" />
          <path d="M76 18L79 26L87 29L79 32L76 40L73 32L65 29L73 26Z" fill="var(--namines-teal-vibrant)" stroke="none" />
        </g>
      )}

      {/* 4. Flow (.nf / Dynamic event surge) */}
      {module === 'flow' && (
        <g>
          <circle cx="12" cy="74" r="6" fill="var(--namines-teal-vibrant)" />
          <path
            d="M26 74V48C26 40 31 35 38 35C45 35 50 40 50 48V74"
            stroke="currentColor"
            strokeWidth="7.5"
            strokeLinecap="round"
            strokeLinejoin="round"
          />
          <path
            d="M66 74V30C66 22 72 17 82 17"
            stroke="currentColor"
            strokeWidth="7.5"
            strokeLinecap="round"
            strokeLinejoin="round"
          />
          <path
            d="M58 43H84"
            stroke="var(--namines-teal-vibrant)"
            strokeWidth="7.5"
            strokeLinecap="round"
          />
        </g>
      )}

      {/* 5. Ground (.ng / Database container fleet) */}
      {module === 'ground' && (
        <g>
          <circle cx="12" cy="74" r="6" fill="var(--namines-teal-vibrant)" />
          <path
            d="M26 74V48C26 40 31 35 38 35C45 35 50 40 50 48V74"
            stroke="currentColor"
            strokeWidth="7.5"
            strokeLinecap="round"
            strokeLinejoin="round"
          />
          <path
            d="M86 48C86 39 79 35 71 35C61 35 56 42 56 54C56 65 61 72 71 72C79 72 86 67 86 58"
            stroke="currentColor"
            strokeWidth="7.5"
            strokeLinecap="round"
            strokeLinejoin="round"
          />
          <path
            d="M86 37V74C86 86 78 92 68 92C59 92 56 87 56 87"
            stroke="var(--namines-teal-vibrant)"
            strokeWidth="7.5"
            strokeLinecap="round"
            strokeLinejoin="round"
          />
        </g>
      )}

      {/* 6. Vault (.nv / Cryptographic shield) */}
      {module === 'vault' && (
        <g>
          <circle cx="12" cy="74" r="6" fill="var(--namines-teal-vibrant)" />
          <path
            d="M26 74V48C26 40 31 35 38 35C45 35 50 40 50 48V74"
            stroke="currentColor"
            strokeWidth="7.5"
            strokeLinecap="round"
            strokeLinejoin="round"
          />
          <path
            d="M60 36L74 74L88 36"
            stroke="currentColor"
            strokeWidth="7.5"
            strokeLinecap="round"
            strokeLinejoin="round"
          />
          <circle cx="74" cy="48" r="4" fill="var(--namines-teal-vibrant)" />
        </g>
      )}

      {/* 7. Desk (.nd / Relational table column) */}
      {module === 'desk' && (
        <g>
          <circle cx="12" cy="74" r="6" fill="var(--namines-teal-vibrant)" />
          <path
            d="M26 74V48C26 40 31 35 38 35C45 35 50 40 50 48V74"
            stroke="currentColor"
            strokeWidth="7.5"
            strokeLinecap="round"
            strokeLinejoin="round"
          />
          <path
            d="M84 58C84 67 77 72 69 72C59 72 54 65 54 54C54 43 59 35 69 35C77 35 84 40 84 49"
            stroke="currentColor"
            strokeWidth="7.5"
            strokeLinecap="round"
            strokeLinejoin="round"
          />
          <path
            d="M84 18V74"
            stroke="var(--namines-teal-vibrant)"
            strokeWidth="7.5"
            strokeLinecap="round"
          />
        </g>
      )}

      {/* 8. Hub (.nh / Git branch nexus) */}
      {module === 'hub' && (
        <g>
          <circle cx="12" cy="74" r="6" fill="var(--namines-teal-vibrant)" />
          <path
            d="M26 74V48C26 40 31 35 38 35C45 35 50 40 50 48V74"
            stroke="currentColor"
            strokeWidth="7.5"
            strokeLinecap="round"
            strokeLinejoin="round"
          />
          <path
            d="M62 18V74"
            stroke="currentColor"
            strokeWidth="7.5"
            strokeLinecap="round"
            strokeLinejoin="round"
          />
          <path
            d="M62 48C62 39 68 35 76 35C84 35 88 40 88 48V74"
            stroke="currentColor"
            strokeWidth="7.5"
            strokeLinecap="round"
            strokeLinejoin="round"
          />
          <circle cx="88" cy="48" r="4" fill="var(--namines-teal-vibrant)" />
        </g>
      )}
    </svg>
  );
}

/**
 * Namines Stilinde Vektörel Modül Tipografisi (Stylized Geometric Wordmark)
 * Namines'in geometrik N Λ M I N E S marka yazı tipine özdeş (Lambda A, keskin açılar, geniş tracking).
 * Tamamen şeffaftır (arkaplan yoktur), açık/koyu temaya göre currentColor rengini alır.
 */
export function ModuleWordmark({
  module,
  height = 13,
  className = '',
}: {
  module: ModuleKind;
  height?: number | string;
  className?: string;
}) {
  if (module === 'vault') {
    return (
      <svg
        height={height}
        viewBox="0 0 154 32"
        fill="none"
        xmlns="http://www.w3.org/2000/svg"
        className={`inline-block shrink-0 align-middle select-none ${className}`}
        role="img"
        aria-label="VAULT"
      >
        <g stroke="currentColor" strokeWidth="3.8" strokeLinecap="round" strokeLinejoin="round">
          {/* V */}
          <path d="M4 6L17 26L30 6" />
          {/* Λ (Lambda A - Namines tarzı) */}
          <path d="M40 26L53 6L66 26" />
          {/* U */}
          <path d="M76 6V19C76 24 80 26 86 26C92 26 96 24 96 19V6" />
          {/* L */}
          <path d="M106 6V26H122" />
          {/* T */}
          <path d="M131 6H149M140 6V26" />
        </g>
      </svg>
    );
  }

  if (module === 'flow') {
    return (
      <svg
        height={height}
        viewBox="0 0 126 32"
        fill="none"
        xmlns="http://www.w3.org/2000/svg"
        className={`inline-block shrink-0 align-middle select-none ${className}`}
        role="img"
        aria-label="FLOW"
      >
        <g stroke="currentColor" strokeWidth="3.8" strokeLinecap="round" strokeLinejoin="round">
          {/* F */}
          <path d="M4 6V26M4 6H22M4 16H18" />
          {/* L */}
          <path d="M31 6V26H47" />
          {/* O */}
          <path d="M66 6C58 6 54 10.5 54 16C54 21.5 58 26 66 26C74 26 78 21.5 78 16C78 10.5 74 6 66 6Z" />
          {/* W */}
          <path d="M87 6L95 26L104 11L113 26L121 6" />
        </g>
      </svg>
    );
  }

  if (module === 'ground') {
    return (
      <svg
        height={height}
        viewBox="0 0 184 32"
        fill="none"
        xmlns="http://www.w3.org/2000/svg"
        className={`inline-block shrink-0 align-middle select-none ${className}`}
        role="img"
        aria-label="GROUND"
      >
        <g stroke="currentColor" strokeWidth="3.8" strokeLinecap="round" strokeLinejoin="round">
          {/* G */}
          <path d="M26 11C24 7.5 19 6 15 6C7 6 4 10.5 4 16C4 21.5 7 26 15 26C22 26 26 21 26 17H15" />
          {/* R */}
          <path d="M35 6V26M35 6H48C54 6 56 8.5 56 13C56 16.5 53 18 48 18H35M47 18L56 26" />
          {/* O */}
          <path d="M75 6C67 6 63 10.5 63 16C63 21.5 67 26 75 26C83 26 87 21.5 87 16C87 10.5 83 6 75 6Z" />
          {/* U */}
          <path d="M97 6V19C97 24 101 26 107 26C113 26 117 24 117 19V6" />
          {/* N */}
          <path d="M127 26V6L147 26V6" />
          {/* D */}
          <path d="M157 6V26M157 6H167C176 6 180 10.5 180 16C180 21.5 176 26 167 26H157" />
        </g>
      </svg>
    );
  }

  if (module === 'desk') {
    return (
      <svg
        height={height}
        viewBox="0 0 108 32"
        fill="none"
        xmlns="http://www.w3.org/2000/svg"
        className={`inline-block shrink-0 align-middle select-none ${className}`}
        role="img"
        aria-label="DESK"
      >
        <g stroke="currentColor" strokeWidth="3.8" strokeLinecap="round" strokeLinejoin="round">
          {/* D */}
          <path d="M4 6V26M4 6H14C23 6 27 10.5 27 16C27 21.5 23 26 14 26H4" />
          {/* E */}
          <path d="M36 6V26M36 6H52M36 16H48M36 26H52" />
          {/* S */}
          <path d="M76 11C74 7.5 70 6 65 6C59 6 56 9 56 13C56 17 60 18.5 66 19.5C72 20.5 76 22 76 26C76 30 71 32 65 32C59 32 55 28 55 24" />
          {/* K */}
          <path d="M86 6V26M103 6L88 17L104 26" />
        </g>
      </svg>
    );
  }

  if (module === 'hub') {
    return (
      <svg
        height={height}
        viewBox="0 0 86 32"
        fill="none"
        xmlns="http://www.w3.org/2000/svg"
        className={`inline-block shrink-0 align-middle select-none ${className}`}
        role="img"
        aria-label="HUB"
      >
        <g stroke="currentColor" strokeWidth="3.8" strokeLinecap="round" strokeLinejoin="round">
          {/* H */}
          <path d="M4 6V26M22 6V26M4 16H22" />
          {/* U */}
          <path d="M31 6V19C31 24 35 26 41 26C47 26 51 24 51 19V6" />
          {/* B */}
          <path d="M61 6V26M61 6H73C79 6 81 8.5 81 12.5C81 15.5 79 16.5 73 16.5M61 16.5H74C80 16.5 82 19 82 23C82 26 79 26 73 26H61" />
        </g>
      </svg>
    );
  }

  return (
    <span className={`font-mono font-bold uppercase tracking-widest text-xs ${className}`}>
      {module}
    </span>
  );
}

/**
 * Tam Modül Logosu (Sembol + Tipografi Wordmark)
 */
export default function ModuleLogo({
  module,
  size = 'default',
  showWordmark = true,
  className = '',
  badge = false,
}: ModuleLogoProps) {
  const meta = MODULE_META[module];
  const conf = SIZE_MAP[size];

  return (
    <div className={`inline-flex items-center gap-2 select-none ${className}`}>
      {badge ? (
        <div className="shrink-0 flex items-center justify-center p-1.5 rounded-[var(--radius-control)] bg-surface-800 border border-line-strong shadow-xs">
          <ModuleSymbol module={module} size={conf.icon} />
        </div>
      ) : (
        <ModuleSymbol module={module} size={conf.icon} />
      )}

      {showWordmark && (
        <div className="flex flex-col justify-center leading-none">
          <div className="flex items-center gap-1.5 font-sans font-black tracking-wider text-content-primary">
            <span className={conf.text}>{meta.name}</span>
            <span className={`${conf.text} font-bold text-accent-text tracking-widest`}>
              {meta.sub}
            </span>
          </div>
          {size !== 'xs' && (
            <span className="text-[10px] font-mono text-content-muted tracking-tight mt-0.5">
              {meta.tagline}
            </span>
          )}
        </div>
      )}
    </div>
  );
}
