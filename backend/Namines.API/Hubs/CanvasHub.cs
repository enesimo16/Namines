using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Namines.Core.Models;

namespace Namines.API.Hubs;

public class CanvasHub : Hub
{
    public async Task JoinRoom(string roomId, string userName)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
        await Clients.OthersInGroup(roomId).SendAsync("ReceiveUserJoined", Context.ConnectionId, userName);
    }

    public async Task MoveCursor(string roomId, string userName, double x, double y)
    {
        await Clients.OthersInGroup(roomId).SendAsync("ReceiveCursor", Context.ConnectionId, userName, x, y);
    }

    public async Task UpdateSchema(string roomId, DatabaseSchema schema)
    {
        await Clients.OthersInGroup(roomId).SendAsync("ReceiveSchema", schema);
    }

    public async Task SendSchemaToPeer(string peerConnectionId, DatabaseSchema schema)
    {
        await Clients.Client(peerConnectionId).SendAsync("ReceiveSchema", schema);
    }
}
