namespace JKiller.Host.Security;
public sealed class MediaGrantStore(TimeProvider clock)
{
    private sealed record Entry(byte[] CredentialHash, Guid SessionId, string Path, string Action, DateTimeOffset ExpiresAt);
    private readonly object gate = new();
    private readonly Dictionary<Guid, Entry> entries = [];
    public (string Token, DateTimeOffset ExpiresAt) Issue(Guid sessionId, string path, string action, DateTimeOffset sessionExpiresAt)
    {
        if (!IsGeneratedPath(path) || action is not ("read" or "publish")) throw new ArgumentException("Only generated media paths and scoped actions can receive grants.");
        lock (gate)
        {
            foreach (var expiredId in entries.Where(x => x.Value.ExpiresAt <= clock.GetUtcNow()).Select(x => x.Key).ToArray()) entries.Remove(expiredId);
            if (entries.Count >= 4096) throw new InvalidOperationException("Media handshake capacity reached; retry after old grants expire.");
            var now = clock.GetUtcNow();
            if (sessionExpiresAt <= now) throw new UnauthorizedAccessException("Session expired before the media handshake.");
            var id = Guid.NewGuid(); var token = OpaqueSecrets.Create('m', id); var expiry = now.AddSeconds(60);
            if (expiry > sessionExpiresAt) expiry = sessionExpiresAt;
            entries.Add(id, new(OpaqueSecrets.Hash(token), sessionId, path, action, expiry));
            return (token, expiry);
        }
    }
    public bool Authorize(string token, string path, string action, DateTimeOffset now)
    {
        if (!IsGeneratedPath(path) || !OpaqueSecrets.TryId(token, 'm', out var id)) return false;
        lock (gate)
            return entries.TryGetValue(id, out var entry) && entry.ExpiresAt > now && entry.Path == path && entry.Action == action && OpaqueSecrets.Matches(token, entry.CredentialHash);
    }
    public Guid? GetAuthorizedSession(string token, string path, string action, DateTimeOffset now)
    {
        if (!IsGeneratedPath(path) || !OpaqueSecrets.TryId(token, 'm', out var id)) return null;
        lock (gate)
            return entries.TryGetValue(id, out var entry) && entry.ExpiresAt > now && entry.Path == path && entry.Action == action && OpaqueSecrets.Matches(token, entry.CredentialHash) ? entry.SessionId : null;
    }
    public void RevokePath(string path) { lock (gate) foreach (var id in entries.Where(x => x.Value.Path == path).Select(x => x.Key).ToArray()) entries.Remove(id); }
    public void RevokeSession(Guid sessionId) { lock (gate) foreach (var id in entries.Where(x => x.Value.SessionId == sessionId).Select(x => x.Key).ToArray()) entries.Remove(id); }
    public static bool IsGeneratedPath(string? path)
    {
        if (path is null) return false;
        var parts = path.Split('/');
        return parts.Length == 5 && parts[0] == "rooms" && parts[2] == "streams" && parts[4] == "main" &&
            Guid.TryParseExact(parts[1], "N", out var room) && room != Guid.Empty && Guid.TryParseExact(parts[3], "N", out var stream) && stream != Guid.Empty &&
            parts[1] == room.ToString("N") && parts[3] == stream.ToString("N");
    }
}
