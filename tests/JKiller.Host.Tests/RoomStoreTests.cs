using JKiller.Host.Rooms;
using JKiller.Host.Security;
using Xunit;
namespace JKiller.Host.Tests;

public sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}
public sealed class RoomStoreTests
{
    [Fact]
    public void ExpirySweepNotifiesTheRemainingRoomWithARevisionedSnapshot()
    {
        var store = Create(); var room = store.CreateRoom(); var owner = store.Join(room.Invite, "owner"); store.ReservePublish(owner.SessionId);
        clock.Now = clock.Now.AddHours(1); store.Join(room.Invite, "viewer");
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
        Assert.Throws<InvalidOperationException>(() => store.ReservePublish(other.SessionId));
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
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123456789012345678901234567890123")]
    public void InvalidNicknameRejected(string nickname)
    {
        var store = Create(); Assert.Throws<ArgumentException>(() => store.Join(store.CreateRoom().Invite, nickname));
    }
    [Fact]
    public void UnicodeLengthCountsTextElementsAndCapacityIsBounded()
    {
        var store = Create(); var room = store.CreateRoom();
        var family = string.Concat(Enumerable.Repeat("👨‍👩‍👧‍👦", 32));
        Assert.Equal(family, store.Join(room.Invite, family).Room.Participants[0].Nickname);
        for (int i = 1; i < 8; i++) store.Join(room.Invite, "participant");
        Assert.Throws<InvalidOperationException>(() => store.Join(room.Invite, "ninth"));
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
