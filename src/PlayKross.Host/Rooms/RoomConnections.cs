using PlayKross.Contracts;
using PlayKross.Host.Security;
using Microsoft.AspNetCore.SignalR;

namespace PlayKross.Host.Rooms;
// Retain the abort capability, never a bearer credential. Delivery is addressed
// only to currently active session IDs rather than permanent room groups.
public sealed class RoomConnections : IDisposable
{
    private readonly SessionStore sessions;
    private readonly RoomStore rooms;
    private readonly object gate = new();
    private readonly Dictionary<string, (Guid Session, HubCallerContext Context)> connections = [];
    public RoomConnections(SessionStore sessions, RoomStore rooms) { this.sessions = sessions; this.rooms = rooms; sessions.Revoked += Revoke; }
    public void Add(SessionIdentity session, HubCallerContext context)
    {
        rooms.Connect(session.SessionId, context.ConnectionId);
        try
        {
            lock (gate)
            {
                if (sessions.FindActive(session.SessionId) is null || connections.Count >= 4096 || connections.Values.Count(x => x.Session == session.SessionId) >= 8)
                    throw new UnauthorizedAccessException("Room subscription is unavailable.");
                connections.Add(context.ConnectionId, (session.SessionId, context));
            }
        }
        catch { rooms.Disconnect(session.SessionId, context.ConnectionId); context.Abort(); throw; }
    }
    public void Remove(string id)
    {
        (Guid Session, HubCallerContext Context) removed;
        lock (gate) if (!connections.Remove(id, out removed)) return;
        rooms.Disconnect(removed.Session, id);
    }
    private void Revoke(Guid id)
    {
        HubCallerContext[] removed;
        lock (gate)
        {
            removed = connections.Values.Where(x => x.Session == id).Select(x => x.Context).ToArray();
            foreach (var context in removed) connections.Remove(context.ConnectionId);
        }
        // Session revocation also removes membership. No RoomStore callback is
        // needed here; abort outside our lock to avoid lock-order inversions.
        foreach (var context in removed) context.Abort();
    }
    public void Sweep()
    {
        Guid[] expired;
        lock (gate) expired = connections.Values.Select(x => x.Session).Distinct().ToArray();
        foreach (var id in expired) if (sessions.FindActive(id) is null) Revoke(id);
    }
    // Endpoints deliver a mutation's snapshot immediately and RoomUpdatesService
    // delivers the same change shortly after. Revisions identify content, so
    // skip snapshots that are not newer than the last one sent for the room.
    private readonly Dictionary<Guid, long> delivered = [];
    public async Task SendAsync(IHubContext<RoomHub> hub, RoomSnapshot snapshot, CancellationToken token = default)
    {
        lock (gate)
        {
            if (delivered.TryGetValue(snapshot.RoomId, out var last) && snapshot.Revision <= last) return;
            delivered[snapshot.RoomId] = snapshot.Revision;
        }
        try
        {
            Sweep();
            var recipients = snapshot.Participants.Where(p => sessions.FindActive(p.SessionId)?.RoomId == snapshot.RoomId).Select(p => p.SessionId.ToString()).ToArray();
            await hub.Clients.Users(recipients).SendAsync("room", snapshot, token);
        }
        catch
        {
            // Allow a later delivery attempt of this revision after a failed send.
            lock (gate) if (delivered.TryGetValue(snapshot.RoomId, out var last) && last == snapshot.Revision) delivered.Remove(snapshot.RoomId);
            throw;
        }
    }
    public void Dispose()
    {
        sessions.Revoked -= Revoke;
        (Guid Session, HubCallerContext Context)[] removed;
        lock (gate) { removed = connections.Values.ToArray(); connections.Clear(); }
        foreach (var entry in removed) { rooms.Disconnect(entry.Session, entry.Context.ConnectionId); entry.Context.Abort(); }
    }
}
