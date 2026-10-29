import { describe, it, expect, vi, afterEach } from 'vitest';
import { NextRequest } from 'next/server';
import { POST } from './route';

/**
 * `POST /handoff` — login-CSRF koruması (`isTrustedCaller`).
 *
 * `same-site` kabul edilmiyor: aynı üst alan adı altındaki başka bir alt alan adı
 * (örn. kullanıcı içeriği barındıran bir host) `Sec-Fetch-Site: same-site` görünür. Ana
 * uygulamadan gelen meşru istek zaten `Origin` eşleşmesiyle geçiyor.
 */
const FRONTEND = 'http://localhost:3000';

function handoff(headers: Record<string, string>): NextRequest {
  const body = new FormData();
  body.set('token', 'one-time-token');
  return new NextRequest('http://localhost:3200/handoff', { method: 'POST', body, headers });
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('POST /handoff caller guard', () => {
  it('rejects a cross-site form post', async () => {
    const res = await POST(handoff({ 'sec-fetch-site': 'cross-site', origin: 'https://evil.example' }));
    expect(res.status).toBe(403);
  });

  it('rejects a same-site request from a sibling subdomain', async () => {
    const res = await POST(
      handoff({ 'sec-fetch-site': 'same-site', origin: 'https://user-content.example.com' }),
    );
    expect(res.status).toBe(403);
  });

  it('rejects a request that carries neither Origin nor Sec-Fetch-Site', async () => {
    const res = await POST(handoff({}));
    expect(res.status).toBe(403);
  });

  it('never caches the response', async () => {
    const res = await POST(handoff({ 'sec-fetch-site': 'cross-site' }));
    expect(res.headers.get('cache-control')).toBe('no-store');
  });

  it('accepts the main app by its Origin even when the browser reports same-site', async () => {
    // Desk ana uygulamayla aynı site'ta ama farklı origin'de (desk.x.com <-> x.com):
    // tarayıcı `same-site` der, ama Origin ana uygulamayla birebir eşleşir.
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({ ok: true, json: async () => ({ token: 'jwt.value.here' }) }),
    );
    const res = await POST(handoff({ 'sec-fetch-site': 'same-site', origin: FRONTEND }));
    expect(res.status).toBe(200);
    expect(res.headers.get('cache-control')).toBe('no-store');
  });

  it('accepts a same-origin request', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({ ok: true, json: async () => ({ token: 'jwt.value.here' }) }),
    );
    const res = await POST(handoff({ 'sec-fetch-site': 'same-origin' }));
    expect(res.status).toBe(200);
  });
});

describe('POST /handoff backend URL', () => {
  afterEach(() => {
    vi.unstubAllEnvs();
  });

  it('reads the backend address at runtime and never doubles /api', async () => {
    vi.stubEnv('NAMINES_API_INTERNAL', 'http://namines-backend:8080/api/');
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, json: async () => ({ token: 'jwt.value.here' }) });
    vi.stubGlobal('fetch', fetchMock);
    const res = await POST(handoff({ 'sec-fetch-site': 'same-origin' }));
    expect(res.status).toBe(200);
    expect(fetchMock.mock.calls[0][0]).toBe('http://namines-backend:8080/api/auth/desk-handoff-exchange');
  });

  it('falls back to NAMINES_API when no internal address is set', async () => {
    vi.stubEnv('NAMINES_API_INTERNAL', undefined);
    vi.stubEnv('NAMINES_API', 'http://backend:9000');
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, json: async () => ({ token: 'jwt.value.here' }) });
    vi.stubGlobal('fetch', fetchMock);
    await POST(handoff({ 'sec-fetch-site': 'same-origin' }));
    expect(fetchMock.mock.calls[0][0]).toBe('http://backend:9000/api/auth/desk-handoff-exchange');
  });
});
