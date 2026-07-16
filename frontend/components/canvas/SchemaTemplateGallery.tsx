'use client';

import React, { useState } from 'react';
import { X, LayoutTemplate, Search } from 'lucide-react';
import { TEMPLATES } from '../../lib/templates';
import { useSchemaStore } from '../../store/useSchemaStore';
import { useToastStore } from '../../store/useToastStore';

interface Props {
  isOpen: boolean;
  onClose: () => void;
}

export default function SchemaTemplateGallery({ isOpen, onClose }: Props) {
  const loadFromSchema = useSchemaStore(s => s.loadFromSchema);
  const mergeFromSchema = useSchemaStore(s => s.mergeFromSchema);
  const showToast = useToastStore(s => s.showToast);
  const [query, setQuery] = useState('');

  if (!isOpen) return null;

  const filtered = query.trim()
    ? TEMPLATES.filter(t =>
        t.label.toLowerCase().includes(query.toLowerCase()) ||
        t.description.toLowerCase().includes(query.toLowerCase())
      )
    : TEMPLATES;

  const handleReplace = (key: string) => {
    const tpl = TEMPLATES.find(t => t.key === key);
    if (!tpl) return;
    loadFromSchema(tpl.schema);
    showToast(`"${tpl.label}" template loaded onto canvas.`, 'success');
    onClose();
  };

  const handleMerge = (key: string) => {
    const tpl = TEMPLATES.find(t => t.key === key);
    if (!tpl) return;
    mergeFromSchema(tpl.schema);
    showToast(`"${tpl.label}" tables merged into current schema.`, 'success');
    onClose();
  };

  return (
    <div className="fixed inset-0 z-[200] flex items-center justify-center bg-black/60 backdrop-blur-sm">
      <div className="bg-surface-800 border border-surface-500 rounded-2xl shadow-2xl w-full max-w-2xl mx-4 flex flex-col" style={{ maxHeight: '80vh' }}>

        {/* Header */}
        <div className="flex items-center justify-between px-6 pt-5 pb-4 border-b border-surface-600">
          <div className="flex items-center gap-2">
            <LayoutTemplate className="w-5 h-5 text-indigo-400" />
            <span className="text-content-primary font-semibold text-base">Schema Templates</span>
          </div>
          <button onClick={onClose} className="text-content-muted hover:text-content-primary transition-colors">
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Search */}
        <div className="px-6 pt-4 pb-3">
          <div className="relative">
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-content-muted" />
            <input
              autoFocus
              value={query}
              onChange={e => setQuery(e.target.value)}
              placeholder="Search templates…"
              className="w-full bg-surface-900 border border-surface-500 focus:border-indigo-500 rounded-xl pl-9 pr-4 py-2 text-sm text-content-primary placeholder-content-muted outline-none"
            />
          </div>
        </div>

        {/* Grid */}
        <div className="overflow-y-auto px-6 pb-6 grid grid-cols-1 sm:grid-cols-2 gap-3">
          {filtered.length === 0 && (
            <p className="col-span-2 text-center text-content-muted text-sm py-8">No templates match your search.</p>
          )}
          {filtered.map(tpl => (
            <div
              key={tpl.key}
              className="flex items-start gap-3 p-4 rounded-xl bg-surface-700 border border-surface-500 text-left transition-all"
            >
              <span className="text-2xl shrink-0 mt-0.5">{tpl.emoji}</span>
              <div className="flex-1 min-w-0">
                <p className="text-content-primary font-semibold text-sm">{tpl.label}</p>
                <p className="text-content-muted text-xs mt-0.5 leading-relaxed">{tpl.description}</p>
                <p className="text-indigo-500 text-xs mt-2 font-medium">
                  {tpl.schema.tables.length} tables · {tpl.schema.relations.length} relations
                </p>
                <div className="flex gap-2 mt-3">
                  <button
                    onClick={() => handleReplace(tpl.key)}
                    className="flex-1 py-1.5 rounded-lg bg-indigo-600 hover:bg-indigo-500 text-white text-xs font-bold transition-colors cursor-pointer"
                  >
                    Replace
                  </button>
                  <button
                    onClick={() => handleMerge(tpl.key)}
                    className="flex-1 py-1.5 rounded-lg bg-surface-600 hover:bg-surface-500 text-content-secondary hover:text-content-primary border border-surface-400 text-xs font-bold transition-colors cursor-pointer"
                  >
                    Merge into schema
                  </button>
                </div>
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
