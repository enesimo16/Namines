using Namines.Infrastructure.Realtime;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Namines.Tests.Integration;

/// <summary>
/// GERÇEK Redis'e karşı, ÇOK INSTANCE senaryosunu kanıtlıyor.
///
/// Faz 1'in sorunu: <c>Membership</c> bir <c>static ConcurrentDictionary</c> idi —
/// yalnızca tek bir process'in belleğinde yaşar. API iki instance'la (ör. iki
/// Kubernetes pod'u) ayağa kalktığında ve bir kullanıcı instance A'ya, diğeri
/// instance B'ye bağlandığında, hiçbiri diğerinin oda üyeliğini göremez —
/// <c>SendSchemaToPeer</c>'ın çapraz-oda doğrulaması sessizce başarısız olur.
///
/// Bu test tam olarak o senaryoyu kurar: AYNI Redis'e bağlı İKİ AYRI
/// <see cref="RedisPresenceStore"/>+<see cref="ConnectionMultiplexer"/> çifti
/// (instance A ve instance B'yi temsil eder) ve birinin yazdığını diğerinin
/// okuyabildiğini kanıtlar.
/// </summary>
public class RedisPresenceStoreTests : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder("redis:7.4-alpine").Build();

    public Task InitializeAsync() =>
        DockerAvailable.Value ? _container.StartAsync() : Task.CompletedTask;

    public Task DisposeAsync() =>
        DockerAvailable.Value ? _container.DisposeAsync().AsTask() : Task.CompletedTask;

    [RequiresDockerFact]
    public async Task Write_from_instance_a_is_visible_from_instance_b()
    {
        // İki AYRI multiplexer — iki AYRI API instance'ını temsil eder. Aynı sınıfın
        // aynı örneğini paylaşmak testi anlamsızlaştırırdı (o zaman zaten in-memory
        // ile aynı şeyi test etmiş olurduk).
        await using var multiplexerA = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
        await using var multiplexerB = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());

        var storeOnInstanceA = new RedisPresenceStore(multiplexerA);
        var storeOnInstanceB = new RedisPresenceStore(multiplexerB);

        // Kullanıcı 1, instance A'ya bağlanıp bir odaya katılıyor.
        await storeOnInstanceA.SetRoomAsync("conn-on-instance-a", "shared-room-42");

        // Kullanıcı 2, instance B'ye bağlı. SendSchemaToPeer çağırdığında instance B,
        // "conn-on-instance-a" bağlantısının hangi odada olduğunu bilmek zorunda —
        // bu bilgi instance A'nın belleğinde DEĞİL, paylaşılan Redis'te olmalı.
        var roomSeenFromInstanceB = await storeOnInstanceB.TryGetRoomAsync("conn-on-instance-a");

        Assert.Equal("shared-room-42", roomSeenFromInstanceB);
    }

    [RequiresDockerFact]
    public async Task Remove_from_instance_a_is_visible_from_instance_b()
    {
        await using var multiplexerA = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
        await using var multiplexerB = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());

        var storeOnInstanceA = new RedisPresenceStore(multiplexerA);
        var storeOnInstanceB = new RedisPresenceStore(multiplexerB);

        await storeOnInstanceA.SetRoomAsync("conn-x", "room-y");

        // Kullanıcı bağlantıyı instance A üzerinde kapatıyor (OnDisconnectedAsync
        // orada tetiklenir, çünkü WebSocket o instance'a bağlıydı).
        var removed = await storeOnInstanceA.RemoveAsync("conn-x");
        Assert.Equal("room-y", removed);

        // instance B artık bu bağlantı için hiçbir oda görmemeli.
        var roomSeenFromInstanceB = await storeOnInstanceB.TryGetRoomAsync("conn-x");
        Assert.Null(roomSeenFromInstanceB);
    }

    [RequiresDockerFact]
    public async Task Unknown_connection_returns_null_not_exception()
    {
        await using var multiplexer = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
        var store = new RedisPresenceStore(multiplexer);

        var result = await store.TryGetRoomAsync("never-connected");

        Assert.Null(result);
    }

    [RequiresDockerFact]
    public async Task Removing_unknown_connection_returns_null_not_exception()
    {
        await using var multiplexer = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
        var store = new RedisPresenceStore(multiplexer);

        var result = await store.RemoveAsync("never-connected");

        Assert.Null(result);
    }

    [RequiresDockerFact]
    public async Task Key_has_a_ttl_so_a_crashed_instance_does_not_leak_forever()
    {
        await using var multiplexer = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
        var store = new RedisPresenceStore(multiplexer);

        await store.SetRoomAsync("conn-ttl", "room-ttl");

        var db = multiplexer.GetDatabase();
        var ttl = await db.KeyTimeToLiveAsync("namines:presence:conn-ttl");

        Assert.NotNull(ttl);
        Assert.True(ttl.Value > TimeSpan.Zero, "Anahtarın TTL'i olmalı — sunucu OnDisconnectedAsync'i " +
            "çalıştıramadan çökerse (ör. kill -9) Redis'te kalıcı çöp birikmemeli.");
    }
}
