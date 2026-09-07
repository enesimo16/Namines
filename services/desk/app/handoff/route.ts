import { NextRequest } from 'next/server';

/**
 * Namines Desk v2 §E1.2 — SSO devrinin Desk tarafı, adım 2.
 *
 * Yalnızca POST kabul eder (App Router'da bir GET handler'ı hiç
 * tanımlanmadığı için otomatik 405 döner) — jeton BURAYA bir form POST
 * gövdesinde gelir, asla bir query string olarak değil (frontend'in
 * `openNaminesDesk()`'ine bkz.).
 *
 * Değişim sunucu tarafında yapılır (bu bir Route Handler, istemci
 * component'i değil): jeton hiçbir zaman tarayıcının adres çubuğuna ya da
 * ağ sekmesinin "Request URL"ine düşmez, yalnızca bu POST'un gövdesinde
 * görünür.
 */
export async function POST(req: NextRequest): Promise<Response> {
  const form = await req.formData();
  const token = form.get('token');

  if (typeof token !== 'string' || !token) {
    return htmlResponse(errorPage('Eksik jeton.'), 400);
  }

  const API = process.env.NAMINES_API ?? 'http://localhost:5000';
  let jwt: string | null = null;

  try {
    const res = await fetch(`${API}/api/auth/desk-handoff-exchange`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ token }),
      cache: 'no-store',
    });

    if (res.ok) {
      const body = await res.json();
      jwt = body?.token ?? null;
    }
  } catch {
    // ağ hatası — jwt null kalır, aşağıdaki genel hata sayfası gösterilir
  }

  if (!jwt) {
    return htmlResponse(
      errorPage('Bu bağlantı geçersiz, süresi dolmuş ya da zaten kullanılmış. Ana uygulamadan tekrar deneyin.'),
      401,
    );
  }

  // JWT yalnızca BURADA, sunucudan istemciye giden bu tek yanıtın gövdesinde
  // görünür — bir SPA'nın token'ı istemciye teslim etmesinin standart yolu
  // budur (bir URL değil, bir yanıt gövdesi).
  return htmlResponse(`<!doctype html>
<html><head><meta charset="utf-8"><title>Namines Desk</title></head>
<body style="background:#0b0b0b;color:#eceff1;font-family:ui-sans-serif,system-ui,sans-serif;display:flex;align-items:center;justify-content:center;min-height:100vh;margin:0">
<p>Giriş yapılıyor…</p>
<script>
  try { sessionStorage.setItem('namines-desk-token', ${JSON.stringify(jwt)}); } catch (e) {}
  location.replace('/');
</script>
</body></html>`, 200);
}

function errorPage(message: string): string {
  return `<!doctype html>
<html><head><meta charset="utf-8"><title>Namines Desk</title></head>
<body style="background:#0b0b0b;color:#eceff1;font-family:ui-sans-serif,system-ui,sans-serif;display:flex;align-items:center;justify-content:center;min-height:100vh;margin:0;padding:20px;text-align:center">
<div>
  <h1 style="font-size:16px">Namines Desk'e giriş yapılamadı</h1>
  <p style="color:#acb2b7;font-size:13px">${escapeHtml(message)}</p>
</div>
</body></html>`;
}

function escapeHtml(s: string): string {
  return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

function htmlResponse(html: string, status: number): Response {
  return new Response(html, { status, headers: { 'Content-Type': 'text/html; charset=utf-8' } });
}
