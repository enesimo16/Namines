import { describe, it, expect } from 'vitest';
import { BULK_DELETE_THRESHOLD } from './BulkDeleteConfirm';

/**
 * `new-phase/34-SENDEN-BEKLENENLER.md` madde 12'nin onaylanmış kararı: eşik
 * 10. Bu sayı UI'da (Desk.tsx'in `requestBulkDelete`) ve dokümanlarda tekrar
 * ediyor — burada sabit bir değere kilitleniyor ki biri "yuvarlarım" diyip
 * elle değiştirdiğinde fark edilsin.
 */
describe('BULK_DELETE_THRESHOLD', () => {
  it('onaylanan değer olan 10 olarak kalır', () => {
    expect(BULK_DELETE_THRESHOLD).toBe(10);
  });
});
