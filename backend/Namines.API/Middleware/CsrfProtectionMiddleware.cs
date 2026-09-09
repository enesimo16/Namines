using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Namines.API.Middleware;

/// <summary>
/// Cookie ile kimlik doğrulanan durum değiştirici isteklerde CSRF koruması.
///
/// <b>Neden gerekli:</b> JWT bir <c>httpOnly</c> cookie'de taşınıyor
/// (<c>namines_token</c>) — bu XSS'e karşı doğru tercih, ama tarayıcı cookie'yi
/// isteğe <b>otomatik</b> eklediği için CSRF'i mümkün kılar. Varsayılan
/// <c>SameSite=Lax</c> çoğu senaryoyu kapatıyordu; ancak
/// <c>Auth:CrossSiteCookie=true</c> verildiğinde cookie <c>SameSite=None</c>
/// oluyor ve tarayıcı onu <b>her siteden</b> gönderiyor. Bu, önerilen dağıtım
/// şekli (frontend ve API ayrı domain'lerde) olduğu için teorik değil.
///
/// <b>CORS neden yetmiyor:</b> CORS allowlist'i saldırganın <i>cevabı
/// okumasını</i> engeller, <i>isteğin gitmesini</i> engellemez. Yan etkili bir
/// uç için cevabı okuyamamak yeterli bir teselli değildir.
///
/// <b>Çözüm — özel başlık zorunluluğu:</b> Bir HTML formu ya da
/// <c>&lt;img&gt;</c>/<c>&lt;script&gt;</c> etiketi özel başlık gönderemez.
/// <c>fetch</c> ile göndermek ise isteği "basit istek" olmaktan çıkarır ve
/// tarayıcıyı <b>preflight</b>'a zorlar — preflight de CORS allowlist'ine
/// takılır. Yani başlığın varlığı, isteğin izinli bir origin'den geldiğinin
/// tarayıcı tarafından doğrulanmış kanıtıdır.
///
/// Anti-forgery token'a göre tercih edilme sebebi: durumsuz, ek bir uç
/// gerektirmiyor ve API tüketicilerini (SDK, CLI) bozmuyor.
///
/// <b>Kimler etkilenmez:</b>
/// <list type="bullet">
/// <item>Güvenli metotlar (GET/HEAD/OPTIONS) — yan etkileri yok.</item>
/// <item><c>Authorization</c> başlığı ile gelen istekler — tarayıcı o başlığı
/// kendiliğinden eklemez, dolayısıyla CSRF yüzeyi yoktur. SDK, CLI, MCP ve
/// Desk bu yoldan gelir.</item>
/// <item>Auth cookie'si taşımayan istekler — webhook'lar (Stripe, GitHub) ve
/// giriş/kayıt uçları buraya düşer.</item>
/// </list>
///
/// <b>SignalR notu:</b> Canvas hub'ı istemcide <c>skipNegotiation: true</c> ile
/// kuruluyor (bkz. <c>frontend/hooks/useMultiplayer.ts</c>), yani bir
/// <c>POST .../negotiate</c> isteği hiç yapılmıyor — WebSocket el sıkışması bir
/// GET. Bu yüzden hub bu kapıdan etkilenmiyor.
///
/// <c>skipNegotiation</c> bir gün kaldırılırsa negotiate isteği 403 alacak.
/// Bu, sessiz bir arıza değil: yanıt gövdesi hangi başlığın eksik olduğunu
/// açıkça söylüyor ve geliştirme sırasında ilk denemede görünür.
/// </summary>
public sealed class CsrfProtectionMiddleware
{
    /// <summary>
    /// İstemcinin göndermesi gereken başlık. Değeri önemli değil, VARLIĞI önemli
    /// — koruma, başlığın içeriğinden değil gönderilebilmiş olmasından geliyor.
    /// </summary>
    public const string HeaderName = "X-Namines-Request";

    private const string AuthCookieName = "namines_token";

    private readonly RequestDelegate _next;

    public CsrfProtectionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (RequiresCsrfHeader(context.Request) &&
            !context.Request.Headers.ContainsKey(HeaderName))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                $"{{\"error\":\"Missing {HeaderName} header. Cookie-authenticated write requests must send it.\"}}");
            return;
        }

        await _next(context);
    }

    private static bool RequiresCsrfHeader(HttpRequest request)
    {
        if (IsSafeMethod(request.Method)) return false;

        // Bearer jetonu tarayıcı tarafından otomatik eklenmez → CSRF yok.
        if (request.Headers.ContainsKey("Authorization")) return false;

        // Cookie yoksa istek zaten kimliksiz; korunacak bir oturum yok.
        return request.Cookies.ContainsKey(AuthCookieName);
    }

    private static bool IsSafeMethod(string method) =>
        HttpMethods.IsGet(method) ||
        HttpMethods.IsHead(method) ||
        HttpMethods.IsOptions(method) ||
        HttpMethods.IsTrace(method);
}
