namespace JKiller.Contracts;

public sealed record RoomInvite(Guid RoomId, string Invite);
public sealed record SessionGrant(Guid RoomId, Guid SessionId, string SessionToken, DateTimeOffset ExpiresAt, RoomSnapshot Room);
public sealed record ParticipantSnapshot(Guid SessionId, string Nickname);
public sealed record StreamSnapshot(Guid StreamId, Guid OwnerSessionId, string Nickname, string Status);
public sealed record RoomSnapshot(Guid RoomId, long Revision, IReadOnlyList<ParticipantSnapshot> Participants, IReadOnlyList<StreamSnapshot> Streams);
public sealed record PublishReservation(Guid StreamId, string Path, Uri WhipUri, string Token, DateTimeOffset ExpiresAt);
public sealed record PlaybackGrant(Guid StreamId, string Path, Uri WhepUri, string Token, DateTimeOffset ExpiresAt);
