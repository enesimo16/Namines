'use client';

import type { AppSlug } from '../../lib/appsCatalog';

export default function AppBrandMark({
  slug,
  className = 'w-5 h-5',
}: {
  slug: AppSlug;
  className?: string;
}) {
  return (
    <svg
      viewBox="0 0 100 100"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      aria-hidden="true"
      className={className}
    >
      <path
        d="M62 16H34C24.06 16 16 24.06 16 34V66C16 75.94 24.06 84 34 84H66C75.94 84 84 75.94 84 66V38"
        stroke="currentColor"
        strokeWidth="8"
        strokeLinecap="round"
      />
      <circle cx="80" cy="20" r="7.5" fill="#00F5C4" />

      {slug === 'desk' && (
        <>
          <rect x="31" y="33" width="16" height="15" rx="4" fill="#6366F1" />
          <rect x="52" y="33" width="16" height="15" rx="4" fill="currentColor" />
          <rect x="31" y="53" width="16" height="15" rx="4" fill="currentColor" />
          <rect x="52" y="53" width="16" height="15" rx="4" fill="#00F5C4" />
        </>
      )}

      {slug === 'vault' && (
        <>
          <path
            d="M40 46V40C40 34.48 44.48 30 50 30C55.52 30 60 34.48 60 40V46"
            stroke="#6366F1"
            strokeWidth="6.5"
            strokeLinecap="round"
          />
          <rect x="33" y="45" width="34" height="24" rx="7" fill="currentColor" />
          <circle cx="50" cy="57" r="4.5" fill="#00F5C4" />
        </>
      )}

      {slug === 'ground' && (
        <>
          <rect x="31" y="50" width="10" height="19" rx="5" fill="#6366F1" />
          <rect x="45" y="40" width="10" height="29" rx="5" fill="currentColor" />
          <rect x="59" y="32" width="10" height="37" rx="5" fill="#00F5C4" />
        </>
      )}

      {slug === 'flow' && (
        <>
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
        </>
      )}
    </svg>
  );
}
