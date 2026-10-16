namespace JKiller.Host.Security;
public sealed record SessionIdentity(Guid SessionId, Guid RoomId, DateTimeOffset ExpiresAt);
public sealed class SessionStore(TimeProvider clock)
{
    private sealed record Entry(SessionIdentity Identity, byte[] CredentialHash);
    private readonly object gate = new();
    private readonly Dictionary<Guid, Entry> entries = [];
    public event Action<Guid>? Revoked;
    public (SessionIdentity Identity, string Token) Issue(Guid roomId)
    {
        lock (gate)
        {
            foreach (var id in entries.Where(x => x.Value.Identity.ExpiresAt <= clock.GetUtcNow()).Select(x => x.Key).ToArray()) entries.Remove(id);
            if (entries.Count >= 1024) throw new InvalidOperationException("Session capacity reached.");
            var identity = new SessionIdentity(Guid.NewGuid(), roomId, clock.GetUtcNow().AddHours(12));
            var token = OpaqueSecrets.Create('s', identity.SessionId);
            entries.Add(identity.SessionId, new(identity, OpaqueSecrets.Hash(token)));
            return (identity, token);
        }
    }
    public SessionIdentity? Authenticate(string token)
    {
        if (!OpaqueSecrets.TryId(token, 's', out var id)) return null;
        lock (gate)
            return entries.TryGetValue(id, out var entry) && entry.Identity.ExpiresAt > clock.GetUtcNow() && OpaqueSecrets.Matches(token, entry.CredentialHash) ? entry.Identity : null;
    }
    public SessionIdentity? FindActive(Guid id)
    {
        lock (gate) return entries.TryGetValue(id, out var entry) && entry.Identity.ExpiresAt > clock.GetUtcNow() ? entry.Identity : null;
    }
    public void Revoke(Guid id) { lock (gate) entries.Remove(id); Revoked?.Invoke(id); }
}
