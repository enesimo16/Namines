// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest';
import { renderHook, cleanup, fireEvent } from '@testing-library/react';
import { useEscapeKey } from './useEscapeKey';

/**
 * Elle çizilmiş modallar yalnızca fareyle kapanıyordu (AI Settings, Migration, Team,
 * Auth...). Ortak kanca: açıkken Esc kapatır, kapalıyken dinlemez, Esc'i başka biri
 * tüketmişse (defaultPrevented) dış modal onunla birlikte kapanmaz.
 */
describe('useEscapeKey', () => {
  afterEach(() => cleanup());

  it('calls onEscape when Escape is pressed while active', () => {
    const onEscape = vi.fn();
    renderHook(() => useEscapeKey(true, onEscape));

    fireEvent.keyDown(document, { key: 'Escape' });

    expect(onEscape).toHaveBeenCalledTimes(1);
  });

  it('ignores other keys', () => {
    const onEscape = vi.fn();
    renderHook(() => useEscapeKey(true, onEscape));

    fireEvent.keyDown(document, { key: 'Enter' });

    expect(onEscape).not.toHaveBeenCalled();
  });

  it('does not listen while inactive (a closed modal must not react)', () => {
    const onEscape = vi.fn();
    renderHook(() => useEscapeKey(false, onEscape));

    fireEvent.keyDown(document, { key: 'Escape' });

    expect(onEscape).not.toHaveBeenCalled();
  });

  it('stops listening once it becomes inactive', () => {
    const onEscape = vi.fn();
    const { rerender } = renderHook(({ active }) => useEscapeKey(active, onEscape), {
      initialProps: { active: true },
    });

    rerender({ active: false });
    fireEvent.keyDown(document, { key: 'Escape' });

    expect(onEscape).not.toHaveBeenCalled();
  });

  it('lets an inner control consume Escape first (defaultPrevented)', () => {
    const onEscape = vi.fn();
    renderHook(() => useEscapeKey(true, onEscape));

    const inner = (event: KeyboardEvent) => event.preventDefault();
    document.addEventListener('keydown', inner, true);
    fireEvent.keyDown(document, { key: 'Escape' });
    document.removeEventListener('keydown', inner, true);

    expect(onEscape).not.toHaveBeenCalled();
  });
});
