using JKiller.Host.Security;
namespace JKiller.Host.Media;
public sealed record MediaResource(Guid Id, Guid OwnerSessionId, Guid? MediaSessionId, string Path, Uri Upstream, byte[] CredentialHash, DateTimeOffset ExpiresAt);
public sealed class MediaResourceStore(TimeProvider clock)
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, MediaResource> entries = [];
    public MediaResource Add(SessionIdentity owner, Guid? mediaId, string path, Uri uri, string credential)
    {
        lock (gate)
        {
            if (entries.Count >= 4096) throw new InvalidOperationException("Media resource capacity reached.");
            var value = new MediaResource(Guid.NewGuid(), owner.SessionId, mediaId, path, uri, OpaqueSecrets.Hash(credential), owner.ExpiresAt);
            entries.Add(value.Id, value); return value;
        }
    }
    public MediaResource? Find(Guid id) { lock (gate) return entries.TryGetValue(id, out var entry) && entry.ExpiresAt > clock.GetUtcNow() ? entry : null; }
    public void Remove(Guid id) { lock (gate) entries.Remove(id); }
    public MediaResource[] All() { lock (gate) return entries.Values.ToArray(); }
    public void RevokePath(string path) { lock (gate) foreach (var id in entries.Where(x => x.Value.Path == path).Select(x => x.Key).ToArray()) entries.Remove(id); }
    public void RevokeOwner(Guid owner) { lock (gate) foreach (var id in entries.Where(x => x.Value.OwnerSessionId == owner).Select(x => x.Key).ToArray()) entries.Remove(id); }
}
