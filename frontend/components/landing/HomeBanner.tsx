'use client';

import { useState } from 'react';
import Link from 'next/link';
import { ArrowRight, X } from 'lucide-react';

/**
 * Tanıtım sayfasının en üstündeki promosyon şeridi (render.com'un "Migrating
 * production infrastructure?" şeridinden ilhamla).
 *
 * <b>Aşağı kaydırınca neden JS'siz kayboluyor:</b> bu bileşen normal akışta
 * (sticky DEĞİL) render ediliyor, altındaki <header> ise sticky top-0. Sayfa
 * kaydırıldığında bu şerit doğal olarak görünüm dışına çıkıyor, sticky
 * header ise yerinde kalıyor — `Header.tsx`'in kendisi bunu `isHome` dalında
 * bu bileşenden ÖNCE render ederek sağlıyor. Scroll event dinleyicisi YOK;
 * tarayıcının kendi kaydırma davranışı yeterli.
 *
 * <b>Uydurma rakam yok:</b> Render'ın "$10K migration credits" gibi somut
 * bir teklifimiz yok, o yüzden metin gerçek bir yeteneğe (Desk'in barındırılan
 * paneli) işaret ediyor — icat edilmiş bir kampanya değil.
 */
export default function HomeBanner() {
  const [dismissed, setDismissed] = useState(false);
  if (dismissed) return null;

  return (
    <div
      className="relative flex items-center justify-center gap-3 px-4 py-2 text-center text-xs sm:text-sm font-medium text-content-primary"
      style={{
        background: 'linear-gradient(90deg, var(--accent-subtle), var(--accent), var(--accent-hover))',
      }}
    >
      <span className="truncate">
        Namines Desk (beta) is live — a hosted admin panel generated straight from your schema.
      </span>
      <Link
        href="/new"
        className="inline-flex items-center gap-1 shrink-0 px-2.5 py-1 rounded-full bg-surface-900/80 hover:bg-surface-900 text-content-primary text-xs font-bold transition-colors"
      >
        Try it now
        <ArrowRight className="w-3 h-3" />
      </Link>
      <button
        onClick={() => setDismissed(true)}
        aria-label="Dismiss"
        className="absolute right-2 sm:right-3 p-1 text-content-primary/70 hover:text-content-primary transition-colors"
      >
        <X className="w-3.5 h-3.5" />
      </button>
    </div>
  );
}
