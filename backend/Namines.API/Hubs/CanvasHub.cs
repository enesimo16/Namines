using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Core.Realtime;

namespace Namines.API.Hubs;

// NOT: Odalar "paylaşım-linki capability" modeliyle çalışır (tahmin edilemez roomId,
// crypto.randomUUID() ile üretilir). Guest erişimi TASARIM GEREĞİDİR — girişsiz
// kullanıcılar da /canvas sayfasında gerçek zamanlı işbirliği yapabilmeli. Bu yüzden
// hub'a bağlanmak JWT ZORUNLU DEĞİL.
//
// G17 — new-phase/30-SERVER-SIDE-BRANCHING.md §3 Adım 2: kimliği doğrulanmış bir
// kullanıcının aktif bir projesi varsa, frontend artık roomId olarak rastgele bir
// string yerine BranchController.GetOrCreateDefaultBranch'ten dönen gerçek branch
// ID'sini kullanıyor (bkz. frontend hooks/useMultiplayer.ts). Bu sınıf DEĞİŞMEDİ —
// hub, roomId'nin rastgele mi yoksa bir branch ID'si mi olduğunu bilmiyor/bilmesi
// gerekmiyor (IPresenceStore aynı kalır, doc'un öngördüğü tam olarak bu).
//
// Ama kimliği doğrulanmış (giriş yapmış) bir kullanıcı bağlanıyorsa, sunum adı için
// istemcinin gönderdiği serbest metin yerine JWT claim'inden gelen gerçek adı
// kullanıyoruz — böylece giriş yapmış bir kullanıcının kimliğine bürünmek (başka
// birinin adını yazıp onun gibi görünmek) mümkün olmuyor. Anonim/guest kullanıcılar
// için davranış değişmedi.
public class CanvasHub : Hub
{
    private readonly IPresenceStore _presence;

    public CanvasHub(IPresenceStore presence)
    {
        _presence = presence;
    }

    private static readonly Regex RoomIdPattern = new(@"^[A-Za-z0-9_\-]{1,100}$", RegexOptions.Compiled);
    private static bool IsValidRoomId(string? roomId) => !string.IsNullOrEmpty(roomId) && RoomIdPattern.IsMatch(roomId);

    private string ResolveDisplayName(string clientSuppliedName) =>
        PresenceIdentity.ResolveDisplayName(Context.User, clientSuppliedName);

    public async Task JoinRoom(string roomId, string userName)
    {
        if (!IsValidRoomId(roomId)) return;
        await _presence.SetRoomAsync(Context.ConnectionId, roomId);
        await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
        await Clients.OthersInGroup(roomId).SendAsync("ReceiveUserJoined", Context.ConnectionId, ResolveDisplayName(userName));
    }

    public async Task MoveCursor(string roomId, string userName, double x, double y)
    {
        if (!IsValidRoomId(roomId)) return;
        await Clients.OthersInGroup(roomId).SendAsync("ReceiveCursor", Context.ConnectionId, ResolveDisplayName(userName), x, y);
    }

    public async Task UpdateSchema(string roomId, DatabaseSchema schema)
    {
        if (!IsValidRoomId(roomId)) return;
        await Clients.OthersInGroup(roomId).SendAsync("ReceiveSchema", schema);
    }

    // Yalnızca çağıran ile hedefin AYNI odada olması durumunda şema gönderilir
    // (cross-room şema enjeksiyonunu engeller). Üyelik artık IPresenceStore
    // üzerinden okunuyor: Redis yapılandırılmışsa bu kontrol çok instance'lı
    // dağıtımda da doğru çalışır — bellek-içi static dictionary yalnızca
    // kendi instance'ının bağlantılarını görebiliyordu.
    public async Task SendSchemaToPeer(string peerConnectionId, DatabaseSchema schema)
    {
        if (string.IsNullOrEmpty(peerConnectionId)) return;

        var callerRoom = await _presence.TryGetRoomAsync(Context.ConnectionId);
        if (callerRoom is null) return;

        var targetRoom = await _presence.TryGetRoomAsync(peerConnectionId);
        if (targetRoom is null) return;

        if (callerRoom != targetRoom) return;
        await Clients.Client(peerConnectionId).SendAsync("ReceiveSchema", schema);
    }

    public override async Task OnDisconnectedAsync(System.Exception? exception)
    {
        // Peer ayrılınca odadakilere bildir → hayalet imleç kalmaz.
        var roomId = await _presence.RemoveAsync(Context.ConnectionId);
        if (roomId is not null && IsValidRoomId(roomId))
        {
            await Clients.OthersInGroup(roomId).SendAsync("ReceiveUserLeft", Context.ConnectionId);
        }
        await base.OnDisconnectedAsync(exception);
    }
}
