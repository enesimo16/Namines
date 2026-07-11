using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Namines.Core.Models;

namespace Namines.API.Hubs;

// NOT: Odalar "paylaşım-linki capability" modeliyle çalışır (tahmin edilemez roomId).
// Guest erişimi tasarım gereğidir. roomId biçimi doğrulanır ve bağlantı→oda üyeliği
// takip edilerek peer-mesajları yalnızca aynı odadaki bağlantılara gönderilir.
public class CanvasHub : Hub
{
    // connectionId -> roomId üyelik haritası (peer doğrulaması ve leave bildirimi için).
    private static readonly ConcurrentDictionary<string, string> Membership = new();

    private static readonly Regex RoomIdPattern = new(@"^[A-Za-z0-9_\-]{1,100}$", RegexOptions.Compiled);
    private static bool IsValidRoomId(string? roomId) => !string.IsNullOrEmpty(roomId) && RoomIdPattern.IsMatch(roomId);
    private static string Trim(string? s, int max) => string.IsNullOrEmpty(s) ? string.Empty : (s.Length > max ? s.Substring(0, max) : s);

    public async Task JoinRoom(string roomId, string userName)
    {
        if (!IsValidRoomId(roomId)) return;
        Membership[Context.ConnectionId] = roomId;
        await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
        await Clients.OthersInGroup(roomId).SendAsync("ReceiveUserJoined", Context.ConnectionId, Trim(userName, 64));
    }

    public async Task MoveCursor(string roomId, string userName, double x, double y)
    {
        if (!IsValidRoomId(roomId)) return;
        await Clients.OthersInGroup(roomId).SendAsync("ReceiveCursor", Context.ConnectionId, Trim(userName, 64), x, y);
    }

    public async Task UpdateSchema(string roomId, DatabaseSchema schema)
    {
        if (!IsValidRoomId(roomId)) return;
        await Clients.OthersInGroup(roomId).SendAsync("ReceiveSchema", schema);
    }

    // Yalnızca çağıran ile hedefin AYNI odada olması durumunda şema gönderilir
    // (cross-room şema enjeksiyonunu engeller).
    public async Task SendSchemaToPeer(string peerConnectionId, DatabaseSchema schema)
    {
        if (string.IsNullOrEmpty(peerConnectionId)) return;
        if (!Membership.TryGetValue(Context.ConnectionId, out var callerRoom)) return;
        if (!Membership.TryGetValue(peerConnectionId, out var targetRoom)) return;
        if (callerRoom != targetRoom) return;
        await Clients.Client(peerConnectionId).SendAsync("ReceiveSchema", schema);
    }

    public override async Task OnDisconnectedAsync(System.Exception? exception)
    {
        // Peer ayrılınca odadakilere bildir → hayalet imleç kalmaz.
        if (Membership.TryRemove(Context.ConnectionId, out var roomId) && IsValidRoomId(roomId))
        {
            await Clients.OthersInGroup(roomId).SendAsync("ReceiveUserLeft", Context.ConnectionId);
        }
        await base.OnDisconnectedAsync(exception);
    }
}
