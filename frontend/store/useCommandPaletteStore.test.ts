import { describe, it, expect, beforeEach } from 'vitest';
import { useCommandPaletteStore, CommandPaletteAction } from './useCommandPaletteStore';

describe('useCommandPaletteStore', () => {
  beforeEach(() => {
    useCommandPaletteStore.setState({
      isOpen: false,
      query: '',
      contextualActions: [],
      recentIds: [],
    });
  });

  it('manages open, close and toggle states', () => {
    const store = useCommandPaletteStore.getState();
    expect(store.isOpen).toBe(false);

    store.open();
    expect(useCommandPaletteStore.getState().isOpen).toBe(true);

    store.close();
    expect(useCommandPaletteStore.getState().isOpen).toBe(false);

    store.toggle();
    expect(useCommandPaletteStore.getState().isOpen).toBe(true);
    store.toggle();
    expect(useCommandPaletteStore.getState().isOpen).toBe(false);
  });

  it('updates search query', () => {
    const store = useCommandPaletteStore.getState();
    store.setQuery('users table');
    expect(useCommandPaletteStore.getState().query).toBe('users table');
  });

  it('registers and unregisters contextual actions', () => {
    const action1: CommandPaletteAction = {
      id: 'test-action-1',
      label: 'Test Action 1',
      category: 'actions',
      onSelect: () => {},
    };
    const action2: CommandPaletteAction = {
      id: 'test-action-2',
      label: 'Test Action 2',
      category: 'canvas',
      onSelect: () => {},
    };

    const unregister = useCommandPaletteStore.getState().registerContextualActions([action1, action2]);
    expect(useCommandPaletteStore.getState().contextualActions).toHaveLength(2);
    expect(useCommandPaletteStore.getState().contextualActions[0].id).toBe('test-action-1');

    unregister();
    expect(useCommandPaletteStore.getState().contextualActions).toHaveLength(0);
  });

  it('records recent action IDs with MRU ordering and limits', () => {
    const store = useCommandPaletteStore.getState();
    store.recordAction('action-a');
    store.recordAction('action-b');
    store.recordAction('action-a');

    expect(useCommandPaletteStore.getState().recentIds).toEqual(['action-a', 'action-b']);
  });
});
