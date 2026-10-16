'use client';

import { useEffect, useRef } from 'react';
import { isEditableElement, matchesShortcut } from '../lib/shortcuts';

export interface ShortcutOptions {
  /** If true, shortcut will not fire when user is typing in input/textarea/editor. Default: true */
  ignoreInputs?: boolean;
  /** Whether shortcut is active. Default: true */
  enabled?: boolean;
  /** If true, call event.preventDefault() when matched. Default: true */
  preventDefault?: boolean;
}

/**
 * Registers a global or contextual keyboard shortcut handler.
 * Respects input safety and prevents default behavior when triggered.
 */
export function useKeyboardShortcut(
  keys: string[],
  handler: (event: KeyboardEvent) => void,
  options: ShortcutOptions = {}
) {
  const {
    ignoreInputs = true,
    enabled = true,
    preventDefault = true,
  } = options;

  const handlerRef = useRef(handler);
  useEffect(() => {
    handlerRef.current = handler;
  });

  useEffect(() => {
    if (!enabled) return;

    const onKeyDown = (event: KeyboardEvent) => {
      // If ignoreInputs is set and target is an editable field, skip
      if (ignoreInputs && isEditableElement(event.target)) {
        return;
      }

      if (matchesShortcut(event, keys)) {
        if (preventDefault) {
          event.preventDefault();
        }
        handlerRef.current(event);
      }
    };

    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [keys, ignoreInputs, enabled, preventDefault]);
}

/**
 * Global two-key sequence navigation hook (e.g. "G" then "D" goes to Diagram).
 * Suppresses when typing in input elements.
 */
export function useSequenceShortcut(
  firstKey: string,
  secondKey: string,
  handler: () => void,
  enabled = true
) {
  const pendingFirstKeyRef = useRef(false);
  const timeoutRef = useRef<NodeJS.Timeout | null>(null);

  useEffect(() => {
    if (!enabled) return;

    const onKeyDown = (event: KeyboardEvent) => {
      if (isEditableElement(event.target)) return;
      if (event.ctrlKey || event.metaKey || event.altKey) return;

      const key = event.key.toLowerCase();

      if (pendingFirstKeyRef.current) {
        if (timeoutRef.current) clearTimeout(timeoutRef.current);
        pendingFirstKeyRef.current = false;

        if (key === secondKey.toLowerCase()) {
          event.preventDefault();
          handler();
        }
        return;
      }

      if (key === firstKey.toLowerCase()) {
        pendingFirstKeyRef.current = true;
        timeoutRef.current = setTimeout(() => {
          pendingFirstKeyRef.current = false;
        }, 800);
      }
    };

    window.addEventListener('keydown', onKeyDown);
    return () => {
      window.removeEventListener('keydown', onKeyDown);
      if (timeoutRef.current) clearTimeout(timeoutRef.current);
    };
  }, [firstKey, secondKey, handler, enabled]);
}
