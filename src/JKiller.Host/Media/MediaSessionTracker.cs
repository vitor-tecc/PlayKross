namespace JKiller.Host.Media;
public sealed record TrackedMediaSession(Guid Id, Guid Owner, string Path, DateTimeOffset ExpiresAt);
public sealed class MediaSessionTracker
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, TrackedMediaSession> sessions = [];
    public bool Track(Guid id, Guid owner, string path, DateTimeOffset expiry)
    {
        lock (gate)
        {
            if (!sessions.ContainsKey(id) && sessions.Count >= 4096) return false;
            sessions[id] = new(id, owner, path, expiry); return true;
        }
    }
    public TrackedMediaSession[] ForOwner(Guid owner) { lock (gate) return sessions.Values.Where(x => x.Owner == owner).ToArray(); }
    public TrackedMediaSession[] ForPath(string path) { lock (gate) return sessions.Values.Where(x => x.Path == path).ToArray(); }
    public TrackedMediaSession[] All() { lock (gate) return sessions.Values.ToArray(); }
    public void Remove(Guid id) { lock (gate) sessions.Remove(id); }
    public void RemovePath(string path) { lock (gate) foreach (var id in sessions.Where(x => x.Value.Path == path).Select(x => x.Key).ToArray()) sessions.Remove(id); }
}
