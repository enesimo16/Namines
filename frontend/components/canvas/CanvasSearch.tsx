'use client';

import { useEffect, useRef, useState } from 'react';
import { Search, X } from 'lucide-react';
import { useReactFlow } from '@xyflow/react';
import { useSchemaStore } from '../../store/useSchemaStore';

interface Props {
  isOpen: boolean;
  onClose: () => void;
}

export default function CanvasSearch({ isOpen, onClose }: Props) {
  const [query, setQuery] = useState('');
  const [matchIdx, setMatchIdx] = useState(0);
  const inputRef = useRef<HTMLInputElement>(null);
  const { fitBounds, getNode } = useReactFlow();
  const schema = useSchemaStore(s => s.schema);

  useEffect(() => {
    if (isOpen) {
      setQuery('');
      setMatchIdx(0);
      setTimeout(() => inputRef.current?.focus(), 50);
    }
  }, [isOpen]);

  if (!isOpen) return null;

  const tables = schema?.tables ?? [];
  const q = query.trim().toLowerCase();
  const matches = q
    ? tables.filter(t =>
        t.name.toLowerCase().includes(q) ||
        t.columns.some(c => c.name.toLowerCase().includes(q))
      )
    : [];

  const zoomToMatch = (idx: number) => {
    const table = matches[idx];
    if (!table) return;
    const node = getNode(table.id);
    if (!node) return;
    fitBounds(
      { x: node.position.x, y: node.position.y, width: 320, height: 200 },
      { duration: 400, padding: 0.4 }
    );
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Escape') { onClose(); return; }
    if (e.key === 'Enter') {
      if (matches.length === 0) return;
      const next = e.shiftKey
        ? (matchIdx - 1 + matches.length) % matches.length
        : (matchIdx + 1) % matches.length;
      setMatchIdx(next);
      zoomToMatch(next);
    }
  };

  const handleQueryChange = (v: string) => {
    setQuery(v);
    setMatchIdx(0);
    // Zoom to first match immediately
    const q2 = v.trim().toLowerCase();
    if (!q2) return;
    const first = tables.find(t =>
      t.name.toLowerCase().includes(q2) ||
      t.columns.some(c => c.name.toLowerCase().includes(q2))
    );
    if (!first) return;
    const node = getNode(first.id);
    if (!node) return;
    fitBounds(
      { x: node.position.x, y: node.position.y, width: 320, height: 200 },
      { duration: 300, padding: 0.4 }
    );
  };

  return (
    <div className="absolute top-4 left-1/2 -translate-x-1/2 z-[200] pointer-events-auto">
      <div className="flex items-center gap-2 bg-surface-800/95 backdrop-blur-md border border-accent-hover/40 rounded-[var(--radius-card)] px-3 py-2 shadow-[0_8px_30px_color-mix(in srgb, var(--color-scrim) 60%, transparent)] w-80">
        <Search className="w-4 h-4 text-content-muted shrink-0" />
        <input
          ref={inputRef}
          value={query}
          onChange={e => handleQueryChange(e.target.value)}
          onKeyDown={handleKeyDown}
          placeholder="Tablo veya sütun ara…"
          className="flex-1 bg-transparent text-content-primary placeholder-content-muted text-sm outline-none"
        />
        {matches.length > 0 && (
          <span className="text-content-muted text-xs shrink-0">
            {matchIdx + 1}/{matches.length}
          </span>
        )}
        {q && matches.length === 0 && (
          <span className="text-danger-text text-xs shrink-0">Bulunamadı</span>
        )}
        <button onClick={onClose} className="text-content-muted hover:text-content-primary transition-colors">
          <X className="w-4 h-4" />
        </button>
      </div>
      {matches.length > 0 && (
        <p className="text-center text-content-muted text-[10px] mt-1.5">
          Enter → sonraki · Shift+Enter → önceki
        </p>
      )}
    </div>
  );
}
