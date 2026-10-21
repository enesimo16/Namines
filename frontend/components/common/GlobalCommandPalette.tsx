'use client';

import React, { useEffect, useMemo, useRef, useState, useCallback } from 'react';
import { useRouter, usePathname } from 'next/navigation';
import {
  Search, X, Boxes, Zap, Hammer, ShieldCheck, HardDrive, GitPullRequest,
  Table2, Plus, Settings, Sun, Moon, Languages, Keyboard,
  Sparkles, Columns, GitBranch, ArrowRight, CornerDownLeft, FileText,
  Database
} from 'lucide-react';
import { useCommandPaletteStore, CommandPaletteAction, ActionCategory } from '../../store/useCommandPaletteStore';
import { useSchemaStore } from '../../store/useSchemaStore';
import { useAuthStore, isUserAdmin } from '../../store/useAuthStore';
import { useProjectHistoryStore } from '../../store/useProjectHistoryStore';
import { useHomeThemeStore } from '../../store/useHomeThemeStore';
import { useToastStore } from '../../store/useToastStore';
import { setLocale, useLangAttr } from '../../lib/i18n';
import { formatShortcutKey, isMac } from '../../lib/shortcuts';
import { openDeskHandoff, DESK_URL } from '../../lib/deskHandoff';
import { authService } from '../../services/api';

const CATEGORY_ORDER: ActionCategory[] = [
  'navigation',
  'tables',
  'branches',
  'canvas',
  'actions',
  'settings',
  'help',
];

const CATEGORY_LABELS: Record<ActionCategory, string> = {
  navigation: 'Navigation',
  tables: 'Tables & Entities',
  branches: 'Branches',
  canvas: 'Canvas & Diagram',
  actions: 'Actions',
  settings: 'Preferences & System',
  help: 'Help & Reference',
};

export default function GlobalCommandPalette() {
  const { isOpen, close, toggle, query, setQuery, contextualActions, recordAction } = useCommandPaletteStore();
  const router = useRouter();
  const pathname = usePathname();
  const lang = useLangAttr();
  const showToast = useToastStore(s => s.showToast);

  const schema = useSchemaStore(s => s.schema);
  const tables = schema?.tables || [];
  const activeProjectId = useProjectHistoryStore(s => s.activeProjectId);
  const user = useAuthStore(s => s.user);
  const projects = useProjectHistoryStore(s => s.projects);
  const switchBranch = useProjectHistoryStore(s => s.switchBranch);
  const activeProject = projects.find(p => p.id === activeProjectId);
  const branches = activeProject?.branches || [];
  const currentBranch = activeProject?.currentBranch || 'main';
  const { theme, toggle: toggleTheme } = useHomeThemeStore();

  const [activeIdx, setActiveIdx] = useState(0);
  const inputRef = useRef<HTMLInputElement>(null);
  const listRef = useRef<HTMLDivElement>(null);
  const isApple = isMac();

  // Listen to global ⌘K / Ctrl+K and custom events
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault();
        toggle();
      }
    };

    const handleCustomOpen = () => {
      useCommandPaletteStore.getState().open();
    };

    window.addEventListener('keydown', handleKeyDown);
    window.addEventListener('namines:open-command-palette', handleCustomOpen);

    return () => {
      window.removeEventListener('keydown', handleKeyDown);
      window.removeEventListener('namines:open-command-palette', handleCustomOpen);
    };
  }, [toggle]);

  // Focus input when opened
  useEffect(() => {
    if (isOpen) {
      const timer = setTimeout(() => {
        inputRef.current?.focus();
        inputRef.current?.select();
      }, 40);
      return () => clearTimeout(timer);
    }
  }, [isOpen]);

  const navigateTo = useCallback((path: string) => {
    close();
    if (pathname !== path) {
      router.push(path);
    }
  }, [close, pathname, router]);

  const openDesk = useCallback(async () => {
    close();
    try {
      if (activeProjectId) {
        try {
          const { token } = await authService.createDeskHandoffToken();
          openDeskHandoff({ token, projectId: activeProjectId, view: 'data' });
          return;
        } catch {
          window.open(DESK_URL, '_blank');
          return;
        }
      }
      window.open(DESK_URL, '_blank');
    } catch {
      window.open(DESK_URL, '_blank');
    }
  }, [close, activeProjectId]);

  // Built-in actions
  const defaultActions = useMemo<CommandPaletteAction[]>(() => {
    const actions: CommandPaletteAction[] = [
      // Navigation
      {
        id: 'nav-canvas',
        label: 'Canvas Diagram',
        description: 'Interactive ERD whiteboard and schema design',
        category: 'navigation',
        keywords: ['canvas', 'diagram', 'whiteboard', 'erd', 'schema'],
        icon: <Boxes className="w-4 h-4 text-accent-text" />,
        shortcut: ['G', 'D'],
        onSelect: () => navigateTo('/canvas'),
      },
      {
        id: 'nav-compile',
        label: 'Studio Compile & Export',
        description: 'Export DDL, Prisma, EF Core, README and test sandbox',
        category: 'navigation',
        keywords: ['compile', 'studio', 'export', 'sql', 'prisma', 'docker'],
        icon: <Hammer className="w-4 h-4 text-accent-text" />,
        shortcut: ['G', 'C'],
        onSelect: () => navigateTo('/compile'),
      },
      {
        id: 'nav-review',
        label: 'Review Console',
        description: 'Database Change Review, risk classification, and audit log',
        category: 'navigation',
        keywords: ['review', 'pull request', 'migration', 'approval', 'cr'],
        icon: <GitPullRequest className="w-4 h-4 text-accent-text" />,
        shortcut: ['G', 'R'],
        onSelect: () => navigateTo('/review'),
      },
      {
        id: 'nav-flow',
        label: 'Flow Automation',
        description: 'Event-driven database webhooks, rules, and actions',
        category: 'navigation',
        keywords: ['flow', 'automation', 'rules', 'triggers', 'events'],
        icon: <Zap className="w-4 h-4 text-accent-text" />,
        shortcut: ['G', 'F'],
        onSelect: () => navigateTo('/flow'),
      },
      {
        id: 'nav-ground',
        label: 'Ground Hosted DBs',
        description: 'Managed database instances and live connection metrics',
        category: 'navigation',
        keywords: ['ground', 'host', 'database', 'postgres', 'finops'],
        icon: <HardDrive className="w-4 h-4 text-accent-text" />,
        shortcut: ['G', 'G'],
        onSelect: () => navigateTo('/ground'),
      },
      {
        id: 'nav-vault',
        label: 'Vault Backups',
        description: 'Encrypted backups, verification runs, and point-in-time restore',
        category: 'navigation',
        keywords: ['vault', 'backup', 'restore', 'encrypted', 'security'],
        icon: <ShieldCheck className="w-4 h-4 text-accent-text" />,
        shortcut: ['G', 'V'],
        onSelect: () => navigateTo('/vault'),
      },
      {
        id: 'nav-desk',
        label: 'Desk Backoffice',
        description: 'Data browser, table management, and raw SQL console',
        category: 'navigation',
        keywords: ['desk', 'admin', 'crud', 'data', 'backoffice'],
        icon: <Table2 className="w-4 h-4 text-accent-text" />,
        shortcut: ['G', 'K'],
        onSelect: openDesk,
      },
      ...(isUserAdmin(user) ? [{
        id: 'nav-admin',
        label: 'Admin Console',
        description: 'System operations, feature flags, AI routing, and error logs',
        category: 'navigation' as const,
        keywords: ['admin', 'console', 'panel', 'flags', 'routing', 'users', 'logs'],
        icon: <ShieldCheck className="w-4 h-4 text-accent-text" />,
        shortcut: ['G', 'A'],
        onSelect: () => navigateTo('/admin'),
      }] : []),
      {
        id: 'nav-new',
        label: 'New Project',
        description: 'Create a new database architecture from scratch or AI prompt',
        category: 'actions',
        keywords: ['new', 'create', 'project', 'start'],
        icon: <Plus className="w-4 h-4 text-accent-text" />,
        onSelect: () => navigateTo('/new'),
      },

      // Settings & Preferences
      {
        id: 'action-settings',
        label: 'Account & AI Settings',
        description: 'Configure models, API keys, team access, and quota',
        category: 'settings',
        keywords: ['settings', 'preferences', 'ai', 'token', 'quota', 'profile'],
        icon: <Settings className="w-4 h-4 text-content-secondary" />,
        shortcut: ['mod', ','],
        onSelect: () => {
          close();
          window.dispatchEvent(new CustomEvent('namines:open-ai-settings'));
        },
      },
      {
        id: 'action-theme',
        label: `Switch to ${theme === 'dark' ? 'Light' : 'Dark'} Mode`,
        description: 'Toggle visual appearance theme',
        category: 'settings',
        keywords: ['theme', 'dark', 'light', 'mode'],
        icon: theme === 'dark' ? <Sun className="w-4 h-4 text-accent-text" /> : <Moon className="w-4 h-4 text-content-secondary" />,
        onSelect: () => {
          close();
          toggleTheme();
          showToast(`Theme switched to ${theme === 'dark' ? 'Light' : 'Dark'} mode`, 'info');
        },
      },
      {
        id: 'action-language',
        label: lang === 'tr' ? 'Switch Language to English' : 'Dili Türkçe\'ye Değiştir',
        description: 'Toggle interface language',
        category: 'settings',
        keywords: ['language', 'dil', 'english', 'turkish', 'türkçe', 'i18n'],
        icon: <Languages className="w-4 h-4 text-content-secondary" />,
        onSelect: () => {
          close();
          const nextLocale = lang === 'tr' ? 'en' : 'tr';
          setLocale(nextLocale);
          showToast(nextLocale === 'tr' ? 'Dil Türkçe olarak ayarlandı' : 'Language set to English', 'info');
        },
      },
      {
        id: 'action-shortcuts',
        label: 'Keyboard Shortcuts Cheat Sheet',
        description: 'View all keyboard shortcuts and navigation commands',
        category: 'help',
        keywords: ['shortcuts', 'keyboard', 'hotkeys', 'cheat sheet', 'help'],
        icon: <Keyboard className="w-4 h-4 text-content-secondary" />,
        shortcut: ['?'],
        onSelect: () => {
          close();
          window.dispatchEvent(new CustomEvent('namines:open-shortcuts-modal'));
        },
      },
    ];

    // Add branch switching if branches exist
    if (branches.length > 0) {
      branches.forEach(b => {
        actions.push({
          id: `branch-${b.name}`,
          label: `Switch to branch: ${b.name}`,
          description: b.name === currentBranch ? 'Currently active branch' : `Switch workspace to ${b.name}`,
          category: 'branches',
          keywords: ['branch', 'git', b.name],
          icon: <GitBranch className="w-4 h-4 text-content-secondary" />,
          badge: b.name === currentBranch ? 'Active' : undefined,
          onSelect: () => {
            close();
            if (activeProjectId && b.name !== currentBranch) {
              switchBranch(activeProjectId, b.name);
              showToast(`Switched to branch "${b.name}"`, 'success');
            }
          },
        });
      });
    }

    // Add schema tables as direct search targets
    if (tables.length > 0) {
      tables.forEach(t => {
        const colNames = t.columns.map(c => c.name).join(', ');
        actions.push({
          id: `table-${t.name}`,
          label: t.name,
          description: `${t.columns.length} columns: ${colNames.slice(0, 45)}${colNames.length > 45 ? '…' : ''}`,
          category: 'tables',
          keywords: ['table', t.name, ...t.columns.map(c => c.name)],
          icon: <Table2 className="w-4 h-4 text-accent-text" />,
          badge: `${t.columns.length} cols`,
          onSelect: () => {
            close();
            if (pathname === '/canvas') {
              window.dispatchEvent(new CustomEvent('namines:focus-table', {
                detail: { tableName: t.name, tableId: t.id }
              }));
            } else {
              router.push(`/canvas?table=${encodeURIComponent(t.name)}`);
            }
          },
        });
      });
    }

    return actions;
  }, [
    activeProjectId,
    branches,
    close,
    currentBranch,
    lang,
    navigateTo,
    openDesk,
    pathname,
    router,
    showToast,
    switchBranch,
    tables,
    theme,
    toggleTheme,
  ]);

  // Combine default actions with contextual actions
  const allActions = useMemo(() => {
    const actionMap = new Map<string, CommandPaletteAction>();
    // Default actions first
    defaultActions.forEach(a => actionMap.set(a.id, a));
    // Contextual actions override or extend
    contextualActions.forEach(a => actionMap.set(a.id, a));
    return Array.from(actionMap.values());
  }, [defaultActions, contextualActions]);

  // Filter actions based on query
  const filteredActions = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return allActions;

    return allActions.filter(action => {
      if (action.label.toLowerCase().includes(q)) return true;
      if (action.description?.toLowerCase().includes(q)) return true;
      if (action.category.toLowerCase().includes(q)) return true;
      if (action.keywords?.some(k => k.toLowerCase().includes(q))) return true;
      return false;
    });
  }, [allActions, query]);

  // Group actions by category
  const groupedActions = useMemo(() => {
    const groups: { category: ActionCategory; label: string; items: CommandPaletteAction[] }[] = [];
    const map = new Map<ActionCategory, CommandPaletteAction[]>();

    filteredActions.forEach(action => {
      const cat = action.category;
      if (!map.has(cat)) map.set(cat, []);
      map.get(cat)!.push(action);
    });

    CATEGORY_ORDER.forEach(cat => {
      const items = map.get(cat);
      if (items && items.length > 0) {
        groups.push({
          category: cat,
          label: CATEGORY_LABELS[cat] || cat,
          items,
        });
      }
    });

    return groups;
  }, [filteredActions]);

  // Flat list for indexing
  const flatItems = useMemo(() => {
    return groupedActions.flatMap(g => g.items);
  }, [groupedActions]);

  // Keep active index in bounds
  useEffect(() => {
    setActiveIdx(0);
  }, [query]);

  // Keep selected row in view
  useEffect(() => {
    const list = listRef.current;
    if (!list) return;
    const item = list.querySelector(`[data-idx="${activeIdx}"]`) as HTMLElement | null;
    item?.scrollIntoView({ block: 'nearest' });
  }, [activeIdx]);

  const runAction = useCallback((action: CommandPaletteAction) => {
    recordAction(action.id);
    action.onSelect();
  }, [recordAction]);

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      setActiveIdx(i => (i + 1 < flatItems.length ? i + 1 : 0));
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setActiveIdx(i => (i - 1 >= 0 ? i - 1 : Math.max(0, flatItems.length - 1)));
    } else if (e.key === 'Enter') {
      e.preventDefault();
      const current = flatItems[activeIdx];
      if (current) runAction(current);
    } else if (e.key === 'Escape') {
      e.preventDefault();
      close();
    }
  };

  if (!isOpen) return null;

  let currentGlobalIdx = 0;

  return (
    <div
      className="fixed inset-0 z-modal flex items-start justify-center pt-[10vh] sm:pt-[14vh] bg-scrim/60 backdrop-blur-sm animate-in fade-in duration-150 p-4"
      onClick={close}
      role="dialog"
      aria-modal="true"
      aria-label="Command Palette"
    >
      <div
        className="bg-surface-800 border border-line-strong rounded-[var(--radius-modal)] shadow-2xl w-full max-w-2xl overflow-hidden flex flex-col focus:outline-none"
        onClick={e => e.stopPropagation()}
        onKeyDown={handleKeyDown}
      >
        {/* Search Input Bar (Shopify Polaris style) */}
        <div className="flex items-center gap-3 px-4 py-3.5 border-b border-line-subtle bg-surface-800/90">
          <Search className="w-5 h-5 text-content-muted shrink-0" aria-hidden="true" />
          <input
            ref={inputRef}
            type="text"
            role="combobox"
            aria-autocomplete="list"
            aria-expanded="true"
            aria-controls="command-palette-results"
            aria-activedescendant={flatItems[activeIdx] ? `cmd-opt-${flatItems[activeIdx].id}` : undefined}
            value={query}
            onChange={e => setQuery(e.target.value)}
            placeholder="Search commands, navigate, or search tables..."
            className="flex-1 bg-transparent text-content-primary placeholder:text-content-muted text-sm sm:text-base outline-none border-none focus:ring-0"
          />
          {query ? (
            <button
              type="button"
              onClick={() => setQuery('')}
              className="text-content-muted hover:text-content-primary p-1 rounded-[var(--radius-control)] transition-colors cursor-pointer"
              aria-label="Clear query"
            >
              <X className="w-4 h-4" />
            </button>
          ) : (
            <div className="flex items-center gap-1 shrink-0">
              <kbd className="px-1.5 py-0.5 rounded-[var(--radius-control)] bg-surface-700 border border-line-strong text-content-muted text-[11px] font-mono">
                ESC
              </kbd>
            </div>
          )}
        </div>

        {/* Results List */}
        <div
          id="command-palette-results"
          ref={listRef}
          role="listbox"
          className="overflow-y-auto max-h-[55vh] py-2 scroll-py-2"
        >
          {flatItems.length === 0 ? (
            <div className="flex flex-col items-center justify-center py-12 px-4 text-center">
              <Database className="w-8 h-8 text-content-muted/60 mb-2" />
              <p className="text-sm font-medium text-content-primary">No commands or entities found</p>
              <p className="text-xs text-content-muted mt-1 max-w-sm">
                No matching actions, tables, or navigation targets for &quot;{query}&quot;. Try another search term.
              </p>
            </div>
          ) : (
            groupedActions.map(group => (
              <div key={group.category} className="mb-2">
                <div className="px-4 py-1.5 text-[11px] font-bold uppercase tracking-wider text-content-muted/80">
                  {group.label}
                </div>
                {group.items.map(action => {
                  const itemIndex = currentGlobalIdx++;
                  const isSelected = itemIndex === activeIdx;

                  return (
                    <div
                      key={action.id}
                      id={`cmd-opt-${action.id}`}
                      role="option"
                      aria-selected={isSelected}
                      data-idx={itemIndex}
                      onClick={() => runAction(action)}
                      onMouseEnter={() => setActiveIdx(itemIndex)}
                      className={`flex items-center justify-between gap-3 px-4 py-2.5 mx-2 rounded-[var(--radius-control)] text-left cursor-pointer transition-colors ${
                        isSelected
                          ? 'bg-surface-700 text-content-primary'
                          : 'text-content-secondary hover:bg-surface-700/60 hover:text-content-primary'
                      }`}
                    >
                      <div className="flex items-center gap-3 min-w-0">
                        <span
                          className={`w-7 h-7 flex items-center justify-center rounded-[var(--radius-control)] shrink-0 transition-colors ${
                            isSelected
                              ? 'bg-accent text-accent-on'
                              : 'bg-surface-700/80 text-content-muted border border-line-subtle'
                          }`}
                        >
                          {action.icon ?? <ArrowRight className="w-3.5 h-3.5" />}
                        </span>
                        <div className="min-w-0">
                          <div className="flex items-center gap-2">
                            <span className="text-sm font-medium truncate text-content-primary">
                              {action.label}
                            </span>
                            {action.badge && (
                              <span className="px-1.5 py-0.5 rounded-[var(--radius-control)] text-[10px] font-mono bg-surface-600 text-content-muted border border-line-subtle">
                                {action.badge}
                              </span>
                            )}
                          </div>
                          {action.description && (
                            <p className="text-xs text-content-muted truncate mt-0.5">
                              {action.description}
                            </p>
                          )}
                        </div>
                      </div>

                      <div className="flex items-center gap-1.5 shrink-0">
                        {action.shortcut && (
                          <div className="flex items-center gap-1">
                            {action.shortcut.map(k => (
                              <kbd
                                key={k}
                                className="px-1.5 py-0.5 rounded-[var(--radius-control)] bg-surface-700 border border-line-strong text-content-muted text-[10px] font-mono"
                              >
                                {formatShortcutKey(k, isApple)}
                              </kbd>
                            ))}
                          </div>
                        )}
                        {isSelected && (
                          <CornerDownLeft className="w-3.5 h-3.5 text-accent-text ml-1" />
                        )}
                      </div>
                    </div>
                  );
                })}
              </div>
            ))
          )}
        </div>

        {/* Footer Hints (Shopify Polaris style) */}
        <div className="flex items-center justify-between px-4 py-2.5 border-t border-line-subtle bg-surface-800/80 text-content-muted text-xs">
          <div className="flex items-center gap-3">
            <span className="flex items-center gap-1">
              <kbd className="font-mono bg-surface-700 border border-line-strong px-1.5 py-0.5 rounded-[var(--radius-control)] text-[10px]">↑</kbd>
              <kbd className="font-mono bg-surface-700 border border-line-strong px-1.5 py-0.5 rounded-[var(--radius-control)] text-[10px]">↓</kbd>
              <span className="ml-1">Navigate</span>
            </span>
            <span className="flex items-center gap-1">
              <kbd className="font-mono bg-surface-700 border border-line-strong px-1.5 py-0.5 rounded-[var(--radius-control)] text-[10px]">↵</kbd>
              <span className="ml-1">Select</span>
            </span>
            <span className="flex items-center gap-1">
              <kbd className="font-mono bg-surface-700 border border-line-strong px-1.5 py-0.5 rounded-[var(--radius-control)] text-[10px]">ESC</kbd>
              <span className="ml-1">Close</span>
            </span>
          </div>

          <div className="hidden sm:flex items-center gap-2 text-[11px]">
            <span>Press <kbd className="font-mono bg-surface-700 border border-line-strong px-1 py-0.2 rounded-[var(--radius-control)] text-[10px]">?</kbd> for shortcut guide</span>
          </div>
        </div>
      </div>
    </div>
  );
}
