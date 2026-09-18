'use client';

import React, { useEffect, useRef, useState } from 'react';
import {
  LayoutTemplate, FolderGit2, Database, FileCode2, Link as LinkIcon,
  Braces, Image as ImageIcon, Plus, Sparkles,
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
    <div className="w-full max-w-2xl mb-3 flex flex-col gap-1.5" ref={containerRef}>
      <span className="text-micro text-content-muted flex items-center gap-1.5 px-0.5">
        <Sparkles className="w-3 h-3" />
        Start without writing anything
      </span>

      {/* Açılır liste, kaydırılan kabın DIŞINDA duruyor.
          `overflow-x: auto` veren bir kap diğer ekseni de kırpar (CSS kuralı:
          bir eksen `visible` değilse öteki de `auto` olur), yani liste bu kabın
          içinde kalsaydı alt kısmı kesilirdi. */}
      <div className="relative">
        {/* Dar ekranda sarmak yerine YATAY KAYIYOR: sarmak şeridi iki-üç satıra
            çıkarıp prompt kutusunu ekranın dışına itiyordu. */}
        <div className="flex items-stretch gap-2 overflow-x-auto pb-1">
        {visible.map(item => {
          const Icon = ICONS[item.id] ?? Plus;
          return (
            <button
              key={item.id}
              type="button"
              disabled={disabled}
              onClick={() => pick(item.id)}
              title={item.description}
              className="group shrink-0 min-w-[124px] flex flex-col items-start gap-1.5 px-3 py-2.5 rounded-[var(--radius-card)] glass-button text-left transition-all hover:bg-surface-700/50 hover:-translate-y-px disabled:opacity-50 disabled:cursor-not-allowed"
            >
              <Icon className="w-4 h-4 text-content-secondary group-hover:text-content-primary transition-colors" />
              <span className="flex items-center gap-1.5">
                <span className="text-xs font-medium text-content-primary">{item.displayName}</span>
                {item.producesGuess && (
                  <span className="text-micro px-1 py-px rounded-[var(--radius-control)] border border-line-strong text-content-muted">
                    guess
                  </span>
                )}
              </span>
            </button>
          );
        })}

        {overflow.length > 0 && (
          <button
            type="button"
            disabled={disabled}
            aria-haspopup="menu"
            aria-expanded={open}
            onClick={() => setOpen(!open)}
            className="shrink-0 min-w-[84px] flex flex-col items-start justify-center gap-1.5 px-3 py-2.5 rounded-[var(--radius-card)] glass-button text-left transition-all hover:bg-surface-700/50 hover:-translate-y-px disabled:opacity-50"
          >
            <Plus className="w-4 h-4 text-content-secondary" />
            <span className="text-xs font-medium text-content-primary">{overflow.length} more</span>
          </button>
        )}

        </div>

        {open && (
          <div
            role="menu"
            className="absolute right-0 top-full mt-2 w-[280px] max-w-[calc(100vw-2rem)] rounded-[var(--radius-card)] border border-line-strong bg-surface-800/98 backdrop-blur-xl p-2 shadow-2xl z-50 flex flex-col gap-1 animate-dropdown-in"
          >
            {overflow.map(item => {
              const Icon = ICONS[item.id] ?? Plus;
              return (
                <button
                  key={item.id}
                  type="button"
                  role="menuitem"
                  onClick={() => pick(item.id)}
                  className="flex items-start gap-2 px-2 py-1.5 rounded-[var(--radius-control)] text-left hover:bg-surface-700/50 transition-colors"
                >
                  <Icon className="w-3.5 h-3.5 mt-0.5 shrink-0 text-content-muted" />
                  <span className="flex flex-col">
                    <span className="flex items-center gap-1.5">
                      <span className="text-sm text-content-primary">{item.displayName}</span>
                      {item.producesGuess && (
                        <span className="text-micro px-1 py-px rounded-[var(--radius-control)] border border-line-strong text-content-muted">
                          guess
                        </span>
                      )}
                    </span>
                    <span className="text-micro text-content-muted">{item.description}</span>
                  </span>
                </button>
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
}
