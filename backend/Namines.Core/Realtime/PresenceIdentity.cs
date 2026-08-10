using System.Security.Claims;

namespace Namines.Core.Realtime;

/// <summary>
/// CanvasHub'ın sunum-adı çözümleme mantığı — SignalR'dan bağımsız, saf fonksiyon
/// olarak ayrıştırıldı ki doğrudan test edilebilsin (Hub context'i mock'lamadan).
///
/// Kural: kimliği doğrulanmış (giriş yapmış) bir kullanıcı için JWT claim'inden
/// gelen gerçek ad kullanılır — istemcinin gönderdiği serbest metne güvenilmez,
/// aksi halde giriş yapmış bir kullanıcının adını yazıp onun gibi görünmek mümkün
/// olurdu. Anonim/guest kullanıcı için (Studio'da girişsiz canlı işbirliği tasarım
/// gereği desteklenir) istemcinin gönderdiği ad olduğu gibi kullanılır.
/// </summary>
public static class PresenceIdentity
{
    public static string ResolveDisplayName(ClaimsPrincipal? user, string? clientSuppliedName, int maxLength = 64)
    {
        if (user?.Identity?.IsAuthenticated == true)
        {
            var claimName = user.FindFirstValue(ClaimTypes.Name);
            if (!string.IsNullOrWhiteSpace(claimName))
                return Truncate(claimName, maxLength);
        }

        return Truncate(clientSuppliedName, maxLength);
    }

    private static string Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? string.Empty : (s.Length > max ? s[..max] : s);
}
