using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.Infrastructure.Data;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace Namines.Tests.Integration;

/// <summary>
/// <see cref="GatewayRateLimiter"/>'ın DAĞITIK sayacı — docs/last-review L-4.
///
/// <b>Bulunan açık:</b> sayaç yalnızca bellek içiydi, yani INSTANCE BAŞINA. İki API
/// instance'ı çalışıyorsa gerçek sınır iki katına çıkıyordu: "dakikada 600 istek"
/// satılan bir anahtar üç instance'lı bir dağıtımda 1.800 istek yapabiliyordu.
/// Sınır kâğıt üzerinde vardı ama ölçeklendikçe sessizce gevşiyordu.
///
/// <b>Bu testler GERÇEK Redis'e karşı çalışmak zorunda.</b> İddia tam olarak
/// "sayaç süreç dışında paylaşılıyor" — sahte bir Redis ile bunu kanıtlamak,
/// kendi sahtemizi test etmek olurdu.
/// </summary>
[Collection("Docker")]
public class GatewayRateLimiterDistributedTests : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder("redis:7.4-alpine").Build();
    private IConnectionMultiplexer? _redis;

    public async Task InitializeAsync()
    {
        if (!DockerAvailable.Value) return;

        await _container.StartAsync();
        _redis = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        GatewayRateLimiter.Reset();
        if (!DockerAvailable.Value) return;

        if (_redis is not null) await _redis.DisposeAsync();
        await _container.DisposeAsync();
    }

    /// <summary>
    /// Dağıtık sayacı bağlar ve Redis'in GERÇEKTEN cevap verdiğini doğrular.
    ///
    /// <b>Ping şart ve bunu bir test hatası öğretti:</b> `TryAcquireDistributed`
    /// hata alırsa bellek içi sayaca düşüyor (bilinçli — bir önbellek arızası
    /// müşterinin API'sini kapatmamalı). Bağlantı henüz kurulmamışken ilk çağrı
    /// belleğe, ikincisi Redis'e giderse Redis sayacı sıfır olduğu için ikinci
    /// çağrıya İZİN VERİLİYOR. Test tek başına geçip sınıfla birlikte kırılmasının
    /// sebebi tam olarak buydu (bozuk-Redis testinden sonra bağlantı toparlanırken).
    ///
    /// <b>Bu, ürünün de gerçek bir sınırı:</b> iki sayaç arasındaki geçiş anında
    /// bir anahtar iki pencerenin hakkını kullanabilir. Alternatif, Redis
    /// arızasında isteği reddetmek olurdu — sınırın kendisinden büyük bir zarar.
    /// </summary>
    private void UseRedis()
    {
        GatewayRateLimiter.Reset();

        // Ping: bağlantı hazır olmadan sayacı bağlamak, ilk çağrının sessizce
        // bellek içine düşmesi demek.
        var pong = _redis!.GetDatabase().Ping();
        Assert.True(pong >= TimeSpan.Zero);

        GatewayRateLimiter.UseDistributedCounter(_redis!, NullLogger.Instance);
    }

    [RequiresDockerFact]
    public async Task Sayac_SURECLER_ARASI_paylasiliyor()
    {
        // ASIL İDDİA: bellek içi sayaçta bu test geçmez, çünkü her "instance"
        // kendi sözlüğünü görür. Burada tek süreç var ama sayaç Redis'te —
        // yani ikinci bir instance'ın göreceği şeyin aynısı.
        UseRedis();
        var keyId = "shared-" + Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;

        Assert.True(await GatewayRateLimiter.TryAcquireAsync(keyId, limitPerMinute: 3, now));
        Assert.True(await GatewayRateLimiter.TryAcquireAsync(keyId, limitPerMinute: 3, now));
        Assert.True(await GatewayRateLimiter.TryAcquireAsync(keyId, limitPerMinute: 3, now));

        // Dördüncü istek sınırı aşıyor.
        Assert.False(await GatewayRateLimiter.TryAcquireAsync(keyId, limitPerMinute: 3, now));

        // Bellek içi sözlük BOŞ — karar gerçekten Redis'ten geldi.
        GatewayRateLimiter.Reset();
        GatewayRateLimiter.UseDistributedCounter(_redis!, NullLogger.Instance);
        Assert.False(await GatewayRateLimiter.TryAcquireAsync(keyId, limitPerMinute: 3, now));
    }

    [RequiresDockerFact]
    public async Task Pencere_dolunca_sayac_SIFIRLANIYOR()
    {
        // Pencere numarası anahtarın İÇİNDE: yeni pencere yeni anahtar demek,
        // yani sıfırlama ayrı bir yazma gerektirmiyor.
        //
        // <b>Başlangıç DAKİKAYA HİZALANIYOR</b> ve bu testin ilk hâlini düzeltti:
        // dağıtık sayaç pencereyi MUTLAK zamana göre kovalara ayırıyor
        // (`now.Ticks / WindowLength.Ticks`), bellek içi sayaç ise `now`'a GÖRELİ
        // ölçüyor (`now - window.StartedAt`). Hizalanmamış bir başlangıçta
        // "30 saniye sonra" bir sonraki kovaya düşebiliyor ve test rastgele
        // kırılıyordu — ilk koşuda geçip tam suite'te kırılmasının sebebi buydu.
        UseRedis();
        var keyId = "window-" + Guid.NewGuid().ToString("N");

        var utc = DateTime.UtcNow;
        var windowStart = new DateTime(
            utc.Ticks - (utc.Ticks % TimeSpan.FromMinutes(1).Ticks), DateTimeKind.Utc);

        Assert.True(await GatewayRateLimiter.TryAcquireAsync(keyId, limitPerMinute: 1, windowStart));

        // İlk çağrı GERÇEKTEN Redis'e gitti mi? `TryAcquireDistributed` hata
        // alırsa sessizce bellek içine düşüyor (bilinçli), ve o durumda bu test
        // anlamsız bir boolean üzerinden kırılıyordu. Kovanın Redis'te var
        // olduğunu doğrulamak, kararsızlığı NET bir mesaja çeviriyor.
        var bucket = windowStart.Ticks / TimeSpan.FromMinutes(1).Ticks;
        Assert.True(
            _redis!.GetDatabase().KeyExists($"nmn:rl:{keyId}:{bucket}"),
            "Ilk cagri Redis'e gitmedi (bellek ici sayaca dusulmus) — bu test " +
            "dagitik sayaci olcemez.");

        // AYNI kovada: reddediliyor.
        Assert.False(await GatewayRateLimiter.TryAcquireAsync(keyId, limitPerMinute: 1, windowStart.AddSeconds(30)));

        // SONRAKİ kovada: yeni anahtar, yeni sayaç.
        Assert.True(await GatewayRateLimiter.TryAcquireAsync(keyId, limitPerMinute: 1, windowStart.AddMinutes(1)));
    }

    [RequiresDockerFact]
    public async Task Farkli_anahtarlar_BIRBIRINI_etkilemiyor()
    {
        UseRedis();
        var suffix = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;

        Assert.True(await GatewayRateLimiter.TryAcquireAsync("a-" + suffix, limitPerMinute: 1, now));
        Assert.False(await GatewayRateLimiter.TryAcquireAsync("a-" + suffix, limitPerMinute: 1, now));

        Assert.True(await GatewayRateLimiter.TryAcquireAsync("b-" + suffix, limitPerMinute: 1, now));
    }

    [RequiresDockerFact]
    public async Task Redis_ERISILEMEZSE_istek_reddedilmiyor()
    {
        // Bir önbellek arızasını müşterinin API'sini tamamen kapatmaya çevirmek,
        // sınırın kendisinden çok daha büyük bir zarar olurdu. Bellek içi sayaca
        // düşülüyor — yani eski davranış, hiç sınır olmaması değil.
        GatewayRateLimiter.Reset();

        var broken = await ConnectionMultiplexer.ConnectAsync(new ConfigurationOptions
        {
            EndPoints = { { "127.0.0.1", 6399 } }, // kimse dinlemiyor
            AbortOnConnectFail = false,
            ConnectTimeout = 200,
            ConnectRetry = 0,
        });

        GatewayRateLimiter.UseDistributedCounter(broken, NullLogger.Instance);

        var keyId = "fallback-" + Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;

        // Bellek içi sayaç devrede: ilk istek geçiyor, sınır yine uygulanıyor.
        Assert.True(await GatewayRateLimiter.TryAcquireAsync(keyId, limitPerMinute: 1, now));
        Assert.False(await GatewayRateLimiter.TryAcquireAsync(keyId, limitPerMinute: 1, now));

        await broken.DisposeAsync();
    }
}
