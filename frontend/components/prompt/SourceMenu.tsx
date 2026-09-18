'use client';

import React, { useEffect, useRef, useState } from 'react';
import { Plus } from 'lucide-react';
import type { SchemaSourceDescriptor } from '../../types/source';

interface SourceMenuProps {
  sources: SchemaSourceDescriptor[];
  onSelect: (id: string) => void;
  disabled?: boolean;
}

/**
 * Grup başlıkları ve sıraları.
 *
 * **"Connect" ile "Import" ayrımı kozmetik değil, bir SÖZ:** bağlanan kaynak
 * sürekli bir ilişkidir ve arkadan drift takip edilebilir; içe aktarılan tek
 * seferliktir. Kullanıcı bu farkı menüde görmezse sonradan "neden drift
 * bildirimi almıyorum" diye sorar (github/06-EKLENTI-MIMARISI.md §4).
 */
const GROUPS: { kind: string; title: string }[] = [
  { kind: 'connect', title: 'Connect' },
  { kind: 'import', title: 'Import' },
  { kind: 'starter', title: 'Start from' },
];

/**
 * Prompt kutusunun "+ Add source" menüsü.
 *
 * **Kaynakları ADIYLA tanımıyor.** Ne gösterileceği, hangi grupta duracağı ve
 * "guess" rozetinin çıkıp çıkmayacağı sunucudan gelen tanımlayıcıdan okunuyor;
 * yeni bir kaynak eklendiğinde burada değiştirilecek bir şey olmaması gerekiyor.
 */
export function SourceMenu({ sources, onSelect, disabled }: SourceMenuProps) {
  const [open, setOpen] = useState(false);
  const [maxHeight, setMaxHeight] = useState<number | null>(null);
  const containerRef = useRef<HTMLDivElement>(null);

  // Menü YUKARI açılıyor (aşağısı üretim düğmeleriyle dolu), dolayısıyla
  // düğmenin üstündeki boşluktan uzun olamaz. Telefonda (375×812) altı
  // kaynaklık liste tam da bunu yapıyordu: ilk grup ekranın üstünde kalıyor
  // ve ERİŞİLEMEZ oluyordu — canlı doğrulamada görüldü, CSS'le çözülemiyor
  // çünkü boşluğun ne kadar olduğunu yalnızca ölçüm biliyor.
  useEffect(() => {
    if (!open) return;
    const top = containerRef.current?.getBoundingClientRect().top ?? 0;
    // jsdom ve ilk kare gibi ölçümün anlamsız olduğu durumlarda sınır
    // KOYULMUYOR: 0'a yakın bir tavan menüyü tamamen görünmez yapardı.
    setMaxHeight(top > 80 ? top - 16 : null);
  }, [open]);

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

  // Tanınmayan bir `kind` DÜŞÜRÜLMEZ, "Import" altına alınır: sunucu yeni bir
  // grup eklediğinde kaynağın menüden kaybolması, yanlış grupta görünmesinden
  // daha kötü — ve "import" hiçbir süreklilik vaat etmeyen en muhafazakâr grup.
  const groupOf = (kind: string) => (GROUPS.some(g => g.kind === kind) ? kind : 'import');

  return (
    <div className="relative" ref={containerRef}>
      <button
        type="button"
        disabled={disabled}
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => setOpen(!open)}
        className="flex items-center gap-1.5 h-7 px-2 rounded-[var(--radius-control)] glass-button text-content-muted hover:text-content-primary transition-all disabled:opacity-50 disabled:cursor-not-allowed"
      >
        <Plus className="w-3.5 h-3.5" />
        <span className="text-xs font-medium">Add source</span>
      </button>

      {open && (
        <div
          role="menu"
          style={maxHeight ? { maxHeight } : undefined}
          className="absolute left-0 bottom-full mb-2 w-[300px] max-w-[calc(100vw-2rem)] overflow-y-auto rounded-[var(--radius-card)] border border-line-strong bg-surface-800/98 backdrop-blur-xl p-2 shadow-2xl z-50 flex flex-col gap-1 animate-dropdown-in"
        >
          {GROUPS.map(group => {
            const items = sources.filter(s => groupOf(s.kind) === group.kind);
            if (items.length === 0) return null;
            return (
              <div key={group.kind} className="flex flex-col">
                <span className="px-2 pt-1.5 pb-1 text-[10px] uppercase tracking-wide text-content-muted">
                  {group.title}
                </span>
                {items.map(source => (
                  <button
                    key={source.id}
                    type="button"
                    role="menuitem"
                    onClick={() => { onSelect(source.id); setOpen(false); }}
                    className="flex flex-col items-start gap-0.5 px-2 py-1.5 rounded-[var(--radius-control)] text-left hover:bg-surface-700/50 transition-colors"
                  >
                    <span className="flex items-center gap-1.5">
                      <span className="text-sm text-content-primary">{source.displayName}</span>
                      {/* Rozet katalogdan geliyor; menünün hangi kaynağın çıkarım
                          yaptığını ayrıca bilmesi gerekmiyor — bu kuralın her
                          ekranda elle hatırlanması gereken bir disiplin olmaktan
                          çıkması için sunucuya taşındı. */}
                      {source.producesGuess && (
                        <span className="text-micro px-1 py-px rounded-[var(--radius-control)] border border-line-strong text-content-muted">
                          guess
                        </span>
                      )}
                    </span>
                    <span className="text-[11px] text-content-muted">{source.description}</span>
                  </button>
                ))}
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
