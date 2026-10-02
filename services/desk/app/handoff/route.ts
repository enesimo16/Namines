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
  // Çapraz-site form gönderimi REDDEDİLİR (login CSRF).
  //
  // Bu uç kimlik doğrulaması istemiyor — istemesi de gerekmiyor, jetonun
  // kendisi kimlik belgesi. Ama koruma olmadan bir saldırgan KENDİ devir
  // jetonuyla otomatik gönderilen bir form kurup kurbanın tarayıcısını
  // KENDİ hesabına düşürebilir. Kurban Desk'i kendi paneli sanıp Import
  // akışından gerçek bir bağlantı dizesi girerse, o bağlantı saldırganın
  // projesine yazılır ve saldırgan onu okuyabilir.
  //
  // `Sec-Fetch-Site` modern tarayıcılarda her istekte var ve JavaScript'ten
  // taklit edilemez. Yoksa (eski tarayıcı) `Origin` ile aynı kontrol yapılır;
  // ikisi de yoksa istek reddedilir — belirsizlikte oturum kurmak yanlış yön.
  if (!isTrustedCaller(req)) {
    return htmlResponse(errorPage('This link came from an unexpected site.'), 403);
  }

  const form = await req.formData();
  const token = form.get('token');
  // Launch akışı (POST /api/launch) bu ikisini de gönderir: kullanıcıyı
  // Desk'in proje listesine değil, doğrudan o projenin ilgili görünümüne
  // (ör. Data) indirir. İkisi de OPSİYONEL — bugünkü openNaminesDesk() çağrısı
  // yalnızca token gönderiyor ve öncekiyle birebir aynı davranır.
  const projectId = form.get('projectId');
  const view = form.get('view');

  if (typeof token !== 'string' || !token) {
    return htmlResponse(errorPage('Missing token.'), 400);
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
      errorPage('This link is invalid, expired, or already used. Try again from the main app.'),
      401,
    );
  }

  // Yalnızca ikisi de string ve dolu ise seed edilir — LaunchController'ın
  // gönderdiği deep-link. Yoksa bu blok boş string'e düşer ve script'te
  // hiçbir şey çalışmaz: önceki (yalnızca token'lı) davranış AYNEN korunur.
  const deepLinkScript = (typeof projectId === 'string' && projectId && typeof view === 'string' && view)
    ? `try {
         sessionStorage.setItem('namines-desk-project', ${jsonForScript(projectId)});
         sessionStorage.setItem('namines-desk-view', ${jsonForScript(view)});
       } catch (e) {}`
    : '';

  // JWT yalnızca BURADA, sunucudan istemciye giden bu tek yanıtın gövdesinde
  // görünür — bir SPA'nın token'ı istemciye teslim etmesinin standart yolu
  // budur (bir URL değil, bir yanıt gövdesi).
  return htmlResponse(`<!doctype html>
<html><head><meta charset="utf-8"><title>Namines Desk</title></head>
<body style="background:#0b0b0b;color:#eceff1;font-family:ui-sans-serif,system-ui,sans-serif;display:flex;align-items:center;justify-content:center;min-height:100vh;margin:0">
<p>Signing in…</p>
<script>
  try { sessionStorage.setItem('namines-desk-token', ${jsonForScript(jwt)}); } catch (e) {}
  ${deepLinkScript}
  location.replace('/');
</script>
</body></html>`, 200);
}

/**
 * JSON.stringify + `<` kaçışı — bir `</script>` (ya da `<!--`) alt dizesi
 * içeren bir değer (JWT/projectId/view kullanıcı/istemci kontrollü olabilir),
 * kaçışsız olsaydı bu inline <script> bloğunu erken kapatıp içine gelen
 * metni ham HTML/script olarak çalıştırabilirdi.
 */
function jsonForScript(value: string): string {
  return JSON.stringify(value).replace(/</g, '\\u003c');
}

function errorPage(message: string): string {
  return `<!doctype html>
<html><head><meta charset="utf-8"><title>Namines Desk</title></head>
<body style="background:#0b0b0b;color:#eceff1;font-family:ui-sans-serif,system-ui,sans-serif;display:flex;align-items:center;justify-content:center;min-height:100vh;margin:0;padding:20px;text-align:center">
<div>
  <h1 style="font-size:16px">Could not sign in to Namines Desk</h1>
  <p style="color:#acb2b7;font-size:13px">${escapeHtml(message)}</p>
</div>
</body></html>`;
}

/**
 * İstek, güvenilen bir yerden mi geldi?
 *
 * İki kabul yolu var:
 *
 * 1. `Origin`, yapılandırılmış ana uygulama adresiyle (`NAMINES_FRONTEND`) birebir
 *    eşleşiyorsa kabul. Desk ana uygulamayla aynı site'ta (`desk.namines.com` ↔
 *    `namines.com`) ya da tamamen ayrı bir domain'de durabilir; her iki durumda tek
 *    meşru çağıran ana uygulamadır ve tarayıcı çapraz-origin POST'a Origin'i ekler.
 * 2. Aksi hâlde `Sec-Fetch-Site` YALNIZCA `same-origin` (ya da elle açılan `none`) ise
 *    kabul. Bu başlığı tarayıcı koyar, sayfa JavaScript'i değiştiremez.
 *    `same-site` kabul EDİLMİYOR: aynı üst alan adı altındaki başka bir alt alan adı da
 *    `same-site` görünür ve login-CSRF'i yeniden açardı.
 *
 * İkisi de yoksa reddedilir — belirsizlikte oturum kurmak yanlış yön.
 */
function isTrustedCaller(req: NextRequest): boolean {
  const expected = process.env.NAMINES_FRONTEND ?? 'http://localhost:3000';
  const origin = req.headers.get('origin');

  if (origin !== null && isSameOrigin(origin, expected)) return true;

  const fetchSite = req.headers.get('sec-fetch-site');
  // 'none' = adres çubuğuna elle yazılan/yer imi isteği; bir form POST'u
  // olarak gelmesi mümkün değil ama tarayıcı farkları için kabul ediliyor.
  // 'same-site' BİLEREK yok: aynı üst alan adı altındaki başka bir alt alan adı (örn. kullanıcı
  // içeriği barındıran bir host) 'same-site' görünür ve login-CSRF'i yeniden açardı. Ayrı
  // alt alan adında kurulmuş meşru bir Desk'in tek çağıranı ana uygulamadır ve o zaten yukarıdaki
  // Origin eşleşmesiyle geçer (tarayıcı çapraz-origin POST'a Origin'i her zaman ekler).
  return fetchSite === 'same-origin' || fetchSite === 'none';
}

/** Şema + host + port karşılaştırması; sondaki eğik çizgi farkı önemsiz. */
function isSameOrigin(a: string, b: string): boolean {
  try {
    const x = new URL(a);
    const y = new URL(b);
    return x.protocol === y.protocol && x.host === y.host;
  } catch {
    return false;
  }
}

function escapeHtml(s: string): string {
  return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

function htmlResponse(html: string, status: number): Response {
  // no-store: başarılı yanıt bir taşıyıcı JWT içeriyor; POST yanıtı olduğu için pratikte
  // önbelleğe alınmaz, ama bunu bir ara katmanın varsayılanına bırakmıyoruz.
  return new Response(html, {
    status,
    headers: { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' },
  });
}
