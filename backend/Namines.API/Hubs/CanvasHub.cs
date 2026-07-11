using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Namines.Core.Models;

namespace Namines.API.Hubs;

// NOT: Odalar "paylaşım-linki capability" modeliyle çalışır (tahmin edilemez roomId).
// Guest erişimi tasarım gereğidir. roomId biçimi doğrulanarak grup-adı istismarı engellenir.
public class CanvasHub : Hub
{
    private static readonly Regex RoomIdPattern = new(@"^[A-Za-z0-9_\-]{1,100}$", RegexOptions.Compiled);
    private static bool IsValidRoomId(string? roomId) => !string.IsNullOrEmpty(roomId) && RoomIdPattern.IsMatch(roomId);
    private static string Trim(string? s, int max) => string.IsNullOrEmpty(s) ? string.Empty : (s.Length > max ? s.Substring(0, max) : s);

    public async Task JoinRoom(string roomId, string userName)
    {
        if (!IsValidRoomId(roomId)) return;
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

    public async Task SendSchemaToPeer(string peerConnectionId, DatabaseSchema schema)
    {
        await Clients.Client(peerConnectionId).SendAsync("ReceiveSchema", schema);
    }
}
