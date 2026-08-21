using System;
using System.Collections.Concurrent;
using Namines.Core.Models.Auth;

namespace Namines.Infrastructure.Data;

/// <summary>
/// Anahtar başına dakikalık istek sınırı (new-phase/08-GATEWAY-API.md §4.3
/// <c>rateLimit</c>).
///
/// <b>Bellek içi, yani INSTANCE BAŞINA.</b> İki API instance'ı çalışıyorsa gerçek
/// sınır iki katına çıkar. Doküman (§5) bunun Redis token bucket olmasını istiyor;
/// Redis bu kurulumda yapılandırılmadığı için bellekte tutuluyor. Bu, sınırı
/// "yaklaşık" yapar — ama sınırın hiç olmamasından iyidir ve tek instance
/// dağıtımda tam doğrudur. Redis geldiğinde yalnızca bu sınıfın gövdesi değişir.
///
/// Sabit pencere (fixed window) seçildi: sürgülü pencere daha adil ama istek başına
/// zaman damgası listesi tutmayı gerektirir, yani bellekte istemci başına sınırsız
/// büyüyebilen bir yapı. Sınır koymak için bellek sızdırmak yanlış takas.
/// </summary>
public static class GatewayRateLimiter
{
    private sealed class Window
    {
        public DateTime StartedAt;
        public int Count;
    }

    private static readonly ConcurrentDictionary<string, Window> Windows = new();

    private static readonly TimeSpan WindowLength = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Anahtarın kendi sınırı yoksa sınır uygulanmaz — sunucunun genel
    /// rate-limit politikası zaten devrede (Program.cs "sensitive" politikası).
    /// </summary>
    public static bool TryAcquire(GatewayApiKey key) =>
        key.RateLimitPerMinute is not > 0 || TryAcquire(key.Id, key.RateLimitPerMinute.Value, DateTime.UtcNow);

    internal static bool TryAcquire(string keyId, int limitPerMinute, DateTime now)
    {
        var window = Windows.GetOrAdd(keyId, _ => new Window { StartedAt = now });

        // Kilit sözlüğün tamamında değil, tek bir anahtarın penceresinde: farklı
        // anahtarların istekleri birbirini beklememeli.
        lock (window)
        {
            if (now - window.StartedAt >= WindowLength)
            {
                window.StartedAt = now;
                window.Count = 0;
            }

            if (window.Count >= limitPerMinute) return false;

            window.Count++;
            return true;
        }
    }

    /// <summary>Testler için: pencereleri sıfırlar.</summary>
    internal static void Reset() => Windows.Clear();
}
