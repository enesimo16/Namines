'use client';

import React from 'react';
import { create } from 'zustand';

export type ActionCategory =
  | 'navigation'
  | 'tables'
  | 'branches'
  | 'canvas'
  | 'actions'
  | 'settings'
  | 'help';

export interface CommandPaletteAction {
  id: string;
  label: string;
  description?: string;
  category: ActionCategory;
  keywords?: string[];
  icon?: React.ReactNode;
  shortcut?: string[];
  badge?: string;
  onSelect: () => void;
}

interface CommandPaletteState {
  isOpen: boolean;
  query: string;
  contextualActions: CommandPaletteAction[];
  recentIds: string[];

  open: () => void;
  close: () => void;
  toggle: () => void;
  setQuery: (query: string) => void;
  registerContextualActions: (actions: CommandPaletteAction[]) => () => void;
  recordAction: (id: string) => void;
}

export const useCommandPaletteStore = create<CommandPaletteState>((set) => ({
  isOpen: false,
  query: '',
  contextualActions: [],
  recentIds: [],

  open: () => set({ isOpen: true, query: '' }),
  close: () => set({ isOpen: false, query: '' }),
  toggle: () => set(state => ({ isOpen: !state.isOpen, query: '' })),
  setQuery: (query: string) => set({ query }),

  registerContextualActions: (actions: CommandPaletteAction[]) => {
    set(state => {
      // Remove any existing actions with duplicate IDs before appending
      const newIds = new Set(actions.map(a => a.id));
      const filtered = state.contextualActions.filter(a => !newIds.has(a.id));
      return { contextualActions: [...filtered, ...actions] };
    });

    // Return cleanup unregister function
    return () => {
      set(state => {
        const removedIds = new Set(actions.map(a => a.id));
        return {
          contextualActions: state.contextualActions.filter(a => !removedIds.has(a.id)),
        };
      });
    };
  },

  recordAction: (id: string) => {
    set(state => {
      const filtered = state.recentIds.filter(i => i !== id);
      return { recentIds: [id, ...filtered].slice(0, 8) };
    });
  },
}));
