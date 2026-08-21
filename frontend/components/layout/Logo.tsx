'use client';

import { Database } from 'lucide-react';

/**
 * Minimalist marka işareti. Eski gradyanlı/yıldızlı SVG logo kaldırıldı —
 * bkz. FRONTEND.md §1 (işlevsel minimalizm) ve kullanıcının "logoyu tamamen
 * değiştir/kaldırabilirsin" talimatı.
 */
export default function Logo({ size = 'default' }: { size?: 'default' | 'sm' }) {
  const box = size === 'sm' ? 'w-6 h-6' : 'w-7 h-7';
  const icon = size === 'sm' ? 'w-3.5 h-3.5' : 'w-4 h-4';
  const text = size === 'sm' ? 'text-sm' : 'text-base';

  return (
    <span className="inline-flex items-center gap-2">
      <span className={`${box} rounded-lg bg-surface-600 border border-content-primary/15 flex items-center justify-center shrink-0`}>
        <Database className={`${icon} text-accent-text`} strokeWidth={2} />
      </span>
      <span className={`font-mono font-bold tracking-widest ${text} text-content-primary`}>
        NAMINES
      </span>
    </span>
  );
}
