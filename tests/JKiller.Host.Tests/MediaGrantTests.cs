using JKiller.Host.Rooms;
using JKiller.Host.Security;
using Xunit;
namespace JKiller.Host.Tests;
public sealed class MediaGrantTests
{
    [Fact]
    public void GrantCannotOutliveItsSessionAndViewerRevocationInvalidatesRead()
    {
        var clock = new TestClock(); var sessions = new SessionStore(clock); var grants = new MediaGrantStore(clock); var rooms = new RoomStore(sessions, grants, clock);
        var invite = rooms.CreateRoom(); var owner = rooms.Join(invite.Invite, "owner"); var viewer = rooms.Join(invite.Invite, "viewer");
        rooms.Connect(owner.SessionId, "owner-tab");
        var stream = rooms.ReservePublish(owner.SessionId);
        var read = rooms.GetPlayback(viewer.SessionId, stream.StreamId);
        rooms.RevokeSession(viewer.SessionId);
        Assert.False(grants.Authorize(read.Token, stream.Path, "read", clock.Now));
        Assert.True(grants.Authorize(stream.Token, stream.Path, "publish", clock.Now));
        clock.Now = owner.ExpiresAt.AddSeconds(-1);
        // Recreate a publication after the prior lease expires; this assertion
        // isolates the media grant's session-expiry cap, not publisher liveness.
        stream = rooms.ReservePublish(owner.SessionId);
        read = rooms.GetPlayback(owner.SessionId, stream.StreamId);
        Assert.False(grants.Authorize(read.Token, stream.Path, "read", owner.ExpiresAt));
    }
    [Fact]
    public void InviteCannotPublishDirectlyAndSessionCredentialIsSeparate()
    {
        var clock = new TestClock(); var sessions = new SessionStore(clock); var grants = new MediaGrantStore(clock); var rooms = new RoomStore(sessions, grants, clock);
        var invite = rooms.CreateRoom(); var owner = rooms.Join(invite.Invite, "owner"); var stream = rooms.ReservePublish(owner.SessionId);
        Assert.Null(sessions.Authenticate(invite.Invite));
        Assert.Equal(owner.SessionId, sessions.Authenticate(owner.SessionToken)!.SessionId);
        Assert.False(grants.Authorize(invite.Invite, stream.Path, "publish", clock.Now));
        Assert.False(grants.Authorize(owner.SessionToken, stream.Path, "publish", clock.Now));
    }
    [Fact]
    public void GrantRejectsWrongPathActionAndExpiredHandshake()
    {
        var clock = new TestClock(); var grants = new MediaGrantStore(clock); var rooms = new RoomStore(new(clock), grants, clock);
        var owner = rooms.Join(rooms.CreateRoom().Invite, "owner"); var stream = rooms.ReservePublish(owner.SessionId); var read = rooms.GetPlayback(owner.SessionId, stream.StreamId);
        Assert.True(grants.Authorize(stream.Token, stream.Path, "publish", clock.Now));
        Assert.True(grants.Authorize(read.Token, stream.Path, "read", clock.Now));
        Assert.False(grants.Authorize(read.Token, stream.Path, "publish", clock.Now));
        Assert.False(grants.Authorize(read.Token, stream.Path + "/other", "read", clock.Now));
        Assert.False(grants.Authorize(read.Token, stream.Path, "read", clock.Now.AddSeconds(61)));
        rooms.StopPublish(owner.SessionId, stream.StreamId);
        Assert.False(grants.Authorize(stream.Token, stream.Path, "publish", clock.Now));
        Assert.False(grants.Authorize(read.Token, stream.Path, "read", clock.Now));
    }
    [Fact]
    public void PathTraversalCannotBecomeMediaPath()
    {
        var clock = new TestClock(); var grants = new MediaGrantStore(clock); var rooms = new RoomStore(new(clock), grants, clock);
        var owner = rooms.Join(rooms.CreateRoom().Invite, "../evil/#?%2f"); var stream = rooms.ReservePublish(owner.SessionId);
        Assert.DoesNotContain("evil", stream.Path); Assert.DoesNotContain("..", stream.Path);
        foreach (var path in new[] { "../" + stream.Path, "/" + stream.Path, stream.Path + "/../main", stream.Path.Replace("/", "%2f"), stream.Path + "?x=1" })
            Assert.False(grants.Authorize(stream.Token, path, "publish", clock.Now));
    }
}
