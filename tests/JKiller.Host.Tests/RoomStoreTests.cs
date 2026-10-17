using JKiller.Host.Rooms;
using JKiller.Host.Security;
using Xunit;
namespace JKiller.Host.Tests;

public sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
    public override long GetTimestamp() => Now.UtcTicks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
}
public sealed class RoomStoreTests
{
    [Fact]
    public void AbandonedPublicationIsReclaimedWithoutExpiringTheOwnersSession()
    {
        var store = Create(); var invite = store.CreateRoom(); var owner = store.Join(invite.Invite, "owner");
        var other = store.Join(invite.Invite, "other"); var old = store.ReservePublish(owner.SessionId);
        store.Connect(owner.SessionId, "owner-tab"); store.Connect(other.SessionId, "other-tab");
        var revoked = new List<string>(); store.StreamRevoked += revoked.Add;
        clock.Now = clock.Now.AddSeconds(61); store.SweepExpired();
        Assert.Empty(store.Snapshot(owner.SessionId).Streams);
        Assert.Equal(old.Path, Assert.Single(revoked));
        Assert.NotEqual(old.StreamId, store.ReservePublish(other.SessionId).StreamId);
    }
    [Fact]
    public void ExpirySweepNotifiesTheRemainingRoomWithARevisionedSnapshot()
    {
        var store = Create(); var room = store.CreateRoom(); var owner = store.Join(room.Invite, "owner"); store.ReservePublish(owner.SessionId);
        store.Connect(owner.SessionId, "owner-tab");
        clock.Now = clock.Now.AddHours(1); var viewer = store.Join(room.Invite, "viewer"); store.Connect(viewer.SessionId, "viewer-tab");
        var changes = new List<JKiller.Contracts.RoomSnapshot>(); store.Changed += changes.Add;
        clock.Now = owner.ExpiresAt; store.SweepExpired();
        var snapshot = Assert.Single(changes);
        Assert.Single(snapshot.Participants); Assert.Equal("viewer", snapshot.Participants[0].Nickname); Assert.Empty(snapshot.Streams);
    }
    private readonly TestClock clock = new();
    private RoomStore Create() => new(new(clock), new(clock), clock);
    [Fact]
    public void RenamePreservesOwnership()
    {
        var store = Create(); var invite = store.CreateRoom(); var owner = store.Join(invite.Invite, "Alice");
        var stream = store.ReservePublish(owner.SessionId); var before = store.Snapshot(owner.SessionId);
        var after = store.Rename(owner.SessionId, " 新名字 <b> ");
        Assert.Equal(before.Streams[0].OwnerSessionId, after.Streams[0].OwnerSessionId);
        Assert.Equal(stream.StreamId, after.Streams[0].StreamId);
        Assert.Equal("新名字 <b>", after.Streams[0].Nickname); Assert.True(after.Revision > before.Revision);
    }
    [Fact]
    public void AnotherSessionCannotStopOrReplaceStream()
    {
        var store = Create(); var invite = store.CreateRoom(); var owner = store.Join(invite.Invite, "owner"); var other = store.Join(invite.Invite, "owner");
        var stream = store.ReservePublish(owner.SessionId);
        Assert.Throws<UnauthorizedAccessException>(() => store.StopPublish(other.SessionId, stream.StreamId));
        var independent = store.ReservePublish(other.SessionId);
        Assert.NotEqual(stream.StreamId, independent.StreamId);
        Assert.Contains(store.Snapshot(owner.SessionId).Streams, x => x.StreamId == stream.StreamId);
        var different = store.Join(store.CreateRoom().Invite, "different");
        Assert.Throws<UnauthorizedAccessException>(() => store.GetPlayback(different.SessionId, stream.StreamId));
    }
    [Fact]
    public async Task ConcurrentReserveCreatesOneStream()
    {
        var store = Create(); var owner = store.Join(store.CreateRoom().Invite, "owner");
        var streams = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => store.ReservePublish(owner.SessionId))));
        Assert.Single(streams.Select(x => x.StreamId).Distinct()); Assert.Single(store.Snapshot(owner.SessionId).Streams);
    }
    [Fact]
    public void JoinAndRejoinBeyondFormerLimit()
    {
        var store = Create(); var invite = store.CreateRoom();
        var members = Enumerable.Range(0, 12).Select(i => store.Join(invite.Invite, $"member {i}")).ToArray();
        var snapshot = store.Snapshot(members[0].SessionId);
        Assert.Equal(12, snapshot.Participants.Count);
        foreach (var member in members.Skip(1)) store.Connect(member.SessionId, $"tab-{member.SessionId}");
        clock.Now = clock.Now.AddSeconds(31); store.SweepExpired();
        Assert.Equal(11, store.Snapshot(members[1].SessionId).Participants.Count);
        snapshot = store.Rejoin(members[0].SessionId, "returned");
        Assert.Equal(12, snapshot.Participants.Count);
        Assert.Equal("returned", Assert.Single(snapshot.Participants, p => p.SessionId == members[0].SessionId).Nickname);
    }
    [Fact]
    public async Task ConcurrentOwnersHaveIndependentStreams()
    {
        var store = Create(); var invite = store.CreateRoom();
        var owners = Enumerable.Range(0, 3).Select(i => store.Join(invite.Invite, $"owner {i}")).ToArray();
        var viewer = store.Join(invite.Invite, "viewer").SessionId;
        var reservations = await Task.WhenAll(owners.Select(owner => Task.Run(() => store.ReservePublish(owner.SessionId))));
        Assert.Equal(3, reservations.Select(x => x.StreamId).Distinct().Count());
        var a = reservations[0]; var b = reservations[1]; var aOwner = owners[0].SessionId;
        Assert.NotEqual(a.Path, b.Path);
        Assert.Throws<UnauthorizedAccessException>(() => store.StopPublish(aOwner, b.StreamId));
        Assert.Throws<UnauthorizedAccessException>(() => store.RenewPublish(aOwner, b.StreamId));
        Assert.Equal(a.StreamId, store.ReservePublish(aOwner).StreamId);
        var revokedPaths = new List<string>(); store.StreamRevoked += revokedPaths.Add;
        store.StopPublish(aOwner, a.StreamId);
        Assert.Contains(store.Snapshot(viewer).Streams, x => x.StreamId == b.StreamId);
        Assert.Contains(store.Snapshot(viewer).Streams, x => x.StreamId == reservations[2].StreamId);
        Assert.Equal(a.Path, Assert.Single(revokedPaths));
        Assert.DoesNotContain(b.Path, revokedPaths);
    }
    [Fact]
    public void SameNicknameLeaveRevokesOnlyItsOwner()
    {
        var store = Create(); var invite = store.CreateRoom();
        var aOwner = store.Join(invite.Invite, "same").SessionId; var bOwner = store.Join(invite.Invite, "same").SessionId;
        var a = store.ReservePublish(aOwner); var b = store.ReservePublish(bOwner);
        var revokedPaths = new List<string>(); store.StreamRevoked += revokedPaths.Add;
        store.Leave(aOwner);
        var snapshot = store.Snapshot(bOwner);
        Assert.Equal(bOwner, Assert.Single(snapshot.Participants).SessionId);
        Assert.Equal(b.StreamId, Assert.Single(snapshot.Streams).StreamId);
        Assert.Equal(a.Path, Assert.Single(revokedPaths));
        Assert.DoesNotContain(b.Path, revokedPaths);
    }
    [Fact]
    public void RoomCountRemainsBounded()
    {
        var store = new RoomStore(new(clock), new(clock), clock, new(Rooms: 2));
        store.CreateRoom(); store.CreateRoom();
        Assert.Throws<InvalidOperationException>(() => store.CreateRoom());
    }
    [Theory]
    [InlineData(0)]
    [InlineData(129)]
    public void InvalidRoomCountRejected(int count) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new RoomStore(new(clock), new(clock), clock, new(Rooms: count)));
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123456789012345678901234567890123")]
    public void InvalidNicknameRejected(string nickname)
    {
        var store = Create(); Assert.Throws<ArgumentException>(() => store.Join(store.CreateRoom().Invite, nickname));
    }
    [Fact]
    public void UnicodeLengthCountsTextElements()
    {
        var store = Create(); var room = store.CreateRoom();
        var family = string.Concat(Enumerable.Repeat("👨‍👩‍👧‍👦", 32));
        Assert.Equal(family, store.Join(room.Invite, family).Room.Participants[0].Nickname);
    }
    [Fact]
    public void SessionExpiryRevokesOwnedMediaAndEmitsDisconnectPath()
    {
        var store = Create(); var owner = store.Join(store.CreateRoom().Invite, "owner"); var stream = store.ReservePublish(owner.SessionId);
        var revoked = new List<string>(); store.StreamRevoked += revoked.Add;
        clock.Now = owner.ExpiresAt;
        Assert.Throws<UnauthorizedAccessException>(() => store.Snapshot(owner.SessionId));
        Assert.Contains(stream.Path, revoked);
    }
    [Fact]
    public void ARunawayCombiningSequenceCannotBypassBoundedNicknameStorage()
    {
        var store = Create(); var nickname = "a" + new string('\u0301', 4096);
        Assert.Throws<ArgumentException>(() => store.Join(store.CreateRoom().Invite, nickname));
    }
}
