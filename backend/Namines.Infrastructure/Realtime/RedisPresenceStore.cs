using Namines.Core.Interfaces;
using StackExchange.Redis;

namespace Namines.Infrastructure.Realtime;

/// <summary>
/// Çok instance'lı dağıtım için presence deposu. Bir peer instance A'ya bağlanıp
/// diğeri instance B'ye bağlandığında, her ikisinin de aynı Redis'i görmesi
/// sayesinde <c>SendSchemaToPeer</c>'ın çapraz-oda doğrulaması doğru çalışır —
/// aksi halde (bellek-içi implementasyonla) her instance yalnızca kendi
/// bağlantılarının üyeliğini bilir ve doğrulama sessizce başarısız olur.
///
/// TTL güvenlik ağıdır: normal akışta <c>OnDisconnectedAsync</c> anahtarı siler.
/// TTL yalnızca sunucu çöktüğü/aniden kapandığı durumlarda Redis'te kalıcı
/// çöp anahtar birikmesini önler.
/// </summary>
public sealed class RedisPresenceStore : IPresenceStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(8);
    private const string KeyPrefix = "namines:presence:";

    private readonly IConnectionMultiplexer _redis;

    public RedisPresenceStore(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    private static string Key(string connectionId) => KeyPrefix + connectionId;

    public async Task SetRoomAsync(string connectionId, string roomId)
    {
        var db = _redis.GetDatabase();
        await db.StringSetAsync(Key(connectionId), roomId, Ttl);
    }

    public async Task<string?> TryGetRoomAsync(string connectionId)
    {
        var db = _redis.GetDatabase();
        var value = await db.StringGetAsync(Key(connectionId));
        return value.IsNullOrEmpty ? null : value.ToString();
    }

    public async Task<string?> RemoveAsync(string connectionId)
    {
        var db = _redis.GetDatabase();
        var key = Key(connectionId);

        // Değeri önce oku (silinen roomId'yi çağırana bildirmek için — hub bunu
        // "ayrılan kullanıcı hangi odadaydı" bildirimi için kullanıyor), sonra sil.
        var value = await db.StringGetAsync(key);
        if (value.IsNullOrEmpty) return null;

        await db.KeyDeleteAsync(key);
        return value.ToString();
    }
}
