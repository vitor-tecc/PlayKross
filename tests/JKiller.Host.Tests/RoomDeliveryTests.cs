using JKiller.Contracts;
using JKiller.Host.Rooms;
using JKiller.Host.Security;
using Microsoft.AspNetCore.SignalR;
using Xunit;

namespace JKiller.Host.Tests;
public sealed class RoomDeliveryTests
{
    private sealed class RecordingClients : IHubClients, IClientProxy
    {
        public List<RoomSnapshot> Sent { get; } = [];
        public bool Fail { get; set; }
        public IClientProxy All => this;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => this;
        public IClientProxy Client(string connectionId) => this;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => this;
        public IClientProxy Group(string groupName) => this;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => this;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => this;
        public IClientProxy User(string userId) => this;
        public IClientProxy Users(IReadOnlyList<string> userIds) => this;
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new IOException("Simulated delivery failure.");
            Sent.Add((RoomSnapshot)args[0]!); return Task.CompletedTask;
        }
    }
    private sealed class RecordingHub : IHubContext<RoomHub>
    {
        public RecordingClients Recorded { get; } = new();
        public List<RoomSnapshot> Sent => Recorded.Sent;
        public bool Fail { get => Recorded.Fail; set => Recorded.Fail = value; }
        public IHubClients Clients => Recorded;
        public IGroupManager Groups => throw new NotSupportedException();
    }
    private static (RoomStore Rooms, RoomConnections Connections, SessionGrant Member) Room()
    {
        var sessions = new SessionStore(TimeProvider.System);
        var rooms = new RoomStore(sessions, new MediaGrantStore(TimeProvider.System), TimeProvider.System);
        var member = rooms.Join(rooms.CreateRoom().Invite, "friend");
        return (rooms, new RoomConnections(sessions, rooms), member);
    }

    [Fact]
    public async Task EachRoomRevisionIsDeliveredOnceAndStaleRevisionsAreSkipped()
    {
        var (rooms, connections, member) = Room(); var hub = new RecordingHub();
        var renamed = rooms.Rename(member.SessionId, "renamed");
        await connections.SendAsync(hub, renamed);
        await connections.SendAsync(hub, renamed);
        await connections.SendAsync(hub, member.Room);
        Assert.Equal([renamed.Revision], hub.Sent.Select(x => x.Revision));
        var newer = rooms.Rename(member.SessionId, "again");
        await connections.SendAsync(hub, newer);
        Assert.Equal([renamed.Revision, newer.Revision], hub.Sent.Select(x => x.Revision));
    }

    [Fact]
    public async Task AFailedDeliveryCanBeRetried()
    {
        var (rooms, connections, member) = Room(); var hub = new RecordingHub { Fail = true };
        var renamed = rooms.Rename(member.SessionId, "renamed");
        await Assert.ThrowsAsync<IOException>(() => connections.SendAsync(hub, renamed));
        hub.Fail = false;
        await connections.SendAsync(hub, renamed);
        Assert.Single(hub.Sent);
    }
}
