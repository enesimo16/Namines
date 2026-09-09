using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Namines.API.Controllers;
using Namines.Infrastructure.Data;

namespace Namines.API.Extensions;

/// <summary>
/// Çıkarılmış bir JWT'yi süresi dolmadan geçersiz kılabilmek.
///
/// <b>Çözdüğü sorun:</b> JWT durumsuzdur — imzası geçerli olduğu sürece sunucu
/// onu kabul eder. Çalınmış (XSS öncesi kopyalanmış, bir log'a düşmüş, bir
/// proxy'de görülmüş) bir jetonu iptal etmenin tek yolu imzalama anahtarını
/// değiştirmekti, ki bu <b>herkesi</b> çıkışa zorlar. Yani pratikte iptal
/// mekanizması yoktu.
///
/// <b>Çözüm:</b> ASP.NET Identity zaten her kullanıcıda bir
/// <c>SecurityStamp</c> tutuyor ve parola değişimi gibi güvenlik olaylarında
/// onu yeniliyor. Jeton üretilirken damganın bir kopyası claim olarak
/// gömülüyor; her istekte kopya ile güncel damga karşılaştırılıyor.
/// Eşleşmiyorsa jeton reddediliyor.
///
/// Böylece "parolamı değiştirdim" ve "tüm oturumlarımı kapat" işlemleri
/// çalınmış jetonu <b>gerçekten</b> öldürüyor.
///
/// <b>Neden önbellek:</b> Kontrolü her istekte veritabanına giderek yapmak,
/// kimlik doğrulamalı her isteğe bir sorgu eklerdi. Damga nadiren değişen bir
/// değer olduğu için kısa ömürlü bir önbellek doğru takas:
/// <see cref="CacheDuration"/> kadar gecikmeyle iptal ediliyor. Bu, ASP.NET
/// Identity'nin cookie tarafında yaptığının (varsayılan 30 dakikalık
/// <c>ValidationInterval</c>) çok daha sıkı bir versiyonu.
///
/// <b>Geriye uyumluluk:</b> Bu değişiklikten ÖNCE üretilmiş jetonlarda damga
/// claim'i yok. Onlar reddedilmiyor — aksi hâlde dağıtım anında herkes
/// çıkışa zorlanırdı. Süreleri dolduğunda (7 gün) kendiliğinden kaybolacaklar.
/// </summary>
public static class SecurityStampValidation
{
    /// <summary>
    /// Damganın önbellekte tutulma süresi. İptalin devreye girmesi bu kadar
    /// gecikebilir. Kısa tutuldu: bir güvenlik olayında dakikalar önemlidir.
    /// </summary>
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    public static JwtBearerEvents AddSecurityStampValidation(this JwtBearerEvents events)
    {
        var existing = events.OnTokenValidated;

        events.OnTokenValidated = async ctx =>
        {
            if (existing is not null) await existing(ctx);

            var principal = ctx.Principal;
            var userId = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var tokenStamp = principal?.FindFirstValue(AuthController.SecurityStampClaimType);

            // Eski jeton (claim yok) → kabul. Bkz. sınıf notu "Geriye uyumluluk".
            if (userId is null || tokenStamp is null) return;

            var cache = ctx.HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
            var cacheKey = $"sstamp:{userId}";

            if (!cache.TryGetValue(cacheKey, out string? currentStamp))
            {
                var db = ctx.HttpContext.RequestServices.GetRequiredService<AuthDbContext>();
                currentStamp = await db.Users
                    .Where(u => u.Id == userId)
                    .Select(u => u.SecurityStamp)
                    .FirstOrDefaultAsync(ctx.HttpContext.RequestAborted);

                // Kullanıcı silinmişse sorgu null döner ve o da önbelleğe
                // alınır. Silinmiş bir kullanıcının jetonu, içindeki gerçek
                // damgayla boş dizeyi karşılaştıracağı için aşağıda reddedilir.
                cache.Set(cacheKey, currentStamp, CacheDuration);
            }

            // null ve boş dize EŞDEĞER sayılıyor. Identity normalde her
            // kullanıcıya damga verir, ama vermediği bir kenar durumda
            // (elle eklenmiş bir satır gibi) `null != ""` karşılaştırması o
            // kullanıcıyı KALICI olarak dışarıda bırakırdı — güvenlik kazancı
            // olmadan, çünkü iki taraf da "damga yok" demek.
            if (!string.Equals(currentStamp ?? string.Empty, tokenStamp, StringComparison.Ordinal))
            {
                ctx.Fail("Security stamp mismatch — this token has been revoked.");
            }
        };

        return events;
    }
}
