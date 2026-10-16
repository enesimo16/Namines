'use client';

import React, { useEffect, useRef, useState } from 'react';
import {
  LayoutTemplate, FolderGit2, Database, FileCode2, Link as LinkIcon,
  Braces, Image as ImageIcon, Plus, Sparkles, ChevronRight,
} from 'lucide-react';
import type { SchemaSourceDescriptor } from '../../types/source';

import { useLocale, useT } from '../../lib/i18n';

interface SourceStripProps {
  sources: SchemaSourceDescriptor[];
  onSelect: (id: string) => void;
  disabled?: boolean;
}

const TR_SOURCES: Record<string, { name: string; desc: string }> = {
  starter: { name: 'Başlangıç şablonları', desc: 'SaaS, E-ticaret, CRM, Çok kiracılı' },
  github: { name: 'GitHub deposu', desc: 'Modeller ve sorgular için kodu tara' },
  dbconnect: { name: 'Veritabanı bağlantısı', desc: 'Canlı veritabanını tersine mühendislikle tara' },
  code: { name: 'Kod dosyaları', desc: 'Modelleri ve tipleri yapıştır veya yükle' },
  openapi: { name: 'OpenAPI / GraphQL URL', desc: 'Uç noktadan şema içe aktar' },
  jsonshape: { name: 'Örnek JSON yanıtı', desc: 'JSON verisinden tabloları çıkar' },
  image: { name: 'Görsel / Çizim', desc: 'Diyagram ekran görüntüsünden şema çıkar' },
};

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
  const locale = useLocale();
  const t = useT();

  const trItem = locale === 'tr' ? TR_SOURCES[source.id] : undefined;
  const title = trItem?.name || source.displayName;
  const description = trItem?.desc || source.description;

  return (
    <button
      type="button"
      disabled={disabled}
      onClick={onClick}
      className="group relative flex flex-col justify-between p-3.5 h-[104px] rounded-[var(--radius-card)] border border-line-strong bg-surface-900/90 text-left transition-all duration-150 hover:border-line-subtle hover:bg-surface-800 hover:-translate-y-0.5 focus-visible:outline-none disabled:opacity-50 disabled:cursor-not-allowed disabled:hover:translate-y-0 shadow-sm cursor-pointer"
    >
      <div className="flex items-center justify-between w-full">
        <div className="w-8 h-8 rounded-full bg-surface-800 border border-line flex items-center justify-center transition-colors group-hover:bg-surface-700 group-hover:border-line-strong">
          <Icon className="w-3.5 h-3.5 text-content-secondary transition-colors group-hover:text-content-primary" />
        </div>
        {source.producesGuess && (
          <span className="text-[10px] font-mono uppercase px-2 py-0.5 rounded-full border border-line bg-surface-800/80 text-content-muted">
            {t('sourceStrip.guess')}
          </span>
        )}
      </div>

      <div className="flex flex-col gap-0.5 mt-auto">
        <span className="text-xs font-semibold text-content-primary group-hover:text-content-primary transition-colors truncate">
          {title}
        </span>
        <span className="text-[11px] text-content-muted leading-tight line-clamp-1">
          {description}
        </span>
      </div>
    </button>
  );
}

/**
 * Prompt kutusunun ÜSTÜNDEKİ kaynak şeridi — Shopify Editions stili 5'li kart ızgarası.
 *
 * Sıra sunucudan geliyor (`GET /api/sources`): ilk dördü kart, kalanı "+N".
 */
export function SourceStrip({ sources, onSelect, disabled }: SourceStripProps) {
  const [open, setOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);
  const locale = useLocale();
  const t = useT();

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

  if (sources.length === 0) return null;

  const visible = sources.slice(0, VISIBLE);
  const overflow = sources.slice(VISIBLE);

  const pick = (id: string) => { onSelect(id); setOpen(false); };

  return (
    <div className="w-full max-w-4xl mb-5 flex flex-col gap-2.5" ref={containerRef}>
      <div className="flex items-center justify-between px-1">
        <span className="text-xs font-mono uppercase tracking-wider text-content-secondary flex items-center gap-1.5 font-medium">
          <Sparkles className="w-3.5 h-3.5 text-content-primary" />
          {t('sourceStrip.startWithoutWriting')}
        </span>
        <span className="text-[10px] font-mono text-content-muted hidden sm:inline uppercase tracking-wider">
          {t('sourceStrip.templatesQuickImports')}
        </span>
      </div>

      <div className="relative">
        <div className={`grid gap-2.5 ${overflow.length > 0 ? 'grid-cols-2 sm:grid-cols-5' : 'grid-cols-2 sm:grid-cols-4'}`}>
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
              className="group relative flex flex-col justify-between p-3.5 h-[104px] rounded-[var(--radius-card)] border border-line-strong bg-surface-900/60 hover:bg-surface-800/80 hover:border-line-subtle text-left transition-all duration-150 hover:-translate-y-0.5 disabled:opacity-50 shadow-sm cursor-pointer"
            >
              <div className="w-8 h-8 rounded-full bg-surface-800 border border-line flex items-center justify-center transition-colors group-hover:bg-surface-700 group-hover:border-line-strong">
                <Plus className="w-3.5 h-3.5 text-content-secondary transition-colors group-hover:text-content-primary" />
              </div>
              <div className="flex flex-col gap-0.5 mt-auto">
                <span className="text-xs font-semibold text-content-primary group-hover:text-content-primary transition-colors">
                  {t('sourceStrip.more', { count: overflow.length })}
                </span>
                <span className="text-[11px] text-content-muted leading-tight">
                  {locale === 'tr' ? 'başlama yolu' : 'ways to start'}
                </span>
              </div>
            </button>
          )}
        </div>

        {open && (
          <div
            role="menu"
            className="absolute right-0 top-full mt-2 w-[280px] max-w-[calc(100vw-2rem)] rounded-[var(--radius-card)] border border-line-strong bg-surface-900/98 backdrop-blur-2xl p-1.5 shadow-2xl z-50 flex flex-col gap-1 select-none animate-dropdown-in"
          >
            {overflow.map(item => {
              const Icon = ICONS[item.id] ?? Plus;
              return (
                <button
                  key={item.id}
                  type="button"
                  role="menuitem"
                  onClick={() => pick(item.id)}
                  className="group flex items-start gap-2.5 px-2.5 py-2 rounded-[var(--radius-control)] text-left hover:bg-surface-700/60 transition-colors cursor-pointer"
                >
                  <span className="w-7 h-7 mt-0.5 shrink-0 rounded-full bg-surface-800 border border-line-strong flex items-center justify-center">
                    <Icon className="w-3.5 h-3.5 text-content-secondary transition-colors group-hover:text-content-primary" />
                  </span>
                  <span className="flex flex-col gap-0.5 min-w-0 flex-1">
                    <span className="flex items-center gap-1.5">
                      <span className="text-xs font-medium text-content-primary">{item.displayName}</span>
                      {item.producesGuess && (
                        <span className="text-[10px] font-mono px-1.5 py-px rounded-full border border-line text-content-muted">
                          guess
                        </span>
                      )}
                    </span>
                    <span className="text-[10px] text-content-muted leading-snug">{item.description}</span>
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
