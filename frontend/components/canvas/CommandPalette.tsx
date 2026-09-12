'use client';

import React, { useEffect, useRef, useState } from 'react';
import {
  Search
} from 'lucide-react';

export interface PaletteAction {
  id: string;
  label: string;
  description?: string;
  icon: React.ReactNode;
  keywords?: string[];
  onSelect: () => void;
}

interface Props {
  isOpen: boolean;
  onClose: () => void;
  actions: PaletteAction[];
}

/**
 * Kapaliyken HIC MONTE ETMEME kabugu.
 *
 * **Neden bu bicim:** icerideki bilesen acilista durumunu sifirlamak icin
 * `useEffect(() => { setQuery(''); ... }, [isOpen])` kullaniyordu. Bu iki
 * sorun uretiyordu: (1) React once ONCEKI aramayi bir kare gosteriyordu,
 * (2) `set-state-in-effect` kurali hakli olarak sasirtici zincir render
 * uyarisi veriyordu.
 *
 * Kapaliyken bilesen artik hic monte edilmiyor; acildiginda TAZE monte
 * oluyor ve `useState` baslangic degerleri sifirlamayi zaten yapiyor.
 * React'in "durumu sifirlamak icin bileseni yeniden monte et" onerisi bu.
 *
 * Kabukta kanca YOK, o yuzden kosullu `return null` kurallara uygun.
 */
export default function CommandPalette({ isOpen, onClose, actions }: Props) {
  if (!isOpen) return null;
  return <CommandPalettePanel onClose={onClose} actions={actions} />;
}

function CommandPalettePanel({ onClose, actions }: { onClose: () => void; actions: PaletteAction[] }) {
  const [query, setQuery] = useState('');
  const [activeIdx, setActiveIdx] = useState(0);
  const inputRef = useRef<HTMLInputElement>(null);
  const listRef  = useRef<HTMLDivElement>(null);

  // Gecikme kasitli: modal gecisi sirasinda odak vermek kaydirmayi bozuyor.
  useEffect(() => {
    const timer = setTimeout(() => inputRef.current?.focus(), 50);
    return () => clearTimeout(timer);
  }, []);

  const filtered = query.trim()
    ? actions.filter(a => {
        const q = query.toLowerCase();
        return (
          a.label.toLowerCase().includes(q) ||
          a.description?.toLowerCase().includes(q) ||
          a.keywords?.some(k => k.toLowerCase().includes(q))
        );
      })
    : actions;

  // Seçili satırı görünür tut.
  //
  // Eskiden bu dosyada "erken dönüşten ÖNCE tanımlanmalı" notu vardı: bileşen
  // kapalıyken de monte kalıyordu ve `if (!isOpen) return null` hook'ları
  // atlayınca React çöküyordu. Artık kapalıyken hiç monte edilmiyor (yukarıdaki
  // kabuk), yani o tuzak ortadan kalktı; bu effect sırf okunabilirlik için
  // burada duruyor.
  useEffect(() => {
    const list = listRef.current;
    if (!list) return;
    const item = list.querySelector(`[data-idx="${activeIdx}"]`) as HTMLElement | null;
    item?.scrollIntoView({ block: 'nearest' });
  }, [activeIdx]);

  /**
   * Filtre degisince secim ilk siraya doner.
   *
   * **Efekt DEGIL, olay isleyicisinin isi:** sorgu yalnizca kullanici yazinca
   * degisiyor, yani sifirlama o olayin bir parcasi. Efektle yapildiginda React
   * once yanlis `activeIdx` ile bir kere render ediyordu (liste kisaldiysa
   * gecersiz bir satir isaretli goruluyordu), sonra ikinci render'da
   * duzeliyordu. `setQuery`nin yaninda cagirmak bu ara kareyi yok ediyor.
   */
  const updateQuery = (value: string) => {
    setQuery(value);
    setActiveIdx(0);
  };

  const select = (action: PaletteAction) => {
    onClose();
    action.onSelect();
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'ArrowDown')  { e.preventDefault(); setActiveIdx(i => Math.min(i + 1, filtered.length - 1)); }
    if (e.key === 'ArrowUp')    { e.preventDefault(); setActiveIdx(i => Math.max(i - 1, 0)); }
    if (e.key === 'Enter' && filtered[activeIdx]) { e.preventDefault(); select(filtered[activeIdx]); }
    if (e.key === 'Escape')     { onClose(); }
  };

  return (
    <div
      className="fixed inset-0 z-[300] flex items-start justify-center pt-[15vh] bg-scrim/60 backdrop-blur-sm"
      onClick={onClose}
    >
      <div
        className="bg-surface-800 border border-surface-500 rounded-[var(--radius-modal)] shadow-2xl w-full max-w-lg mx-4 overflow-hidden"
        onClick={e => e.stopPropagation()}
        onKeyDown={handleKeyDown}
      >
        {/* Search bar */}
        <div className="flex items-center gap-3 px-4 py-3 border-b border-surface-600">
          <Search className="w-4 h-4 text-content-muted shrink-0" />
          <input
            aria-label="Search commands"
            ref={inputRef}
            value={query}
            onChange={e => updateQuery(e.target.value)}
            placeholder="Search commands…"
            className="flex-1 bg-transparent text-content-primary placeholder-content-muted text-sm outline-none"
          />
          <kbd className="px-1.5 py-0.5 rounded-[var(--radius-control)] bg-surface-700 border border-surface-500 text-content-muted text-xs font-mono">ESC</kbd>
        </div>

        {/* Actions list */}
        <div ref={listRef} className="overflow-y-auto" style={{ maxHeight: '60vh' }}>
          {filtered.length === 0 && (
            <div className="px-4 py-8 text-center text-content-muted text-sm">No commands match.</div>
          )}
          {filtered.map((action, idx) => (
            <button
              key={action.id}
              data-idx={idx}
              onClick={() => select(action)}
              onMouseEnter={() => setActiveIdx(idx)}
              className={`w-full flex items-center gap-3 px-4 py-2.5 text-left transition-colors ${
                idx === activeIdx ? 'bg-white/[0.08]' : 'hover:bg-surface-700'
              }`}
            >
              <span className={`w-8 h-8 flex items-center justify-center rounded-[var(--radius-control)] shrink-0 ${
                idx === activeIdx ? 'bg-content-primary/30 text-content-primary' : 'bg-surface-700 text-content-muted'
              }`}>
                {action.icon}
              </span>
              <div className="min-w-0">
                <p className="text-content-primary text-sm font-medium truncate">{action.label}</p>
                {action.description && (
                  <p className="text-content-muted text-xs truncate">{action.description}</p>
                )}
              </div>
            </button>
          ))}
        </div>

        {/* Footer hint */}
        <div className="flex items-center gap-3 px-4 py-2 border-t border-surface-600 text-content-muted text-xs">
          <span><kbd className="font-mono bg-surface-700 px-1 rounded-[var(--radius-control)]">↑↓</kbd> navigate</span>
          <span><kbd className="font-mono bg-surface-700 px-1 rounded-[var(--radius-control)]">↵</kbd> run</span>
          <span className="ml-auto">ESC to close</span>
        </div>
      </div>
    </div>
  );
}
