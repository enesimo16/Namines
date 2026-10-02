using System;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Extensions.Logging;
using Namines.Core.Models.Auth;
using StackExchange.Redis;

namespace Namines.Infrastructure.Data;

/// <summary>
/// Anahtar başına dakikalık istek sınırı (new-phase/08-GATEWAY-API.md §4.3
/// <c>rateLimit</c>).
///
/// <b>Redis yapılandırılmışsa sayaç DAĞITIK, değilse bellek içi.</b>
///
/// <b>Bulunan açık (docs/last-review L-4):</b> sayaç yalnızca bellek içiydi, yani
/// INSTANCE BAŞINA. İki API instance'ı çalışıyorsa gerçek sınır iki katına
/// çıkıyordu: "dakikada 600 istek" satılan bir anahtar üç instance'lı bir
/// dağıtımda 1.800 istek yapabiliyordu. Sınır kâğıt üzerinde vardı ama
/// ölçeklendikçe sessizce gevşiyordu — ve gevşediğini gösteren hiçbir şey yoktu.
///
/// Sabit pencere (fixed window) seçildi: sürgülü pencere daha adil ama istek başına
/// zaman damgası listesi tutmayı gerektirir, yani istemci başına sınırsız
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

    private static IConnectionMultiplexer? _redis;
    private static ILogger? _logger;

    /// <summary>
    /// Redis'i tek seferde bağlar. Çağrılmazsa sayaç bellek içi kalır — tek
    /// instance dağıtımda bu zaten doğru sonucu veriyor.
    ///
    /// <b>Neden statik bir kurulum, DI değil:</b> sınıf <c>GatewayController</c>'ın
    /// üç ayrı yerinden STATİK olarak çağrılıyor. Enjeksiyona çevirmek o üç çağrı
    /// yerini ve controller'ın kurucusunu değiştirmek demekti; bir sayaç
    /// değişikliğinin yetkilendirme yoluna dokunmasına gerek yok.
    /// </summary>
    public static void UseDistributedCounter(IConnectionMultiplexer redis, ILogger logger)
    {
        _redis = redis;
        _logger = logger;
    }

    /// <summary>
    /// Anahtarın kendi sınırı yoksa sınır uygulanmaz — sunucunun genel
    /// rate-limit politikası zaten devrede (Program.cs "sensitive" politikası).
    /// </summary>
    public static bool TryAcquire(GatewayApiKey key) =>
        key.RateLimitPerMinute is not > 0 || TryAcquire(key.Id, key.RateLimitPerMinute.Value, DateTime.UtcNow);

    internal static bool TryAcquire(string keyId, int limitPerMinute, DateTime now)
    {
        if (_redis is not null && TryAcquireDistributed(keyId, limitPerMinute, now) is { } distributed)
            return distributed;

        return TryAcquireInMemory(keyId, limitPerMinute, now);
    }

    /// <summary>
    /// Dağıtık sayaç. Karar verilemezse (Redis erişilemiyor) <c>null</c> döner ve
    /// çağıran bellek içi sayaca düşer.
    ///
    /// <b>Redis arızasında istek REDDEDİLMİYOR</b> ve bu bilinçli: bir önbellek
    /// arızasını müşterinin API'sini tamamen kapatmaya çevirmek, sınırın kendisinden
    /// çok daha büyük bir zarar. Bellek içi sayaç devreye giriyor, yani sınır
    /// "instance başına"ya geri düşüyor — eski davranış, yani hiç sınır olmaması değil.
    /// </summary>
    private static bool? TryAcquireDistributed(string keyId, int limitPerMinute, DateTime now)
    {
        try
        {
            // Pencere numarası ANAHTARIN İÇİNDE: böylece pencere sıfırlaması ayrı
            // bir yazma gerektirmiyor, anahtar kendi TTL'iyle yok oluyor. Ayrıca
            // instance'lar arası saat farkı en fazla bir pencereyi kaydırır,
            // sayacı bozmaz.
            //
            // <b>İKİ SAYACIN PENCERE SEMANTİĞİ FARKLI</b> ve bu bilerek böyle:
            // burada pencere MUTLAK zamana hizalı kovalar (her istek aynı kovayı
            // hesaplar, paylaşım bunu gerektiriyor); bellek içi sayaç ise ilk
            // isteğe GÖRELİ ölçüyor (`now - window.StartedAt`). Pratik sonuç:
            // dağıtık modda bir anahtar, kova sınırında iki pencerenin hakkını
            // arka arkaya kullanabilir — sabit pencerenin bilinen "sınır etkisi".
            // Göreli pencereyi paylaşmak, her instance'ın "başlangıç"ı için ortak
            // bir yazma turu demekti; sınır etkisi o maliyetten küçük bir taviz.
            //
            // Bunu test ederken `now`'u dakikaya HİZALAMAK gerekiyor; hizasız bir
            // başlangıçta "30 saniye sonra" bir sonraki kovaya düşer.
            var bucket = now.Ticks / WindowLength.Ticks;
            var redisKey = (RedisKey)$"nmn:rl:{keyId}:{bucket}";

            // INCR + ilk artışta PEXPIRE: iki komut TEK Lua betiğinde, yani iki
            // instance aynı anda artırsa bile sayaç kaybolmuyor. Ayrı ayrı
            // gönderilseydi araya giren bir istek TTL'siz bir anahtar bırakabilirdi.
            const string script = @"
                local count = redis.call('INCR', KEYS[1])
                if count == 1 then
                    redis.call('PEXPIRE', KEYS[1], ARGV[1])
                end
                return count";

            var result = _redis!.GetDatabase().ScriptEvaluate(
                script,
                new[] { redisKey },
                new RedisValue[] { (long)WindowLength.TotalMilliseconds * 2 });

            return (long)result <= limitPerMinute;
        }
        catch (Exception ex)
        {
            // Her istekte hata basmak log'u boğar; ilk hatadan sonra susuyor.
            if (Interlocked.Exchange(ref _redisFailureLogged, 1) == 0)
                _logger?.LogWarning(ex,
                    "Gateway hiz limiti icin Redis'e erisilemedi — bellek ici sayaca dusuluyor " +
                    "(sinir instance basina uygulanir).");

            return null;
        }
    }

    private static int _redisFailureLogged;

    private static bool TryAcquireInMemory(string keyId, int limitPerMinute, DateTime now)
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

    /// <summary>Testler için: pencereleri ve Redis bağını sıfırlar.</summary>
    internal static void Reset()
    {
        Windows.Clear();
        _redis = null;
        _logger = null;
        _redisFailureLogged = 0;
    }
}
