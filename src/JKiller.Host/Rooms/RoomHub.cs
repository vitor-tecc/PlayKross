using JKiller.Host.Security;
using Microsoft.AspNetCore.SignalR;
namespace JKiller.Host.Rooms;
public sealed class RoomHub(RoomStore rooms, RoomConnections connections) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var session = SessionAuthenticationHandler.Require(Context.GetHttpContext()!);
        connections.Add(session, Context);
        await Clients.Caller.SendAsync("room", rooms.Snapshot(session.SessionId));
        await base.OnConnectedAsync();
    }
    public override Task OnDisconnectedAsync(Exception? exception) { connections.Remove(Context.ConnectionId); return base.OnDisconnectedAsync(exception); }
}
