import { describe, it, expect, vi, afterEach } from 'vitest';
import { fetchMembers, MembersError } from './members';
import type { DeskSession } from './api';

const session: DeskSession = { token: 'tkn', projectId: 'proj1' };

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('fetchMembers', () => {
  it('camelCase alanları (gerçek backend biçimi) doğru eşler', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify([
      { userId: 'u1', username: 'ayse', email: 'ayse@x.com', role: 'Owner', joinedAt: '2026-01-01T00:00:00Z' },
    ]), { status: 200 })));

    const members = await fetchMembers(session);
    expect(members).toEqual([
      { userId: 'u1', username: 'ayse', email: 'ayse@x.com', role: 'Owner', joinedAt: '2026-01-01T00:00:00Z' },
    ]);
  });

  it('PascalCase alanlara da düşer (savunmacı fallback)', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify([
      { UserId: 'u2', Username: 'mehmet', Email: null, Role: 'Editor', JoinedAt: '2026-02-01T00:00:00Z' },
    ]), { status: 200 })));

    const members = await fetchMembers(session);
    expect(members).toEqual([
      { userId: 'u2', username: 'mehmet', email: null, role: 'Editor', joinedAt: '2026-02-01T00:00:00Z' },
    ]);
  });

  it('boş liste boş dizi döner (üye yok durumu, hata değil)', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('[]', { status: 200 })));
    expect(await fetchMembers(session)).toEqual([]);
  });

  it('404 (proje bulunamadı) → MembersError, sunucunun mesajı ve durum koduyla', async () => {
    // Her çağrıda TAZE bir Response üretilir — gövde akışı yalnızca bir kez
    // okunabilir, aynı örneği iki assertion'da yeniden kullanmak ikinci
    // okumayı sessizce boşa düşürür (gerçek hatayı değil, test kurulumunu
    // bozar).
    vi.stubGlobal('fetch', vi.fn().mockImplementation(() =>
      Promise.resolve(new Response(JSON.stringify({ error: 'Proje bulunamadı.' }), { status: 404 })),
    ));

    await expect(fetchMembers(session)).rejects.toMatchObject({
      status: 404, message: 'Proje bulunamadı.',
    });
  });
});
