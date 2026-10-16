/**
 * Centralized Keyboard Shortcut Registry & Input Safety System
 *
 * Implements Shopify Polaris & SaaS design system accessibility rules:
 * - Platform-aware modifier labels (⌘ on macOS, Ctrl on Windows/Linux)
 * - Input-safety guards: suppresses single-character hotkeys inside text inputs, textareas,
 *   content-editable nodes, and code editors (Monaco/Prism).
 * - Central registry of global, navigation, canvas, and review shortcuts.
 */

export type ShortcutCategory = 'global' | 'navigation' | 'canvas' | 'review' | 'dialog';

export interface ShortcutDefinition {
  id: string;
  label: string;
  description?: string;
  keys: string[];
  category: ShortcutCategory;
  global?: boolean;
}

/** Check if current runtime platform is macOS / iOS */
export function isMac(): boolean {
  if (typeof navigator === 'undefined') return false;
  // Modern userAgentData check or platform fallback
  const p = (navigator as unknown as { userAgentData?: { platform?: string } }).userAgentData?.platform
    || navigator.platform
    || '';
  return /Mac|iPhone|iPod|iPad/i.test(p);
}

/**
 * Returns human-readable label for key array.
 * Converts 'mod' to '⌘' or 'Ctrl', 'alt' to '⌥' or 'Alt', 'shift' to '⇧' or 'Shift'.
 */
export function formatShortcutKey(key: string, mac = isMac()): string {
  const k = key.toLowerCase();
  if (k === 'mod' || k === 'meta' || k === 'cmd') return mac ? '⌘' : 'Ctrl';
  if (k === 'shift') return mac ? '⇧' : 'Shift';
  if (k === 'alt' || k === 'option') return mac ? '⌥' : 'Alt';
  if (k === 'enter') return '↵ Enter';
  if (k === 'escape' || k === 'esc') return 'Esc';
  if (k === 'backspace') return '⌫';
  if (k === 'delete') return 'Del';
  if (k === 'space') return 'Space';
  if (k === 'arrowup' || k === 'up') return '↑';
  if (k === 'arrowdown' || k === 'down') return '↓';
  if (k === 'arrowleft' || k === 'left') return '←';
  if (k === 'arrowright' || k === 'right') return '→';
  return key.toUpperCase();
}

/** Format array of keys into readable badges */
export function formatShortcutCombo(keys: string[], mac = isMac()): string[] {
  return keys.map(k => formatShortcutKey(k, mac));
}

/**
 * Determines whether an event target is an active editable field or code editor.
 * Hotkeys should NOT trigger destructive or navigation behaviors while the user is typing.
 */
export function isEditableElement(target: EventTarget | null): boolean {
  if (!target || !(target instanceof HTMLElement)) return false;

  const tag = target.tagName;
  if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') {
    // Buttons or checkboxes inside inputs still count, but text fields are primary
    const inputType = (target as HTMLInputElement).type?.toLowerCase();
    if (tag === 'INPUT' && (inputType === 'checkbox' || inputType === 'radio' || inputType === 'button' || inputType === 'submit')) {
      return false;
    }
    return true;
  }

  if (
    target.isContentEditable ||
    target.getAttribute('contenteditable') === 'true' ||
    target.getAttribute('contenteditable') === ''
  ) {
    return true;
  }

  // Check closest wrapper for embedded editors (Monaco, Prism, CodeMirror)
  if (target.closest('[contenteditable="true"], [contenteditable=""], .monaco-editor, .prism-editor, [role="textbox"]')) {
    return true;
  }

  return false;
}

/** Matches a KeyboardEvent against a list of key specs (e.g. ['mod', 'k']) */
export function matchesShortcut(event: KeyboardEvent, keys: string[]): boolean {
  const needsMod = keys.some(k => ['mod', 'cmd', 'ctrl', 'meta'].includes(k.toLowerCase()));
  const needsShift = keys.some(k => k.toLowerCase() === 'shift');
  const needsAlt = keys.some(k => ['alt', 'option'].includes(k.toLowerCase()));

  const hasMod = event.ctrlKey || event.metaKey;
  const hasShift = event.shiftKey;
  const hasAlt = event.altKey;

  if (needsMod !== hasMod) return false;
  if (needsShift !== hasShift) return false;
  if (needsAlt !== hasAlt) return false;

  // The primary non-modifier key
  const mainKey = keys.find(k => !['mod', 'cmd', 'ctrl', 'meta', 'shift', 'alt', 'option'].includes(k.toLowerCase()));
  if (!mainKey) return false;

  const eventKey = event.key.toLowerCase();
  const targetKey = mainKey.toLowerCase();

  if (targetKey === 'esc' || targetKey === 'escape') return eventKey === 'escape';
  if (targetKey === 'enter') return eventKey === 'enter';
  if (targetKey === 'space') return eventKey === ' ' || eventKey === 'spacebar';
  if (targetKey === '?') return event.key === '?' || (hasShift && eventKey === '/');
  if (targetKey === '/') return eventKey === '/';

  return eventKey === targetKey;
}

/**
 * Standard Application Shortcuts Catalog
 */
export const APP_SHORTCUTS: ShortcutDefinition[] = [
  // Global
  {
    id: 'global.command_palette',
    label: 'Open Command Palette',
    description: 'Search commands, tables, branches, or jump to any page',
    keys: ['mod', 'k'],
    category: 'global',
    global: true,
  },
  {
    id: 'global.shortcuts_help',
    label: 'Keyboard Shortcuts Cheat Sheet',
    description: 'Display all available keyboard shortcuts',
    keys: ['?'],
    category: 'global',
    global: true,
  },
  {
    id: 'global.shortcuts_help_alt',
    label: 'Keyboard Shortcuts Cheat Sheet (Alternative)',
    description: 'Display all available keyboard shortcuts',
    keys: ['mod', '/'],
    category: 'global',
    global: true,
  },
  {
    id: 'global.escape',
    label: 'Close Active Overlay',
    description: 'Dismiss top-level modal, drawer, or dropdown',
    keys: ['Escape'],
    category: 'global',
    global: true,
  },

  // Navigation
  {
    id: 'nav.canvas',
    label: 'Go to Canvas Diagram',
    description: 'Interactive schema designer and entity relationships',
    keys: ['G', 'D'],
    category: 'navigation',
  },
  {
    id: 'nav.compile',
    label: 'Go to Studio Compile',
    description: 'SQL, Prisma, EF Core code generator and Docker sandbox',
    keys: ['G', 'C'],
    category: 'navigation',
  },
  {
    id: 'nav.flow',
    label: 'Go to Flow Automation',
    description: 'Event-driven database rules and webhook triggers',
    keys: ['G', 'F'],
    category: 'navigation',
  },
  {
    id: 'nav.review',
    label: 'Go to Review Console',
    description: 'Database Change Review, DDL tests, and approvals',
    keys: ['G', 'R'],
    category: 'navigation',
  },
  {
    id: 'nav.ground',
    label: 'Go to Ground DBs',
    description: 'Hosted database instances and resource metrics',
    keys: ['G', 'G'],
    category: 'navigation',
  },
  {
    id: 'nav.vault',
    label: 'Go to Vault Backups',
    description: 'Encrypted backups and automated restore verifications',
    keys: ['G', 'V'],
    category: 'navigation',
  },
  {
    id: 'nav.desk',
    label: 'Open Desk Backoffice',
    description: 'Open data management and query console',
    keys: ['G', 'K'],
    category: 'navigation',
  },

  // Canvas
  {
    id: 'canvas.search',
    label: 'Search Tables & Columns',
    description: 'Locate and zoom directly to an entity on canvas',
    keys: ['mod', 'f'],
    category: 'canvas',
  },
  {
    id: 'canvas.undo',
    label: 'Undo Schema Change',
    description: 'Revert last canvas modification',
    keys: ['mod', 'z'],
    category: 'canvas',
  },
  {
    id: 'canvas.redo',
    label: 'Redo Schema Change',
    description: 'Reapply undone canvas modification',
    keys: ['mod', 'shift', 'z'],
    category: 'canvas',
  },
  {
    id: 'canvas.add_table',
    label: 'Insert New Table',
    description: 'Add an empty table node to canvas',
    keys: ['T'],
    category: 'canvas',
  },
  {
    id: 'canvas.toggle_mode',
    label: 'Toggle Edit / View Mode',
    description: 'Switch between interactive editing and read-only inspection',
    keys: ['mod', 'e'],
    category: 'canvas',
  },
  {
    id: 'canvas.nudge',
    label: 'Nudge Table Position',
    description: 'Move selected table node by 10 pixels',
    keys: ['↑ ↓ ← →'],
    category: 'canvas',
  },
  {
    id: 'canvas.delete_node',
    label: 'Delete Selected Node or Relation',
    description: 'Remove table or foreign key edge from schema',
    keys: ['Delete'],
    category: 'canvas',
  },

  // Review & Dialog
  {
    id: 'review.quick_approve',
    label: 'Approve Change Request',
    description: 'Approve pending database migration',
    keys: ['mod', 'Enter'],
    category: 'review',
  },
  {
    id: 'dialog.confirm',
    label: 'Confirm Primary Action',
    description: 'Submit or confirm active modal dialog',
    keys: ['Enter'],
    category: 'dialog',
  },
  {
    id: 'dialog.dismiss',
    label: 'Cancel or Close Dialog',
    description: 'Dismiss open dialog without saving changes',
    keys: ['Escape'],
    category: 'dialog',
  },
];
