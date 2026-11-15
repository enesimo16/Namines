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
      {/* Ortak U3-B Açık Köşe Çerçevesi + Mint Çekirdek Düğüm */}
      <path
        d="M62 16H34C24.06 16 16 24.06 16 34V66C16 75.94 24.06 84 34 84H66C75.94 84 84 75.94 84 66V38"
        stroke="currentColor"
        strokeWidth="8"
        strokeLinecap="round"
      />
      <circle cx="80" cy="20" r="7.5" fill="#00F5C4" />

      {/* 1. Namines (Katmanlı Veri Akışı) */}
      {module === 'namines' && (
        <g>
          <rect x="30" y="32" width="38" height="10" rx="5" fill="#6366F1" />
          <rect x="30" y="47.5" width="24" height="10" rx="5" fill="currentColor" />
          <rect x="30" y="63" width="38" height="7" rx="3.5" fill="#00F5C4" />
        </g>
      )}

      {/* 2. Diagram (İlişkisel Şema Düğümleri) */}
      {module === 'diagram' && (
        <g>
          <path
            d="M44 40H56C60 40 60 60 56 60"
            stroke="currentColor"
            strokeWidth="6"
            strokeLinecap="round"
          />
          <rect x="29" y="32" width="18" height="16" rx="4.5" fill="#6366F1" />
          <rect x="44" y="52" width="22" height="16" rx="4.5" fill="#00F5C4" />
        </g>
      )}

      {/* 3. Build (Kod & Derleyici Köşebentleri) */}
      {module === 'build' && (
        <g>
          <path
            d="M42 36L30 50L42 64"
            stroke="#6366F1"
            strokeWidth="7"
            strokeLinecap="round"
            strokeLinejoin="round"
          />
          <path
            d="M58 36L70 50L58 64"
            stroke="#00F5C4"
            strokeWidth="7"
            strokeLinecap="round"
            strokeLinejoin="round"
          />
          <line x1="53" y1="34" x2="47" y2="66" stroke="currentColor" strokeWidth="6" strokeLinecap="round" />
        </g>
      )}

      {/* 4. Flow (Olay Güdümlü S-Eğrisi Akışı) */}
      {module === 'flow' && (
        <g>
          <path
            d="M35 63C35 49 65 51 65 37"
            stroke="currentColor"
            strokeWidth="6.5"
            strokeLinecap="round"
          />
          <path
            d="M35 37V51"
            stroke="#6366F1"
            strokeWidth="6.5"
            strokeLinecap="round"
          />
          <circle cx="35" cy="37" r="6.5" fill="#6366F1" />
          <circle cx="35" cy="63" r="6.5" fill="currentColor" />
          <circle cx="65" cy="37" r="6.5" fill="#00F5C4" />
        </g>
      )}

      {/* 5. Ground (Yönetilen Postgres & Telemetri Sütunları) */}
      {module === 'ground' && (
        <g>
          <rect x="31" y="50" width="10" height="19" rx="5" fill="#6366F1" />
          <rect x="45" y="40" width="10" height="29" rx="5" fill="currentColor" />
          <rect x="59" y="32" width="10" height="37" rx="5" fill="#00F5C4" />
        </g>
      )}

      {/* 6. Vault (Şifreli Kasa & Kilit) */}
      {module === 'vault' && (
        <g>
          <path
            d="M40 46V40C40 34.48 44.48 30 50 30C55.52 30 60 34.48 60 40V46"
            stroke="#6366F1"
            strokeWidth="6.5"
            strokeLinecap="round"
          />
          <rect x="33" y="45" width="34" height="24" rx="7" fill="currentColor" />
          <circle cx="50" cy="57" r="4.5" fill="#00F5C4" />
        </g>
      )}

      {/* 7. Desk (2x2 Veri Tablosu Izgarası) */}
      {module === 'desk' && (
        <g>
          <rect x="31" y="33" width="16" height="15" rx="4" fill="#6366F1" />
          <rect x="52" y="33" width="16" height="15" rx="4" fill="currentColor" />
          <rect x="31" y="53" width="16" height="15" rx="4" fill="currentColor" />
          <rect x="52" y="53" width="16" height="15" rx="4" fill="#00F5C4" />
        </g>
      )}

      {/* 8. Hub (Dal & Birleştirme Ağı) */}
      {module === 'hub' && (
        <g>
          <line x1="36" y1="34" x2="36" y2="66" stroke="currentColor" strokeWidth="6.5" strokeLinecap="round" />
          <path
            d="M36 58C36 47 62 50 62 40"
            stroke="#6366F1"
            strokeWidth="6.5"
            strokeLinecap="round"
          />
          <circle cx="36" cy="36" r="6" fill="#6366F1" />
          <circle cx="36" cy="64" r="6" fill="currentColor" />
          <circle cx="62" cy="40" r="6" fill="#00F5C4" />
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
