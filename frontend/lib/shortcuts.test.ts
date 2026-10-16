// @vitest-environment jsdom
import { describe, it, expect } from 'vitest';
import {
  formatShortcutKey,
  formatShortcutCombo,
  isEditableElement,
  matchesShortcut,
  APP_SHORTCUTS,
} from './shortcuts';

describe('shortcuts system', () => {
  describe('formatShortcutKey & formatShortcutCombo', () => {
    it('formats mod key for mac and non-mac platforms', () => {
      expect(formatShortcutKey('mod', true)).toBe('⌘');
      expect(formatShortcutKey('mod', false)).toBe('Ctrl');
      expect(formatShortcutKey('shift', true)).toBe('⇧');
      expect(formatShortcutKey('shift', false)).toBe('Shift');
      expect(formatShortcutKey('alt', true)).toBe('⌥');
      expect(formatShortcutKey('alt', false)).toBe('Alt');
    });

    it('formats combo arrays correctly', () => {
      expect(formatShortcutCombo(['mod', 'k'], true)).toEqual(['⌘', 'K']);
      expect(formatShortcutCombo(['mod', 'k'], false)).toEqual(['Ctrl', 'K']);
      expect(formatShortcutCombo(['mod', 'shift', 'z'], false)).toEqual(['Ctrl', 'Shift', 'Z']);
    });
  });

  describe('isEditableElement input safety', () => {
    it('returns false for null or body element', () => {
      expect(isEditableElement(null)).toBe(false);
      const div = document.createElement('div');
      expect(isEditableElement(div)).toBe(false);
    });

    it('returns true for text input and textarea', () => {
      const input = document.createElement('input');
      input.type = 'text';
      expect(isEditableElement(input)).toBe(true);

      const textarea = document.createElement('textarea');
      expect(isEditableElement(textarea)).toBe(true);
    });

    it('returns false for button or checkbox input', () => {
      const btn = document.createElement('input');
      btn.type = 'button';
      expect(isEditableElement(btn)).toBe(false);

      const cb = document.createElement('input');
      cb.type = 'checkbox';
      expect(isEditableElement(cb)).toBe(false);
    });

    it('returns true for contenteditable element or child inside editor', () => {
      const editableDiv = document.createElement('div');
      editableDiv.setAttribute('contenteditable', 'true');
      document.body.appendChild(editableDiv);
      expect(isEditableElement(editableDiv)).toBe(true);
      document.body.removeChild(editableDiv);

      const editorContainer = document.createElement('div');
      editorContainer.className = 'monaco-editor';
      const innerSpan = document.createElement('span');
      editorContainer.appendChild(innerSpan);
      document.body.appendChild(editorContainer);

      expect(isEditableElement(innerSpan)).toBe(true);
      document.body.removeChild(editorContainer);
    });
  });

  describe('matchesShortcut', () => {
    it('matches mod+k correctly', () => {
      const event = new KeyboardEvent('keydown', { key: 'k', ctrlKey: true });
      expect(matchesShortcut(event, ['mod', 'k'])).toBe(true);
      expect(matchesShortcut(event, ['mod', 'f'])).toBe(false);
    });

    it('matches shift+mod+z correctly', () => {
      const event = new KeyboardEvent('keydown', { key: 'z', metaKey: true, shiftKey: true });
      expect(matchesShortcut(event, ['mod', 'shift', 'z'])).toBe(true);
      expect(matchesShortcut(event, ['mod', 'z'])).toBe(false);
    });

    it('matches question mark (?)', () => {
      const event = new KeyboardEvent('keydown', { key: '?' });
      expect(matchesShortcut(event, ['?'])).toBe(true);
    });
  });

  describe('APP_SHORTCUTS catalog', () => {
    it('contains all required categories', () => {
      const categories = new Set(APP_SHORTCUTS.map(s => s.category));
      expect(categories.has('global')).toBe(true);
      expect(categories.has('navigation')).toBe(true);
      expect(categories.has('canvas')).toBe(true);
      expect(categories.has('review')).toBe(true);
      expect(categories.has('dialog')).toBe(true);
    });

    it('has valid key definitions for every shortcut', () => {
      APP_SHORTCUTS.forEach(s => {
        expect(s.id).toBeDefined();
        expect(s.label).toBeDefined();
        expect(s.keys.length).toBeGreaterThan(0);
      });
    });
  });
});
