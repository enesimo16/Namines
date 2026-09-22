import { describe, expect, it, vi, afterEach } from 'vitest';
import { formatDuration, vaultApi } from './vault';
import type { DeskSession } from './api';

describe('formatDuration', () => {
  it('bitmemis is icin null doner', () => {
    expect(formatDuration('2026-01-01T00:00:00Z', null)).toBeNull();
  });

  it('milisaniye, saniye ve dakikayi ayirir', () => {
    expect(formatDuration('2026-01-01T00:00:00.000Z', '2026-01-01T00:00:00.400Z')).toBe('400 ms');
    expect(formatDuration('2026-01-01T00:00:00Z', '2026-01-01T00:00:08Z')).toBe('8.0 sn');
    expect(formatDuration('2026-01-01T00:00:00Z', '2026-01-01T00:02:05Z')).toBe('2 dk 5 sn');
  });

  it('tutarsiz zaman damgalarini gostermez', () => {
    // Bitiş, başlangıçtan önce olamaz. Negatif bir süre yazmak, saat kaymasını
    // kullanıcının yorumlaması gereken bir veri hâline getirirdi.
    expect(formatDuration('2026-01-01T00:00:10Z', '2026-01-01T00:00:00Z')).toBeNull();
    expect(formatDuration('gecersiz', '2026-01-01T00:00:00Z')).toBeNull();
  });
});

const session: DeskSession = { token: 'tkn', projectId: 'proj1' };

afterEach(() => {
  vi.unstubAllGlobals();
});

/**
 * REGRESYON: `waitForBackup` ('Vault backup polling with no unmount
 * cancellation' bulgusu) çağıranı bekletmeden sonsuza kadar yoklamaya devam
 * ediyordu — çağıran bileşen (Vault.tsx) unmount olsa bile. `signal` artık
 * döngüyü erken durdurabiliyor; bu test, iptalden SONRA bir daha
 * `status`'a hiç istek atılmadığını doğruluyor.
 */
describe('vaultApi.waitForBackup', () => {
  it('signal iptal edildiğinde yoklamayı durdurur ve done:false döner', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      id: 'b1', status: 'Running', sizeBytes: 0, errorMessage: null,
      createdAt: '2026-01-01T00:00:00Z', completedAt: null, done: false,
    }), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    const controller = new AbortController();
    controller.abort();

    const outcome = await vaultApi.waitForBackup(session, 'b1', { signal: controller.signal, intervalMs: 1 });

    expect(outcome.done).toBe(false);
    // Zaten iptal edilmiş bir signal ile döngüye hiç girilmemeli.
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('signal olmadan normal şekilde bitene kadar yoklar', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      id: 'b1', status: 'Succeeded', sizeBytes: 100, errorMessage: null,
      createdAt: '2026-01-01T00:00:00Z', completedAt: '2026-01-01T00:00:05Z', done: true,
    }), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    const outcome = await vaultApi.waitForBackup(session, 'b1', { intervalMs: 1 });

    expect(outcome).toMatchObject({ done: true, status: 'Succeeded' });
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });
});
