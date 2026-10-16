using JKiller.Host.Media;
using JKiller.Host.Rooms;
using JKiller.Host.Security;
using Xunit;
namespace JKiller.Host.Tests;
public sealed class MediaAuthTests
{
    [Fact]
    public async Task RevocationCannotPassAnAuthorizedResourceCommit()
    {
        var clock = new TestClock(); var sessions = new SessionStore(clock); var grants = new MediaGrantStore(clock); var rooms = new RoomStore(sessions, grants, clock);
        var owner = rooms.Join(rooms.CreateRoom().Invite, "owner"); var stream = rooms.ReservePublish(owner.SessionId);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var commit = Task.Run(() => grants.CommitAuthorization(stream.Token, stream.Path, "publish", clock.Now, _ => { entered.Set(); release.Wait(TimeSpan.FromSeconds(3)); return true; }));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
        var revoke = Task.Run(() => rooms.StopPublish(owner.SessionId, stream.StreamId));
        try { await Task.Delay(100); Assert.False(revoke.IsCompleted); }
        finally { release.Set(); await commit; await revoke; }
        Assert.False(grants.Authorize(stream.Token, stream.Path, "publish", clock.Now));
    }
    [Fact]
    public void InvalidCallbackSessionIdCannotBeAuthorized()
    {
        var clock = new TestClock(); var sessions = new SessionStore(clock); var grants = new MediaGrantStore(clock); var rooms = new RoomStore(sessions, grants, clock);
        var owner = rooms.Join(rooms.CreateRoom().Invite, "owner"); var stream = rooms.ReservePublish(owner.SessionId);
        var options = new HostOptions(new("http://localhost:5000"), new("http://127.0.0.1:8889"), new("http://127.0.0.1:9997"), new string('b', 43), new string('a', 43));
        var request = new MediaAuthRequest("", "", stream.Token, "127.0.0.1", "publish", stream.Path, "webrtc", "not-a-uuid", "", "OBS");
        Assert.False(MediaAuthEndpoint.Authorize(request, options, grants, sessions, new(), clock));
        Assert.True(MediaAuthEndpoint.Authorize(request with { Id = Guid.NewGuid().ToString() }, options, grants, sessions, new(), clock));
        Assert.False(MediaAuthEndpoint.Authorize(request with { Id = Guid.NewGuid().ToString(), Action = "read" }, options, grants, sessions, new(), clock));
    }
    [Fact]
    public async Task OwnershipRevocationDisconnectsTrackedMediaAndInvalidatesResource()
    {
        await using var host = await HostFixture.Start(); var owner = await host.Join();
        var rooms = (RoomStore)host.App.Services.GetService(typeof(RoomStore))!;
        var tracker = (MediaSessionTracker)host.App.Services.GetService(typeof(MediaSessionTracker))!;
        var resources = (MediaResourceStore)host.App.Services.GetService(typeof(MediaResourceStore))!;
        var publication = rooms.ReservePublish(owner.SessionId); var mediaId = Guid.NewGuid();
        tracker.Track(mediaId, owner.SessionId, publication.Path, owner.ExpiresAt);
        var resource = resources.Add(new(owner.SessionId, owner.RoomId, owner.ExpiresAt), mediaId, publication.Path, new("http://127.0.0.1:8889/resource"), "credential");
        rooms.StopPublish(owner.SessionId, publication.StreamId);
        Assert.Null(resources.Find(resource.Id));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!host.Control.Paths.Contains(publication.Path)) await Task.Delay(10, timeout.Token);
        Assert.Contains(publication.Path, host.Control.Paths);
    }
    [Fact]
    public async Task HostShutdownDisconnectsKnownSessions()
    {
        await using var host = await HostFixture.Start(); var owner = await host.Join();
        var tracker = (MediaSessionTracker)host.App.Services.GetService(typeof(MediaSessionTracker))!; var mediaId = Guid.NewGuid();
        tracker.Track(mediaId, owner.SessionId, "path", owner.ExpiresAt);
        await host.App.StopAsync(); Assert.Contains(mediaId, host.Control.Ids);
    }
}
