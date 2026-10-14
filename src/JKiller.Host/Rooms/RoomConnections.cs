using JKiller.Contracts;
using JKiller.Host.Security;
using Microsoft.AspNetCore.SignalR;

namespace JKiller.Host.Rooms;
// Retain the abort capability, never a bearer credential. Delivery is addressed
// only to currently active session IDs rather than permanent room groups.
public sealed class RoomConnections : IDisposable
{
    private readonly SessionStore sessions;
    private readonly object gate = new();
    private readonly Dictionary<string, (Guid Session, HubCallerContext Context)> connections = [];
    public RoomConnections(SessionStore sessions) { this.sessions = sessions; sessions.Revoked += Revoke; }
    public void Add(SessionIdentity session, HubCallerContext context)
    {
        lock (gate)
        {
            if (sessions.FindActive(session.SessionId) is null || connections.Count >= 4096 || connections.Values.Count(x => x.Session == session.SessionId) >= 8)
            { context.Abort(); throw new UnauthorizedAccessException("Room subscription is unavailable."); }
            connections.Add(context.ConnectionId, (session.SessionId, context));
        }
    }
    public void Remove(string id) { lock (gate) connections.Remove(id); }
    private void Revoke(Guid id)
    {
        lock (gate)
            foreach (var entry in connections.Where(x => x.Value.Session == id).ToArray())
            { connections.Remove(entry.Key); entry.Value.Context.Abort(); }
    }
    public void Sweep()
    {
        lock (gate)
            foreach (var entry in connections.Where(x => sessions.FindActive(x.Value.Session) is null).ToArray())
            { connections.Remove(entry.Key); entry.Value.Context.Abort(); }
    }
    public Task SendAsync(IHubContext<RoomHub> hub, RoomSnapshot snapshot, CancellationToken token = default)
    {
        Sweep();
        var recipients = snapshot.Participants.Where(p => sessions.FindActive(p.SessionId)?.RoomId == snapshot.RoomId).Select(p => p.SessionId.ToString()).ToArray();
        return hub.Clients.Users(recipients).SendAsync("room", snapshot, token);
    }
    public void Dispose()
    {
        sessions.Revoked -= Revoke;
        lock (gate) { foreach (var entry in connections.Values) entry.Context.Abort(); connections.Clear(); }
    }
}
