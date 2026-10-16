using System.Net;
using System.Net.Http.Json;
using JKiller.Contracts;
using JKiller.Host.Rooms;
using JKiller.Host.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Http.Features;
using System.Security.Claims;
using Xunit;

namespace JKiller.Host.Tests;
public sealed class RoomPresenceTests
{
    [Fact]
    public void PresenceExpiryRevokesMediaAndNotifiesRemainingMembersWithoutRevokingCredential()
    {
        var clock = new TestClock(); var sessions = new SessionStore(clock); var grants = new MediaGrantStore(clock);
        var rooms = new RoomStore(sessions, grants, clock); var invite = rooms.CreateRoom();
        var owner = rooms.Join(invite.Invite, "owner"); var viewer = rooms.Join(invite.Invite, "viewer");
        rooms.Connect(viewer.SessionId, "viewer-tab");
        var publication = rooms.ReservePublish(owner.SessionId); var playback = rooms.GetPlayback(viewer.SessionId, publication.StreamId);
        var changed = new List<RoomSnapshot>(); var revoked = new List<Guid>(); rooms.Changed += changed.Add; rooms.SessionRevoked += revoked.Add;
        clock.Now = clock.Now.AddSeconds(31); rooms.SweepExpired();
        var snapshot = Assert.Single(changed); Assert.Single(snapshot.Participants); Assert.Empty(snapshot.Streams);
        Assert.Equal(viewer.SessionId, snapshot.Participants[0].SessionId); Assert.Contains(owner.SessionId, revoked);
        Assert.False(grants.Authorize(publication.Token, publication.Path, "publish", clock.Now));
        Assert.False(grants.Authorize(playback.Token, publication.Path, "read", clock.Now));
        Assert.NotNull(sessions.Authenticate(owner.SessionToken));
        Assert.Throws<UnauthorizedAccessException>(() => rooms.GetPlayback(owner.SessionId, publication.StreamId));
    }
    [Fact]
    public async Task LastSubscriptionClosingExpiresButAnotherTabKeepsMembership()
    {
        await using var f = await HostFixture.Start(); var invite = f.Invite; var viewer = await f.Join(invite);
        var connections = f.App.Services.GetRequiredService<RoomConnections>(); var rooms = f.App.Services.GetRequiredService<RoomStore>();
        var identity = f.App.Services.GetRequiredService<SessionStore>().FindActive(viewer.SessionId)!;
        var first = new Connection("first"); var second = new Connection("second"); connections.Add(identity, first); connections.Add(identity, second);
        connections.Remove(first.ConnectionId); f.Clock.Now = f.Clock.Now.AddSeconds(31); rooms.SweepExpired();
        Assert.Single(rooms.Snapshot(viewer.SessionId).Participants);
        connections.Remove(second.ConnectionId); f.Clock.Now = f.Clock.Now.AddSeconds(29); rooms.SweepExpired();
        Assert.Single(rooms.Snapshot(viewer.SessionId).Participants);
        f.Clock.Now = f.Clock.Now.AddSeconds(31); rooms.SweepExpired();
        var replacement = await f.Join(invite); Assert.Single(replacement.Room.Participants);
        Assert.DoesNotContain(replacement.Room.Participants, p => p.SessionId == viewer.SessionId);
    }

    [Fact]
    public async Task ReconnectWithinGraceKeepsIdentityAndSubscription()
    {
        await using var f = await HostFixture.Start(); var viewer = await f.Join();
        var connections = f.App.Services.GetRequiredService<RoomConnections>(); var rooms = f.App.Services.GetRequiredService<RoomStore>();
        var identity = f.App.Services.GetRequiredService<SessionStore>().FindActive(viewer.SessionId)!;
        connections.Add(identity, new Connection("before")); connections.Remove("before"); f.Clock.Now = f.Clock.Now.AddSeconds(20);
        connections.Add(identity, new Connection("after")); f.Clock.Now = f.Clock.Now.AddSeconds(31); rooms.SweepExpired();
        Assert.Equal(viewer.SessionId, Assert.Single(rooms.Snapshot(viewer.SessionId).Participants).SessionId);
    }
    [Fact]
    public async Task ParticipantWhoNeverOpenedASubscriptionLosesMembershipAfterGrace()
    {
        await using var f = await HostFixture.Start(); var invite = f.Invite;
        var departed = await f.Join(invite);
        for (var i = 1; i < 8; i++) await f.Join(invite);
        f.Clock.Now = f.Clock.Now.AddSeconds(31);
        var replacement = await f.Join(invite, "replacement");
        Assert.Single(replacement.Room.Participants);
        Assert.DoesNotContain(replacement.Room.Participants, p => p.SessionId == departed.SessionId);
    }

    [Fact]
    public async Task NativeHeartbeatKeepsMembershipWithoutASubscription()
    {
        await using var f = await HostFixture.Start(); var invite = f.Invite; var owner = await f.Join(invite);
        f.Clock.Now = f.Clock.Now.AddSeconds(20);
        using var response = await f.Client.SendAsync(f.Request(HttpMethod.Post, "/api/me/presence", owner.SessionToken));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        f.Clock.Now = f.Clock.Now.AddSeconds(20);
        var other = await f.Join(invite);
        Assert.Contains(other.Room.Participants, p => p.SessionId == owner.SessionId);
    }

    [Fact]
    public async Task ExplicitLeaveImmediatelyRemovesMembershipAndMediaPermissions()
    {
        await using var f = await HostFixture.Start(); var invite = f.Invite; var owner = await f.Join(invite);
        var viewer = await f.Join(invite);
        for (var i = 2; i < 12; i++) await f.Join(invite);
        var rooms = f.App.Services.GetRequiredService<RoomStore>();
        var stream = rooms.ReservePublish(owner.SessionId); var read = rooms.GetPlayback(viewer.SessionId, stream.StreamId);
        using var left = await f.Client.SendAsync(f.Request(HttpMethod.Post, "/api/me/leave", owner.SessionToken));
        Assert.Equal(HttpStatusCode.OK, left.StatusCode);
        var replacement = await f.Join(invite);
        Assert.Equal(12, replacement.Room.Participants.Count); Assert.Empty(replacement.Room.Streams);
        var grants = f.App.Services.GetRequiredService<MediaGrantStore>();
        Assert.False(grants.Authorize(stream.Token, stream.Path, "publish", f.Clock.Now));
        Assert.False(grants.Authorize(read.Token, stream.Path, "read", f.Clock.Now));
        using var afterLeave = await f.Client.SendAsync(f.Request(HttpMethod.Get, "/api/me", owner.SessionToken));
        Assert.Equal(HttpStatusCode.Unauthorized, afterLeave.StatusCode);
    }

    [Fact]
    public async Task DormantCredentialCanRejoinWithTheSameIdentityAndAFreshMembership()
    {
        await using var f = await HostFixture.Start(); var invite = f.Invite; var departed = await f.Join(invite);
        f.Clock.Now = f.Clock.Now.AddSeconds(31); var remaining = await f.Join(invite);
        Assert.DoesNotContain(remaining.Room.Participants, p => p.SessionId == departed.SessionId);
        using var request = f.Request(HttpMethod.Post, "/api/join", departed.SessionToken, new { invite, nickname = "returned", desktop = true });
        using var response = await f.Client.SendAsync(request); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var resumed = (await response.Content.ReadFromJsonAsync<SessionGrant>())!;
        Assert.Equal(departed.SessionId, resumed.SessionId); Assert.Equal(departed.SessionToken, resumed.SessionToken);
        Assert.Equal(2, resumed.Room.Participants.Count);
        Assert.Equal("returned", Assert.Single(resumed.Room.Participants, p => p.SessionId == departed.SessionId).Nickname);
    }
    private sealed class Connection(string id) : HubCallerContext
    {
        public override string ConnectionId => id;
        public override string? UserIdentifier => null;
        public override ClaimsPrincipal? User => null;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }
}
