using System.Net.WebSockets;
using System.Text;
using JKiller.Host.Rooms;
using JKiller.Host.Security;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JKiller.Host.Tests;
public sealed class RoomSocketTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EstablishedSocketClosesWhenSessionIsRevokedOrExpires(bool expires)
    {
        await using var f = await HostFixture.Start(); var invite = f.Invite;
        var viewer = await f.Join(invite);
        f.Clock.Now = f.Clock.Now.AddHours(1); var friend = await f.Join(invite);
        var client = f.App.GetTestServer().CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers.Authorization = "Bearer " + viewer.SessionToken;
        using var socket = await client.ConnectAsync(new Uri("wss://room.test/hubs/room"), CancellationToken.None);
        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e"), WebSocketMessageType.Text, true, CancellationToken.None);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var buffer = new byte[16384];
        while (true)
        {
            var received = await socket.ReceiveAsync(buffer.AsMemory(), deadline.Token);
            if (Encoding.UTF8.GetString(buffer, 0, received.Count).Contains("\"target\":\"room\"")) break;
        }
        if (expires) f.Clock.Now = viewer.ExpiresAt;
        else f.App.Services.GetRequiredService<SessionStore>().Revoke(viewer.SessionId);
        f.App.Services.GetRequiredService<RoomStore>().Rename(friend.SessionId, "AFTER-REVOCATION");
        var closed = false;
        try
        {
            while (!closed)
            {
                var received = await socket.ReceiveAsync(buffer.AsMemory(), deadline.Token);
                var text = Encoding.UTF8.GetString(buffer, 0, received.Count);
                Assert.DoesNotContain("AFTER-REVOCATION", text);
                closed = received.MessageType == WebSocketMessageType.Close || text.Contains("\"type\":7");
            }
        }
        catch (WebSocketException) { closed = true; }
        Assert.True(closed);
    }
}
