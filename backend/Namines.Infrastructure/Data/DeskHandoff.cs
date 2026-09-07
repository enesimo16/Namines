using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Models.Auth;

namespace Namines.Infrastructure.Data;

/// <summary>
/// Namines Desk v2 §E1.2 — ana uygulamadan Desk'e SSO devri. Mantık
/// <c>AuthController</c>'dan buraya taşındı (<see cref="GatewayAudit"/> ve
/// <see cref="OrgAccess"/> ile aynı desen: DB'ye dokunan mantık controller'da
/// değil, <c>AuthDbContext</c> uzantı metodu olarak yaşar) — bu, tam da bu
/// dosyadaki tek-kullanımlık YARIŞ KORUMASINI Testcontainers ile gerçek
/// Postgres'e karşı sınayabilmek için gerekli: bir controller action'ı
/// doğrudan çağırmak ASP.NET Core'un tüm pipeline'ını (auth, model binding)
/// ayağa kaldırmayı gerektirirdi.
/// </summary>
public static class DeskHandoff
{
    private static string HashHex(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();

    /// <summary>Adım 1 — ham token üretir, yalnızca özetini saklar, ham değeri döner.</summary>
    public static async Task<string> CreateDeskHandoffTokenAsync(
        this AuthDbContext context, string userId, CancellationToken ct = default)
    {
        var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var now = DateTime.UtcNow;

        context.DeskHandoffTokens.Add(new DeskHandoffToken
        {
            UserId = userId,
            TokenHash = HashHex(raw),
            CreatedAt = now,
            ExpiresAt = now.AddSeconds(30),
        });
        await context.SaveChangesAsync(ct);

        return raw;
    }

    /// <summary>
    /// Adım 2 — jetonu ATOMİK olarak tüketir (kullanılabilirse) ve sahibinin
    /// userId'sini döner; kullanılamıyorsa null.
    ///
    /// <b>Yarış koruması burada:</b> <c>ExecuteUpdateAsync</c> tek bir UPDATE
    /// ifadesi olarak veritabanında çalışır ve yalnızca <c>UsedAt IS NULL</c>
    /// iken satırı günceller — iki eşzamanlı çağrı için ikisi de "kullanılmamış"
    /// okuyup ikisi de geçebileceği normal oku-değiştir-kaydet penceresi burada
    /// yok. Etkilenen satır sayısı 0 ise BU çağrı yarışı kaybetmiştir.
    /// </summary>
    public static async Task<string?> ExchangeDeskHandoffTokenAsync(
        this AuthDbContext context, string rawToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return null;

        var hash = HashHex(rawToken);
        var now = DateTime.UtcNow;

        var entity = await context.DeskHandoffTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (entity is null || !entity.IsUsable(now)) return null;

        var affected = await context.DeskHandoffTokens
            .Where(t => t.Id == entity.Id && t.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), ct);

        return affected == 0 ? null : entity.UserId;
    }
}
