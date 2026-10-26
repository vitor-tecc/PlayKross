using JKiller.Host.Media;
using JKiller.Host.Rooms;
using JKiller.Host.Security;
using Xunit;
namespace JKiller.Host.Tests;
public sealed class MediaAuthTests
{
    [Theory]
    [InlineData("stop")]
    [InlineData("leave")]
    [InlineData("lease")]
    [InlineData("presence")]
    public async Task RevokingOnePublisherPreservesOtherPublisherGrantsAndTrackedResources(string reason)
    {
        await using var host = await HostFixture.Start(); var invite = host.Invite;
        var aOwner = await host.Join(invite); var bOwner = await host.Join(invite); var viewer = await host.Join(invite);
        var rooms = (RoomStore)host.App.Services.GetService(typeof(RoomStore))!;
        var grants = (MediaGrantStore)host.App.Services.GetService(typeof(MediaGrantStore))!;
        var tracker = (MediaSessionTracker)host.App.Services.GetService(typeof(MediaSessionTracker))!;
        var resources = (MediaResourceStore)host.App.Services.GetService(typeof(MediaResourceStore))!;
        rooms.Connect(bOwner.SessionId, "b-tab"); rooms.Connect(viewer.SessionId, "viewer-tab");
        if (reason != "presence") rooms.Connect(aOwner.SessionId, "a-tab");
        var a = rooms.ReservePublish(aOwner.SessionId); var b = rooms.ReservePublish(bOwner.SessionId);
        var aId = Guid.NewGuid(); var bId = Guid.NewGuid(); var readId = Guid.NewGuid();
        tracker.Track(aId, aOwner.SessionId, a.Path, aOwner.ExpiresAt);
        tracker.Track(bId, bOwner.SessionId, b.Path, bOwner.ExpiresAt);
        tracker.Track(readId, viewer.SessionId, b.Path, viewer.ExpiresAt);
        var aResource = resources.Add(new(aOwner.SessionId, aOwner.RoomId, aOwner.ExpiresAt), aId, a.Path, new("http://127.0.0.1:8889/a"), a.Token);
        if (reason == "lease")
        {
            host.Clock.Now = host.Clock.Now.AddSeconds(40); rooms.RenewPublish(bOwner.SessionId, b.StreamId);
            b = rooms.ReservePublish(bOwner.SessionId);
        }
        var bRead = rooms.GetPlayback(viewer.SessionId, b.StreamId);
        var bResource = resources.Add(new(bOwner.SessionId, bOwner.RoomId, bOwner.ExpiresAt), bId, b.Path, new("http://127.0.0.1:8889/b"), b.Token);
        var readResource = resources.Add(new(viewer.SessionId, viewer.RoomId, viewer.ExpiresAt), readId, b.Path, new("http://127.0.0.1:8889/read-b"), bRead.Token);
        if (reason == "stop") rooms.StopPublish(aOwner.SessionId, a.StreamId);
        else if (reason == "leave") rooms.Leave(aOwner.SessionId);
        else { host.Clock.Now = host.Clock.Now.AddSeconds(reason == "lease" ? 21 : 31); rooms.SweepExpired(); }
        Assert.Null(resources.Find(aResource.Id));
        Assert.False(grants.Authorize(a.Token, a.Path, "publish", host.Clock.Now));
        Assert.True(grants.Authorize(b.Token, b.Path, "publish", host.Clock.Now));
        Assert.True(grants.Authorize(bRead.Token, b.Path, "read", host.Clock.Now));
        Assert.NotNull(resources.Find(bResource.Id)); Assert.NotNull(resources.Find(readResource.Id));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!host.Control.Paths.Contains(a.Path)) await Task.Delay(10, timeout.Token);
        Assert.DoesNotContain(b.Path, host.Control.Paths);
        Assert.DoesNotContain(bId, host.Control.Ids); Assert.DoesNotContain(readId, host.Control.Ids);
        Assert.Contains(tracker.ForPath(b.Path), x => x.Id == bId); Assert.Contains(tracker.ForPath(b.Path), x => x.Id == readId);
        Assert.Contains(rooms.Snapshot(viewer.SessionId).Streams, x => x.StreamId == b.StreamId);
    }
    [Theory]
    [InlineData(100)]
    [InlineData(3500)]
    public async Task RevocationCannotPassAnAuthorizedResourceCommit(int assertionDelayMilliseconds)
    {
        var clock = new TestClock(); var sessions = new SessionStore(clock); var grants = new MediaGrantStore(clock); var rooms = new RoomStore(sessions, grants, clock);
        var owner = rooms.Join(rooms.CreateRoom().Invite, "owner"); var stream = rooms.ReservePublish(owner.SessionId);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); using var revoking = new ManualResetEventSlim();
        // Only this test releases the commit. A slow assertion must not expire the held operation.
        // Dedicated threads keep these intentionally blocked operations off the shared worker pool.
        var commit = Task.Factory.StartNew(() => grants.CommitAuthorization(stream.Token, stream.Path, "publish", clock.Now, _ => { entered.Set(); release.Wait(); return true; }), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Task revoke = Task.CompletedTask;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            revoke = Task.Factory.StartNew(() => { revoking.Set(); rooms.StopPublish(owner.SessionId, stream.StreamId); }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Assert.True(revoking.Wait(TimeSpan.FromSeconds(10)));
            await Task.Delay(assertionDelayMilliseconds);
            Assert.False(revoke.IsCompleted);
        }
        finally { release.Set(); await commit.WaitAsync(TimeSpan.FromSeconds(10)); await revoke.WaitAsync(TimeSpan.FromSeconds(10)); }
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
