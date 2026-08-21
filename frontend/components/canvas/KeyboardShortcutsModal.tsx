'use client';

import React from 'react';
import { X, Keyboard } from 'lucide-react';

interface Props {
  isOpen: boolean;
  onClose: () => void;
}

const SECTIONS = [
  {
    title: 'Canvas',
    shortcuts: [
      { keys: ['Ctrl', 'F'], label: 'Search tables / columns' },
      { keys: ['Ctrl', 'Z'], label: 'Undo' },
      { keys: ['Ctrl', 'Shift', 'Z'], label: 'Redo' },
      { keys: ['Ctrl', 'K'], label: 'Open command palette' },
      { keys: ['?'], label: 'Open this shortcuts modal' },
    ],
  },
  {
    title: 'Table Node',
    shortcuts: [
      { keys: ['Double-click'], label: 'Open table actions popover' },
      { keys: ['Enter', 'Space'], label: 'Open table actions popover (keyboard)' },
      { keys: ['↑ ↓ ← →'], label: 'Nudge table position (10px)' },
      { keys: ['Delete', 'Backspace'], label: 'Delete selected table (edit mode)' },
    ],
  },
  {
    title: 'Canvas Search',
    shortcuts: [
      { keys: ['Enter'], label: 'Next match' },
      { keys: ['Shift', 'Enter'], label: 'Previous match' },
      { keys: ['Esc'], label: 'Close search' },
    ],
  },
  {
    title: 'Command Palette',
    shortcuts: [
      { keys: ['↑ ↓'], label: 'Navigate actions' },
      { keys: ['Enter'], label: 'Run selected action' },
      { keys: ['Esc'], label: 'Close palette' },
    ],
  },
];

export default function KeyboardShortcutsModal({ isOpen, onClose }: Props) {
  if (!isOpen) return null;

  return (
    <div
      className="fixed inset-0 z-[300] flex items-center justify-center bg-black/60 backdrop-blur-sm"
      onClick={onClose}
    >
      <div
        className="bg-surface-800 border border-surface-500 rounded-2xl shadow-2xl w-full max-w-xl mx-4 flex flex-col animate-in fade-in zoom-in-95 duration-150"
        style={{ maxHeight: '80vh' }}
        onClick={e => e.stopPropagation()}
      >
        <div className="flex items-center justify-between px-6 pt-5 pb-4 border-b border-surface-600">
          <div className="flex items-center gap-2">
            <Keyboard className="w-5 h-5 text-accent-text" />
            <span className="text-content-primary font-semibold text-base">Keyboard Shortcuts</span>
          </div>
          <button onClick={onClose} className="text-content-muted hover:text-content-primary transition-colors cursor-pointer">
            <X className="w-5 h-5" />
          </button>
        </div>

        <div className="overflow-y-auto px-6 py-5 flex flex-col gap-6">
          {SECTIONS.map(section => (
            <div key={section.title}>
              <p className="text-xs font-bold uppercase tracking-wider text-accent-text mb-3">{section.title}</p>
              <div className="flex flex-col gap-1.5">
                {section.shortcuts.map(s => (
                  <div key={s.label} className="flex items-center justify-between gap-4">
                    <span className="text-sm text-content-secondary">{s.label}</span>
                    <div className="flex items-center gap-1 shrink-0">
                      {s.keys.map(k => (
                        <kbd
                          key={k}
                          className="px-2 py-0.5 rounded-md bg-surface-600 border border-surface-400 text-content-primary text-xs font-mono font-semibold shadow-[0_1px_0_rgba(0,0,0,0.4)]"
                        >
                          {k}
                        </kbd>
                      ))}
                    </div>
                  </div>
                ))}
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
