'use client';

import React, { useEffect, useRef, useState } from 'react';
import {
  LayoutTemplate, FolderGit2, Database, FileCode2, Link as LinkIcon,
  Braces, Image as ImageIcon, ChevronDown, Sparkles
} from 'lucide-react';
import type { SchemaSourceDescriptor } from '../../types/source';
import { useLocale, useT } from '../../lib/i18n';

interface SourceDropdownProps {
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

const ICONS: Record<string, React.ComponentType<{ className?: string }>> = {
  starter: LayoutTemplate,
  github: FolderGit2,
  dbconnect: Database,
  code: FileCode2,
  openapi: LinkIcon,
  jsonshape: Braces,
  image: ImageIcon,
};

export function SourceDropdown({ sources, onSelect, disabled }: SourceDropdownProps) {
  const [open, setOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);
  const locale = useLocale();
  const t = useT();

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false);
    };
    const onClick = (e: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        setOpen(false);
      }
    };
    document.addEventListener('keydown', onKey);
    document.addEventListener('mousedown', onClick);
    return () => {
      document.removeEventListener('keydown', onKey);
      document.removeEventListener('mousedown', onClick);
    };
  }, [open]);

  if (sources.length === 0) return null;

  const handlePick = (id: string) => {
    onSelect(id);
    setOpen(false);
  };

  const templatesGroup = sources.filter(s => s.id === 'starter');
  const codeGroup = sources.filter(s => s.id === 'github' || s.id === 'dbconnect' || s.id === 'code');
  const specsGroup = sources.filter(s => s.id === 'openapi' || s.id === 'jsonshape' || s.id === 'image');
  const otherGroup = sources.filter(s => !['starter', 'github', 'dbconnect', 'code', 'openapi', 'jsonshape', 'image'].includes(s.id));

  const renderItem = (source: SchemaSourceDescriptor) => {
    const Icon = ICONS[source.id] ?? Sparkles;
    const trItem = locale === 'tr' ? TR_SOURCES[source.id] : undefined;
    const title = trItem?.name || source.displayName;
    const description = trItem?.desc || source.description;

    return (
      <button
        key={source.id}
        type="button"
        disabled={disabled}
        onClick={() => handlePick(source.id)}
        className="group flex items-start gap-3 w-full p-2 rounded-[var(--radius-control)] hover:bg-surface-800 transition-colors text-left cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed"
      >
        <div className="w-7 h-7 rounded-[var(--radius-control)] bg-surface-800 border border-line flex items-center justify-center shrink-0 mt-0.5 group-hover:bg-surface-700 group-hover:border-line-strong transition-colors">
          <Icon className="w-3.5 h-3.5 text-content-secondary group-hover:text-content-primary transition-colors" />
        </div>
        <div className="flex-1 min-w-0">
          <div className="flex items-center justify-between gap-1">
            <span className="text-xs font-medium text-content-primary truncate group-hover:text-content-primary">
              {title}
            </span>
            {source.producesGuess && (
              <span className="text-[10px] font-mono uppercase px-1.5 py-0.5 rounded-full border border-line bg-surface-800/80 text-content-muted shrink-0">
                {t('sourceStrip.guess')}
              </span>
            )}
          </div>
          <span className="block text-[11px] text-content-muted leading-tight line-clamp-1 mt-0.5">
            {description}
          </span>
        </div>
      </button>
    );
  };

  return (
    <div className="relative flex-1 min-w-[150px]" ref={containerRef}>
      <button
        id="new-source-dropdown-btn"
        type="button"
        disabled={disabled}
        onClick={() => setOpen(!open)}
        className="flex items-center justify-between rounded-full border border-line-strong bg-surface-800/80 hover:border-line-subtle hover:bg-surface-700/80 px-4 py-2 text-xs font-mono font-medium text-content-primary cursor-pointer w-full text-left select-none disabled:opacity-50 disabled:cursor-not-allowed h-[42px] transition-colors shadow-sm"
        aria-haspopup="true"
        aria-expanded={open}
      >
        <div className="flex items-center gap-2 truncate">
          <LayoutTemplate className="w-3.5 h-3.5 text-content-secondary shrink-0" />
          <span className="truncate">{t('sourceStrip.templatesAndImport')}</span>
        </div>
        <ChevronDown className={`w-3.5 h-3.5 text-content-muted transition-transform duration-200 shrink-0 ml-1.5 ${open ? 'rotate-180' : ''}`} />
      </button>

      {open && (
        <div className="absolute left-0 bottom-full mb-2 w-[340px] sm:w-[370px] max-w-[calc(100vw-2rem)] max-h-[75vh] overflow-y-auto rounded-[var(--radius-card)] border border-line-strong bg-surface-900/98 backdrop-blur-2xl p-2 shadow-2xl z-50 flex flex-col gap-2 select-none animate-dropdown-in">
          {/* Header */}
          <div className="px-2 pt-1 pb-0.5 border-b border-line flex items-center justify-between">
            <span className="text-[10px] font-mono uppercase tracking-wider text-content-muted font-semibold">
              {t('sourceStrip.startWithoutWriting')}
            </span>
          </div>

          {/* Group 1: Templates */}
          {templatesGroup.length > 0 && (
            <div className="flex flex-col gap-0.5">
              <span className="text-[10px] font-mono uppercase tracking-wider text-content-muted px-2 py-0.5">
                {locale === 'tr' ? 'Mimari Şablonlar' : 'Templates'}
              </span>
              {templatesGroup.map(renderItem)}
            </div>
          )}

          {/* Group 2: Code & Live */}
          {codeGroup.length > 0 && (
            <div className="flex flex-col gap-0.5 pt-1 border-t border-line">
              <span className="text-[10px] font-mono uppercase tracking-wider text-content-muted px-2 py-0.5">
                {locale === 'tr' ? 'Canlı Bağlantı & Kod' : 'Live & Code'}
              </span>
              {codeGroup.map(renderItem)}
            </div>
          )}

          {/* Group 3: Specs & Visuals */}
          {specsGroup.length > 0 && (
            <div className="flex flex-col gap-0.5 pt-1 border-t border-line">
              <span className="text-[10px] font-mono uppercase tracking-wider text-content-muted px-2 py-0.5">
                {locale === 'tr' ? 'Spesifikasyon & Görsel' : 'Specs & Visuals'}
              </span>
              {specsGroup.map(renderItem)}
            </div>
          )}

          {/* Other / Custom sources */}
          {otherGroup.length > 0 && (
            <div className="flex flex-col gap-0.5 pt-1 border-t border-line">
              <span className="text-[10px] font-mono uppercase tracking-wider text-content-muted px-2 py-0.5">
                {locale === 'tr' ? 'Diğer Kaynaklar' : 'Other Sources'}
              </span>
              {otherGroup.map(renderItem)}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
