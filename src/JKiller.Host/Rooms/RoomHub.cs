using JKiller.Host.Security;
using Microsoft.AspNetCore.SignalR;
namespace JKiller.Host.Rooms;
public sealed class RoomHub(RoomStore rooms) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var session = SessionAuthenticationHandler.Require(Context.GetHttpContext()!);
        await Groups.AddToGroupAsync(Context.ConnectionId, session.RoomId.ToString("N"));
        await Clients.Caller.SendAsync("room", rooms.Snapshot(session.SessionId));
        await base.OnConnectedAsync();
    }
}
