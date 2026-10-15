using JKiller.Contracts;
using JKiller.Host.Security;
using System.Globalization;
namespace JKiller.Host.Rooms;
public sealed record RoomLimits(int Participants = 8, int Publishers = 1, int Rooms = 16);
public sealed class RoomStore(SessionStore sessions, MediaGrantStore grants, TimeProvider clock, RoomLimits? limits = null)
{
    private sealed class Room(Guid id, byte[] inviteHash)
    {
        public Guid Id = id;
        public byte[] InviteHash = inviteHash;
        public long Revision = 1;
        public Dictionary<Guid, Participant> Participants = [];
        public Dictionary<Guid, Publication> Streams = [];
    }
    private sealed record Publication(Guid Id, Guid Owner, string Path, long RenewedAt);
    private sealed class Participant(string nickname, long renewedAt)
    {
        public string Nickname = nickname;
        public long RenewedAt = renewedAt;
        public HashSet<string> Connections = [];
    }
    private readonly RoomLimits limits = ValidateLimits(limits ?? new());
    private readonly object gate = new();
    private readonly Dictionary<Guid, Room> rooms = [];
    public event Action<string>? StreamRevoked;
    public event Action<Guid>? SessionRevoked;
    public event Action<RoomSnapshot>? Changed;
    private void NotifyChanged(RoomSnapshot snapshot) => Changed?.Invoke(snapshot);
    public RoomInvite CreateRoom() => Run(() =>
    {
        if (rooms.Count >= limits.Rooms) throw new InvalidOperationException("Room capacity reached.");
        var id = Guid.NewGuid(); var invite = OpaqueSecrets.Create('i', id);
        rooms.Add(id, new(id, OpaqueSecrets.Hash(invite))); return new RoomInvite(id, invite);
    });
    public bool MatchesInvite(Guid roomId, string invite) => Run(() =>
        OpaqueSecrets.TryId(invite, 'i', out var supplied) && supplied == roomId && rooms.TryGetValue(roomId, out var room) && OpaqueSecrets.Matches(invite, room.InviteHash));
    public SessionGrant Join(string invite, string nickname) => Run(() =>
    {
        var name = ValidateNickname(nickname);
        if (!OpaqueSecrets.TryId(invite, 'i', out var roomId) || !rooms.TryGetValue(roomId, out var room) || !OpaqueSecrets.Matches(invite, room.InviteHash))
            throw new UnauthorizedAccessException("Room invitation is invalid.");
        if (room.Participants.Count >= limits.Participants) throw new InvalidOperationException("This room is full.");
        var session = sessions.Issue(room.Id); room.Participants.Add(session.Identity.SessionId, new(name, clock.GetTimestamp())); room.Revision++;
        return new SessionGrant(room.Id, session.Identity.SessionId, session.Token, session.Identity.ExpiresAt, Snapshot(room));
    });
    public RoomSnapshot Rename(Guid sessionId, string nickname) => Run(() =>
    {
        var name = ValidateNickname(nickname); var room = RequireRoom(sessionId); room.Participants[sessionId].Nickname = name; room.Revision++; return Snapshot(room);
    });
    // A credential may outlive presence. Rejoining still requires the invitation
    // and available room capacity, and restores no previous media permissions.
    public RoomSnapshot Rejoin(Guid sessionId, string nickname) => Run(() =>
    {
        var name = ValidateNickname(nickname); var session = sessions.FindActive(sessionId);
        if (session is null || !rooms.TryGetValue(session.RoomId, out var room)) throw new UnauthorizedAccessException("Session expired. Join again.");
        if (!room.Participants.ContainsKey(sessionId))
        {
            if (room.Participants.Count >= limits.Participants) throw new InvalidOperationException("This room is full.");
            room.Participants.Add(sessionId, new(name, clock.GetTimestamp()));
        }
        else { room.Participants[sessionId].Nickname = name; room.Participants[sessionId].RenewedAt = clock.GetTimestamp(); }
        room.Revision++; return Snapshot(room);
    });
    public RoomSnapshot Heartbeat(Guid sessionId) => Snapshot(sessionId);
    public RoomSnapshot Leave(Guid sessionId) => Run(() =>
    {
        var session = sessions.FindActive(sessionId);
        if (session is null || !rooms.TryGetValue(session.RoomId, out var room)) throw new UnauthorizedAccessException("Session expired. Join again.");
        RemoveParticipant(room, sessionId); sessions.Revoke(sessionId); return Snapshot(room);
    });
    public void Connect(Guid sessionId, string connectionId) => Run(() =>
    {
        var room = RequireRoom(sessionId); room.Participants[sessionId].Connections.Add(connectionId); return true;
    });
    public void Disconnect(Guid sessionId, string connectionId) => Run(() =>
    {
        foreach (var room in rooms.Values)
            if (room.Participants.TryGetValue(sessionId, out var participant) && participant.Connections.Remove(connectionId))
                participant.RenewedAt = clock.GetTimestamp();
        return true;
    });
    public RoomSnapshot Snapshot(Guid sessionId) => Run(() => Snapshot(RequireRoom(sessionId)));
    public PublishReservation ReservePublish(Guid sessionId) => Run(() =>
    {
        var room = RequireRoom(sessionId);
        var publication = room.Streams.Values.SingleOrDefault(x => x.Owner == sessionId);
        if (publication is null)
        {
            if (room.Streams.Count >= limits.Publishers) throw new InvalidOperationException("This milestone allows one publisher per room.");
            var id = Guid.NewGuid(); publication = new(id, sessionId, $"rooms/{room.Id:N}/streams/{id:N}/main", clock.GetTimestamp());
            room.Streams.Add(id, publication); room.Revision++;
        }
        room.Streams[publication.Id] = publication with { RenewedAt = clock.GetTimestamp() };
        var grant = grants.Issue(sessionId, publication.Path, "publish", sessions.FindActive(sessionId)!.ExpiresAt);
        return new PublishReservation(publication.Id, publication.Path, new Uri($"/media/{publication.Path}/whip", UriKind.Relative), grant.Token, grant.ExpiresAt);
    });
    public RoomSnapshot RenewPublish(Guid sessionId, Guid streamId) => Run(() =>
    {
        var room = RequireRoom(sessionId);
        if (!room.Streams.TryGetValue(streamId, out var stream) || stream.Owner != sessionId)
            throw new UnauthorizedAccessException("Publication lease is no longer owned by this session.");
        room.Streams[streamId] = stream with { RenewedAt = clock.GetTimestamp() };
        return Snapshot(room);
    });
    public RoomSnapshot StopPublish(Guid sessionId, Guid streamId) => Run(() =>
    {
        var room = RequireRoom(sessionId);
        if (!room.Streams.TryGetValue(streamId, out var stream) || stream.Owner != sessionId) throw new UnauthorizedAccessException("Only the owning session may stop this stream.");
        room.Streams.Remove(streamId); room.Revision++; Revoke(stream.Path); return Snapshot(room);
    });
    public PlaybackGrant GetPlayback(Guid sessionId, Guid streamId) => Run(() =>
    {
        var room = RequireRoom(sessionId);
        if (!room.Streams.TryGetValue(streamId, out var stream)) throw new UnauthorizedAccessException("Stream does not belong to this room.");
        var grant = grants.Issue(sessionId, stream.Path, "read", sessions.FindActive(sessionId)!.ExpiresAt);
        return new PlaybackGrant(stream.Id, stream.Path, new Uri($"/media/{stream.Path}/whep", UriKind.Relative), grant.Token, grant.ExpiresAt);
    });
    public void RevokeSession(Guid sessionId) => Run(() => { sessions.Revoke(sessionId); Purge(); return true; });
    public void SweepExpired() => Run(() => true);
    private Room RequireRoom(Guid sessionId)
    {
        var session = sessions.FindActive(sessionId);
        if (session is null || session.ExpiresAt <= clock.GetUtcNow() || !rooms.TryGetValue(session.RoomId, out var room) || !room.Participants.ContainsKey(sessionId)) throw new UnauthorizedAccessException("Session expired or was revoked. Join the room again.");
        room.Participants[sessionId].RenewedAt = clock.GetTimestamp();
        return room;
    }
    private static RoomSnapshot Snapshot(Room room) => new(room.Id, room.Revision,
        room.Participants.Select(x => new ParticipantSnapshot(x.Key, x.Value.Nickname)).ToArray(),
        room.Streams.Values.Select(x => new StreamSnapshot(x.Id, x.Owner, room.Participants[x.Owner].Nickname, "reserved")).ToArray());
    private List<string> revokedPaths = [];
    private List<Guid> revokedSessions = [];
    private void Revoke(string path) { grants.RevokePath(path); revokedPaths.Add(path); }
    private void Purge()
    {
        foreach (var room in rooms.Values)
        {
            foreach (var stream in room.Streams.Values.Where(x => clock.GetElapsedTime(x.RenewedAt) >= TimeSpan.FromSeconds(60)).ToArray())
            { room.Streams.Remove(stream.Id); room.Revision++; Revoke(stream.Path); }
            foreach (var id in room.Participants.Where(x => sessions.FindActive(x.Key) is null ||
                x.Value.Connections.Count == 0 && clock.GetElapsedTime(x.Value.RenewedAt) >= TimeSpan.FromSeconds(30)).Select(x => x.Key).ToArray())
            {
                RemoveParticipant(room, id);
                if (sessions.FindActive(id) is null) sessions.Revoke(id);
            }
        }
    }
    private void RemoveParticipant(Room room, Guid id)
    {
        if (!room.Participants.Remove(id)) return;
        foreach (var stream in room.Streams.Values.Where(x => x.Owner == id).ToArray()) { room.Streams.Remove(stream.Id); Revoke(stream.Path); }
        room.Revision++; grants.RevokeSession(id); revokedSessions.Add(id);
    }
    private T Run<T>(Func<T> action)
    {
        List<string> paths = []; List<Guid> sessionIds = []; List<RoomSnapshot> changed = [];
        try
        {
            lock (gate)
            {
                var revisions = rooms.ToDictionary(x => x.Key, x => x.Value.Revision);
                try { Purge(); return action(); }
                finally
                {
                    paths = revokedPaths; sessionIds = revokedSessions; revokedPaths = []; revokedSessions = [];
                    changed = rooms.Values.Where(x => !revisions.TryGetValue(x.Id, out var prior) || x.Revision > prior).Select(Snapshot).ToList();
                }
            }
        }
        finally
        {
            foreach (var path in paths) StreamRevoked?.Invoke(path);
            foreach (var sessionId in sessionIds) SessionRevoked?.Invoke(sessionId);
            foreach (var snapshot in changed) NotifyChanged(snapshot);
        }
    }
    private static string ValidateNickname(string nickname)
    {
        ArgumentNullException.ThrowIfNull(nickname);
        var name = nickname.Trim();
        if (name.Length > 1024) throw new ArgumentException("Nickname exceeds the storage limit.");
        var length = StringInfo.ParseCombiningCharacters(name).Length;
        if (length is < 1 or > 32) throw new ArgumentException("Nickname must contain 1–32 Unicode text elements.");
        return name;
    }
    private static RoomLimits ValidateLimits(RoomLimits value) => value.Participants is >= 1 and <= 128 && value.Publishers >= 1 && value.Publishers <= value.Participants && value.Rooms is >= 1 and <= 128 ? value : throw new ArgumentOutOfRangeException(nameof(value));
}
