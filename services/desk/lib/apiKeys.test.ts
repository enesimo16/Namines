import { describe, it, expect, vi, afterEach } from 'vitest';
import { apiKeysApi, ApiKeysError } from './apiKeys';
import type { DeskSession } from './api';

/**
 * `lib/apiKeys.ts` — özellikle `revoke()` için REGRESYON testi.
 *
 * Backend'in `GatewayKeyController.Revoke` aksiyonu `204 No Content`
 * (BOŞ gövde) döner. `revoke()` bir zamanlar genel `req<T>()` yardımcısını
 * kullanıyordu ve o körlemesine `res.json()` çağırıyordu — 204'te bu
 * `SyntaxError: Unexpected end of JSON input` ile patlıyordu. Düzeltme:
 * `revoke()` artık `res.json()` hiç çağırmıyor. Bu test o regresyonun bir
 * daha sessizce geri gelmemesi için var.
 */

const session: DeskSession = { token: 'tkn', projectId: 'proj1' };

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('apiKeysApi.revoke', () => {
  it('204 No Content (boş gövde) ile BAŞARIYLA döner — .json() çağırmaz', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(null, { status: 204 }),
    );
    vi.stubGlobal('fetch', fetchMock);

    await expect(apiKeysApi.revoke(session, 'key1')).resolves.toBeUndefined();
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining('/api/gateway/keys/proj1/key1'),
      expect.objectContaining({ method: 'DELETE' }),
    );
  });

  it('hata durumunda sunucunun error mesajını taşır', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ error: 'Admin yetkisi gerekli.' }), { status: 403 }),
    );
    vi.stubGlobal('fetch', fetchMock);

    await expect(apiKeysApi.revoke(session, 'key1')).rejects.toMatchObject({
      message: 'Admin yetkisi gerekli.',
      status: 403,
    });
  });

  it('gövde JSON değilse varsayılan mesaja düşer, atmaz', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response('not json', { status: 500 }));
    vi.stubGlobal('fetch', fetchMock);

    await expect(apiKeysApi.revoke(session, 'key1')).rejects.toBeInstanceOf(ApiKeysError);
  });
});

describe('apiKeysApi.create', () => {
  it('ham anahtarı (yalnızca bu yanıtta gelen) döner', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      id: 'k1', name: 'CI', prefix: 'nmn_abc', canWrite: true, canExecuteSql: false,
      allowedOrigins: null, allowedIps: null, rateLimitPerMinute: 60,
      createdAt: '2026-01-01T00:00:00Z', expiresAt: null, revokedAt: null, lastUsedAt: null,
      key: 'nmn_abc123RAW', warning: 'Bu anahtar bir daha gösterilmeyecek.',
    }), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    const created = await apiKeysApi.create(session, 'CI', true);
    expect(created.key).toBe('nmn_abc123RAW');
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining('/api/gateway/keys/proj1'),
      expect.objectContaining({ method: 'POST' }),
    );
  });
});
