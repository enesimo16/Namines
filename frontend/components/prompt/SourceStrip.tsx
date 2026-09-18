'use client';

import React, { useEffect, useRef, useState } from 'react';
import {
  LayoutTemplate, FolderGit2, Database, FileCode2, Link as LinkIcon,
  Braces, Image as ImageIcon, Plus, Sparkles, ChevronRight,
} from 'lucide-react';
import type { SchemaSourceDescriptor } from '../../types/source';

interface SourceStripProps {
  sources: SchemaSourceDescriptor[];
  onSelect: (id: string) => void;
  disabled?: boolean;
}

/** Şeritte kart olarak duran kaynak sayısı; kalanı "+N" altına giriyor. */
const VISIBLE = 4;

/**
 * Kaynağın ikonu.
 *
 * <b>Katalogdan gelmiyor, çünkü ikon bir SUNUM kararı</b> — sunucuya ikon adı
 * koymak, görsel dili API sözleşmesine taşımak olurdu. Tanınmayan kimlik
 * genel bir ikona düşüyor: yeni bir kaynak eklendiğinde ikonsuz kalmıyor.
 */
const ICONS: Record<string, React.ComponentType<{ className?: string }>> = {
  starter: LayoutTemplate,
  github: FolderGit2,
  dbconnect: Database,
  code: FileCode2,
  openapi: LinkIcon,
  jsonshape: Braces,
  image: ImageIcon,
};

/**
 * Kartın üstünde tek satırlık tanıtım.
 *
 * <b>Sunucunun açıklaması kartta GÖRÜNÜYOR, `title` özniteliğinde değil.</b>
 * Tarayıcının kendi ipucu kutusu hem geç açılıyor hem de tasarımın dışında bir
 * siyah dikdörtgen olarak beliriyordu; dokunmatik ekranda ise hiç açılmıyor.
 * Kartın işe yarayıp yaramadığına karar vermek için gereken bilgi, karara
 * bakılan yerde durmalı.
 */
function SourceCard({
  source, onClick, disabled,
}: { source: SchemaSourceDescriptor; onClick: () => void; disabled?: boolean }) {
  const Icon = ICONS[source.id] ?? Plus;

  return (
    <button
      type="button"
      disabled={disabled}
      onClick={onClick}
      className="group relative flex-1 basis-0 min-w-[132px] flex flex-col gap-2 p-3 rounded-[var(--radius-card)] border border-line-strong bg-surface-800/60 text-left transition-all duration-150 hover:border-accent/40 hover:bg-surface-700/50 hover:-translate-y-0.5 focus-visible:border-accent/60 disabled:opacity-50 disabled:cursor-not-allowed disabled:hover:translate-y-0"
    >
      <span className="flex items-center justify-between">
        <span className="w-7 h-7 rounded-[var(--radius-control)] bg-surface-700/60 border border-line-strong flex items-center justify-center transition-colors group-hover:border-accent/40">
          <Icon className="w-3.5 h-3.5 text-content-secondary transition-colors group-hover:text-accent-text" />
        </span>
        {source.producesGuess && (
          <span className="text-micro px-1 py-px rounded-[var(--radius-control)] border border-line-strong text-content-muted">
            guess
          </span>
        )}
      </span>

      <span className="flex flex-col gap-0.5">
        <span className="text-xs font-medium text-content-primary">{source.displayName}</span>
        {/* İki satırda kesiliyor: kartların yüksekliği eşit kalmalı, yoksa
            şerit tırtıklı görünüyor. */}
        <span className="text-micro text-content-muted leading-snug line-clamp-2">
          {source.description}
        </span>
      </span>
    </button>
  );
}

/**
 * Prompt kutusunun ÜSTÜNDEKİ kaynak şeridi.
 *
 * <b>Neden panelin içinde değil:</b> menü prompt kutusunun içinde küçük bir
 * düğmeyken kimse aramıyordu — ürünün tek girişi bir cümle yazmak sanılıyordu.
 * Şerit, yazı yazmadan başlamanın mümkün olduğunu HİÇBİR TIKLAMA OLMADAN
 * gösteriyor.
 *
 * <b>Sıra sunucudan geliyor</b> (`GET /api/sources`): ilk dördü kart, kalanı
 * "+N". İstemcide ikinci bir sıralama tutmak, iki listenin ayrışması olurdu.
 */
export function SourceStrip({ sources, onSelect, disabled }: SourceStripProps) {
  const [open, setOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setOpen(false); };
    const onClick = (e: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener('keydown', onKey);
    document.addEventListener('mousedown', onClick);
    return () => {
      document.removeEventListener('keydown', onKey);
      document.removeEventListener('mousedown', onClick);
    };
  }, [open]);

  // Liste alınamadıysa boş bir şerit çizmek ürünü kırık gösterirdi; üretim
  // akışı kaynak menüsü olmadan da çalışıyor.
  if (sources.length === 0) return null;

  const visible = sources.slice(0, VISIBLE);
  const overflow = sources.slice(VISIBLE);

  const pick = (id: string) => { onSelect(id); setOpen(false); };

  return (
    <div className="w-full max-w-2xl mb-4 flex flex-col gap-2" ref={containerRef}>
      <span className="text-micro text-content-muted flex items-center gap-1.5 px-0.5">
        <Sparkles className="w-3 h-3" />
        Start without writing anything
      </span>

      {/* Açılır liste, kaydırılan kabın DIŞINDA duruyor.
          `overflow-x: auto` veren bir kap diğer ekseni de kırpar (CSS kuralı:
          bir eksen `visible` değilse öteki de `auto` olur), yani liste bu kabın
          içinde kalsaydı alt kısmı kesilirdi. */}
      <div className="relative">
        {/* Kartlar ESNEK (`flex-1 basis-0`): masaüstünde satırı tam dolduruyor,
            dar ekranda `min-w` sayesinde küçülmek yerine YATAY KAYIYOR. Sabit
            genişlikte kalsalardı masaüstünde "+N" kartı satırdan taşıp
            kesiliyordu; sarmak ise şeridi iki-üç satıra çıkarıp prompt
            kutusunu ekranın dışına itiyordu. */}
        <div className="flex items-stretch gap-2 overflow-x-auto pb-1">
          {visible.map(item => (
            <SourceCard key={item.id} source={item} disabled={disabled} onClick={() => pick(item.id)} />
          ))}

          {overflow.length > 0 && (
            <button
              type="button"
              disabled={disabled}
              aria-haspopup="menu"
              aria-expanded={open}
              onClick={() => setOpen(!open)}
              className="group shrink-0 w-[96px] flex flex-col justify-center gap-2 p-3 rounded-[var(--radius-card)] border border-dashed border-line-strong bg-surface-800/30 text-left transition-all duration-150 hover:border-accent/40 hover:bg-surface-700/40 hover:-translate-y-0.5 disabled:opacity-50"
            >
              <span className="w-7 h-7 rounded-[var(--radius-control)] bg-surface-700/60 border border-line-strong flex items-center justify-center transition-colors group-hover:border-accent/40">
                <Plus className="w-3.5 h-3.5 text-content-secondary transition-colors group-hover:text-accent-text" />
              </span>
              <span className="flex flex-col gap-0.5">
                <span className="text-xs font-medium text-content-primary">{overflow.length} more</span>
                <span className="text-micro text-content-muted">ways to start</span>
              </span>
            </button>
          )}
        </div>

        {open && (
          <div
            role="menu"
            className="absolute right-0 top-full mt-2 w-[300px] max-w-[calc(100vw-2rem)] rounded-[var(--radius-card)] border border-line-strong bg-surface-800/98 backdrop-blur-xl p-1.5 shadow-2xl z-50 flex flex-col gap-0.5 animate-dropdown-in"
          >
            {overflow.map(item => {
              const Icon = ICONS[item.id] ?? Plus;
              return (
                <button
                  key={item.id}
                  type="button"
                  role="menuitem"
                  onClick={() => pick(item.id)}
                  className="group flex items-start gap-2.5 px-2 py-2 rounded-[var(--radius-control)] text-left hover:bg-surface-700/50 transition-colors"
                >
                  <span className="w-6 h-6 mt-0.5 shrink-0 rounded-[var(--radius-control)] bg-surface-700/60 border border-line-strong flex items-center justify-center">
                    <Icon className="w-3 h-3 text-content-secondary transition-colors group-hover:text-accent-text" />
                  </span>
                  <span className="flex flex-col gap-0.5 min-w-0">
                    <span className="flex items-center gap-1.5">
                      <span className="text-sm text-content-primary">{item.displayName}</span>
                      {item.producesGuess && (
                        <span className="text-micro px-1 py-px rounded-[var(--radius-control)] border border-line-strong text-content-muted">
                          guess
                        </span>
                      )}
                    </span>
                    <span className="text-micro text-content-muted leading-snug">{item.description}</span>
                  </span>
                  <ChevronRight className="w-3.5 h-3.5 mt-1 shrink-0 text-content-muted opacity-0 transition-opacity group-hover:opacity-100" />
                </button>
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
}
