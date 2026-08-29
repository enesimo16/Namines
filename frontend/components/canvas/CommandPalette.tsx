'use client';

import React, { useEffect, useRef, useState } from 'react';
import {
  CheckCircle, Database, GitBranch, History, LayoutTemplate,
  Link2, Pencil, Search, Terminal, Upload, X,
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

export default function CommandPalette({ isOpen, onClose, actions }: Props) {
  const [query, setQuery] = useState('');
  const [activeIdx, setActiveIdx] = useState(0);
  const inputRef = useRef<HTMLInputElement>(null);
  const listRef  = useRef<HTMLDivElement>(null);

  // Focus input when opened
  useEffect(() => {
    if (isOpen) {
      setQuery('');
      setActiveIdx(0);
      setTimeout(() => inputRef.current?.focus(), 50);
    }
  }, [isOpen]);

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

  // Scroll active item into view — early return'den ÖNCE tanımlanmalı: hook'lar
  // her render'da aynı sırada çağrılmalı, "isOpen=false" durumunda erken dönüş
  // bu iki effect'i atlatıp render'lar arası hook sayısını değiştiriyordu
  // ("Rendered more hooks than during the previous render" çökmesi).
  useEffect(() => {
    const list = listRef.current;
    if (!list) return;
    const item = list.querySelector(`[data-idx="${activeIdx}"]`) as HTMLElement | null;
    item?.scrollIntoView({ block: 'nearest' });
  }, [activeIdx]);

  // Reset active idx when filter changes
  useEffect(() => { setActiveIdx(0); }, [query]);

  if (!isOpen) return null;

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
        className="bg-surface-800 border border-surface-500 rounded-2xl shadow-2xl w-full max-w-lg mx-4 overflow-hidden"
        onClick={e => e.stopPropagation()}
        onKeyDown={handleKeyDown}
      >
        {/* Search bar */}
        <div className="flex items-center gap-3 px-4 py-3 border-b border-surface-600">
          <Search className="w-4 h-4 text-content-muted shrink-0" />
          <input
            ref={inputRef}
            value={query}
            onChange={e => setQuery(e.target.value)}
            placeholder="Search commands…"
            className="flex-1 bg-transparent text-content-primary placeholder-content-muted text-sm outline-none"
          />
          <kbd className="px-1.5 py-0.5 rounded bg-surface-700 border border-surface-500 text-content-muted text-xs font-mono">ESC</kbd>
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
              <span className={`w-8 h-8 flex items-center justify-center rounded-lg shrink-0 ${
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
          <span><kbd className="font-mono bg-surface-700 px-1 rounded">↑↓</kbd> navigate</span>
          <span><kbd className="font-mono bg-surface-700 px-1 rounded">↵</kbd> run</span>
          <span className="ml-auto">ESC to close</span>
        </div>
      </div>
    </div>
  );
}
