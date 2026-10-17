'use client';

import React, { useEffect, useMemo, useState } from 'react';
import { X, Keyboard, Search } from 'lucide-react';
import { useEscapeKey } from '../../hooks/useEscapeKey';
import { formatShortcutKey, isMac } from '../../lib/shortcuts';

interface Props {
  isOpen: boolean;
  onClose: () => void;
}

interface ShortcutItem {
  keys: string[];
  label: string;
}

interface ShortcutSection {
  title: string;
  shortcuts: ShortcutItem[];
}

const SECTIONS: ShortcutSection[] = [
  {
    title: 'Global & Application Shell',
    shortcuts: [
      { keys: ['mod', 'K'], label: 'Open Command Palette' },
      { keys: ['?'], label: 'Open this keyboard shortcuts modal' },
      { keys: ['mod', '/'], label: 'Open shortcuts modal (alternative)' },
      { keys: ['mod', ','], label: 'Open account and AI settings' },
      { keys: ['Esc'], label: 'Close active dialog, drawer, or modal' },
    ],
  },
  {
    title: 'Quick Navigation (Press G then letter)',
    shortcuts: [
      { keys: ['G', 'D'], label: 'Go to Canvas Diagram' },
      { keys: ['G', 'C'], label: 'Go to Studio Compile & Export' },
      { keys: ['G', 'F'], label: 'Go to Flow Automation' },
      { keys: ['G', 'R'], label: 'Go to Review Console' },
      { keys: ['G', 'G'], label: 'Go to Ground Hosted DBs' },
      { keys: ['G', 'V'], label: 'Go to Vault Backups' },
      { keys: ['G', 'K'], label: 'Open Desk Backoffice' },
    ],
  },
  {
    title: 'Canvas Diagram',
    shortcuts: [
      { keys: ['mod', 'F'], label: 'Search tables and columns' },
      { keys: ['mod', 'Z'], label: 'Undo last change' },
      { keys: ['mod', 'Shift', 'Z'], label: 'Redo undone change' },
      { keys: ['T'], label: 'Insert new table onto canvas' },
      { keys: ['mod', 'E'], label: 'Toggle Edit / View mode' },
    ],
  },
  {
    title: 'Table Node Interactions',
    shortcuts: [
      { keys: ['Double-click'], label: 'Open table column details drawer' },
      { keys: ['Enter', 'Space'], label: 'Open table actions popover (keyboard)' },
      { keys: ['↑', '↓', '←', '→'], label: 'Nudge table position (10px)' },
      { keys: ['Delete', 'Backspace'], label: 'Delete selected table (edit mode)' },
    ],
  },
  {
    title: 'Review Console & Forms',
    shortcuts: [
      { keys: ['mod', 'Enter'], label: 'Approve change request / Confirm' },
      { keys: ['Esc'], label: 'Dismiss review or reset search' },
    ],
  },
];

export default function KeyboardShortcutsModal({ isOpen, onClose }: Props) {
  useEscapeKey(isOpen, onClose);
  const [query, setQuery] = useState('');
  const isApple = isMac();

  // Reset query on open
  useEffect(() => {
    if (isOpen) setQuery('');
  }, [isOpen]);

  const filteredSections = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return SECTIONS;

    return SECTIONS.map(sec => ({
      ...sec,
      shortcuts: sec.shortcuts.filter(
        s => s.label.toLowerCase().includes(q) || s.keys.some(k => k.toLowerCase().includes(q))
      ),
    })).filter(sec => sec.shortcuts.length > 0);
  }, [query]);

  if (!isOpen) return null;

  return (
    <div
      className="fixed inset-0 z-modal flex items-center justify-center bg-scrim/60 backdrop-blur-sm animate-in fade-in duration-150 p-4"
      onClick={onClose}
      role="dialog"
      aria-modal="true"
      aria-label="Keyboard Shortcuts"
    >
      <div
        className="bg-surface-800 border border-line-strong rounded-[var(--radius-modal)] shadow-2xl w-full max-w-xl mx-4 flex flex-col overflow-hidden max-h-[85vh] animate-in zoom-in-95 duration-150"
        onClick={e => e.stopPropagation()}
      >
        {/* Header */}
        <div className="flex items-center justify-between px-6 pt-5 pb-4 border-b border-line-subtle bg-surface-800">
          <div className="flex items-center gap-2.5">
            <span className="w-8 h-8 rounded-[var(--radius-control)] bg-surface-700 flex items-center justify-center text-accent-text border border-line-subtle">
              <Keyboard className="w-4 h-4" />
            </span>
            <div>
              <h2 className="text-content-primary font-semibold text-base">Keyboard Shortcuts</h2>
              <p className="text-xs text-content-muted">Quick hotkeys for fast navigation and editing</p>
            </div>
          </div>
          <button
            onClick={onClose}
            className="text-content-muted hover:text-content-primary p-1.5 rounded-[var(--radius-control)] hover:bg-surface-700 transition-colors cursor-pointer"
            aria-label="Close shortcuts modal"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Search filter inside modal */}
        <div className="px-6 py-3 border-b border-line-subtle bg-surface-850 flex items-center gap-2.5">
          <Search className="w-4 h-4 text-content-muted shrink-0" />
          <input
            type="text"
            value={query}
            onChange={e => setQuery(e.target.value)}
            placeholder="Filter shortcuts..."
            className="flex-1 bg-transparent text-sm text-content-primary placeholder:text-content-muted outline-none border-none"
            autoFocus
          />
          {query && (
            <button
              onClick={() => setQuery('')}
              className="text-xs text-content-muted hover:text-content-primary"
            >
              Clear
            </button>
          )}
        </div>

        {/* Content list */}
        <div className="overflow-y-auto px-6 py-4 flex flex-col gap-6">
          {filteredSections.length === 0 ? (
            <div className="text-center py-8 text-sm text-content-muted">
              No shortcuts found matching &quot;{query}&quot;
            </div>
          ) : (
            filteredSections.map(section => (
              <div key={section.title}>
                <p className="text-[11px] font-bold uppercase tracking-wider text-content-muted mb-2.5">
                  {section.title}
                </p>
                <div className="flex flex-col gap-1.5 bg-surface-850/60 p-2.5 rounded-[var(--radius-control)] border border-line-subtle">
                  {section.shortcuts.map(s => (
                    <div
                      key={s.label}
                      className="flex items-center justify-between gap-4 py-1.5 px-2 rounded-[var(--radius-control)] hover:bg-surface-700/50 transition-colors"
                    >
                      <span className="text-xs sm:text-sm text-content-secondary">{s.label}</span>
                      <div className="flex items-center gap-1 shrink-0">
                        {s.keys.map((k, i) => (
                          <kbd
                            key={`${s.label}-${k}-${i}`}
                            className="px-2 py-0.5 rounded-[var(--radius-control)] bg-surface-700 border border-line-strong text-content-primary text-xs font-mono font-semibold shadow-sm"
                          >
                            {formatShortcutKey(k, isApple)}
                          </kbd>
                        ))}
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            ))
          )}
        </div>

        {/* Footer */}
        <div className="flex items-center justify-between px-6 py-3 border-t border-line-subtle bg-surface-800 text-xs text-content-muted">
          <span>Press <kbd className="font-mono bg-surface-700 border border-line-strong px-1.5 py-0.5 rounded-[var(--radius-control)] text-[10px]">Esc</kbd> anytime to dismiss</span>
          <button
            onClick={onClose}
            className="px-3 py-1 bg-surface-700 hover:bg-surface-600 text-content-primary rounded-[var(--radius-control)] text-xs font-medium transition-colors cursor-pointer"
          >
            Got it
          </button>
        </div>
      </div>
    </div>
  );
}
