import { describe, it, expect } from 'vitest';
import { useConfirmStore } from './useConfirmStore';

describe('useConfirmStore', () => {
  it('ikinci onay istenince birincisi iptal (false) olarak çözülür', async () => {
    const first = useConfirmStore.getState().confirm({ title: 'a', message: 'a' });
    const second = useConfirmStore.getState().confirm({ title: 'b', message: 'b' });
    await expect(first).resolves.toBe(false);
    useConfirmStore.getState().respond(true);
    await expect(second).resolves.toBe(true);
  });
});
