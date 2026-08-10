namespace Namines.Core.Interfaces;

/// <summary>
/// Bağlantı → oda üyeliği eşlemesi. CanvasHub'ın çapraz-oda doğrulaması (peer'a
/// şema göndermeden önce çağıranla hedefin AYNI odada olduğunu kontrol etme) için.
///
/// Faz 1'de bu eşleme <c>static ConcurrentDictionary</c> idi — tek instance'ta
/// çalışır, ama API iki instance'la ayağa kalktığında bir peer instance A'ya,
/// diğeri instance B'ye bağlanırsa hiçbiri diğerinin üyeliğini göremez ve
/// SendSchemaToPeer sessizce hiçbir şey yapmaz. Bu arayüz sayesinde çok
/// instance'lı dağıtımda <see cref="Namines.Infrastructure"/> katmanındaki
/// Redis tabanlı implementasyon devreye girer; Redis yapılandırılmamışsa
/// bellek-içi implementasyon tek instance davranışını korur.
/// </summary>
public interface IPresenceStore
{
    Task SetRoomAsync(string connectionId, string roomId);

    Task<string?> TryGetRoomAsync(string connectionId);

    /// <summary>Kaydı siler ve varsa silinen roomId'yi döndürür (bağlantı kopunca bildirim için).</summary>
    Task<string?> RemoveAsync(string connectionId);
}
