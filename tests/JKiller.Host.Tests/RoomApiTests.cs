using System.Net;
using System.Net.Http.Json;
using JKiller.Contracts;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
namespace JKiller.Host.Tests;
public sealed class RoomApiTests
{
    [Fact]
    public async Task LeaseRenewalIsOwnerOnlyAndKeepsTheStreamAcrossRestartGrace()
    {
        await using var f = await HostFixture.Start(); var invite = f.Invite;
        var owner = await f.Join(invite); var other = await f.Join(invite);
        f.App.Services.GetRequiredService<JKiller.Host.Rooms.RoomStore>().Connect(owner.SessionId, "owner-tab");
        var publication = (await (await f.Client.SendAsync(f.Request(HttpMethod.Post, "/api/streams", owner.SessionToken))).Content.ReadFromJsonAsync<PublishReservation>())!;
        var path = $"/api/streams/{publication.StreamId}/lease";
        Assert.Equal(HttpStatusCode.Forbidden, (await f.Client.SendAsync(f.Request(HttpMethod.Post, path, other.SessionToken))).StatusCode);
        f.Clock.Now = f.Clock.Now.AddSeconds(40);
        Assert.Equal(HttpStatusCode.OK, (await f.Client.SendAsync(f.Request(HttpMethod.Post, path, owner.SessionToken))).StatusCode);
        f.Clock.Now = f.Clock.Now.AddSeconds(40);
        var rooms = f.App.Services.GetRequiredService<JKiller.Host.Rooms.RoomStore>(); rooms.SweepExpired();
        Assert.Equal(publication.StreamId, Assert.Single(rooms.Snapshot(owner.SessionId).Streams).StreamId);
        f.Clock.Now = f.Clock.Now.AddSeconds(21); rooms.SweepExpired();
        Assert.Empty(rooms.Snapshot(owner.SessionId).Streams);
        Assert.Equal(HttpStatusCode.Forbidden, (await f.Client.SendAsync(f.Request(HttpMethod.Post, path, owner.SessionToken))).StatusCode);
    }
    [Fact]
    public async Task ReopeningTheSameInvitationReusesAuthenticatedSessionAndStreamOwnership()
    {
        await using var host = await HostFixture.Start(); var invite = host.Invite; var owner = await host.Join(invite);
        var publication = (await (await host.Client.SendAsync(host.Request(HttpMethod.Post, "/api/streams", owner.SessionToken))).Content.ReadFromJsonAsync<PublishReservation>())!;
        var response = await host.Client.SendAsync(host.Request(HttpMethod.Post, "/api/join", owner.SessionToken, new { invite, nickname = "renamed", desktop = true }));
        var resumed = (await response.Content.ReadFromJsonAsync<SessionGrant>())!;
        Assert.Equal(owner.SessionId, resumed.SessionId); Assert.Equal(publication.StreamId, Assert.Single(resumed.Room.Streams).StreamId);
    }
    [Fact]
    public async Task BrowserResumeReadsItsStableIdentityWithoutReturningCredential()
    {
        await using var host = await HostFixture.Start(); var owner = await host.Join();
        var response = await host.Client.SendAsync(host.Request(HttpMethod.Get, "/api/me", owner.SessionToken));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(owner.SessionId, json.RootElement.GetProperty("sessionId").GetGuid());
        Assert.False(json.RootElement.TryGetProperty("sessionToken", out _));
    }
    [Fact]
    public async Task AnonymousJoinAttemptsHaveABoundedRate()
    {
        await using var host = await HostFixture.Start(); var invite = host.Invite;
        HttpResponseMessage? last = null;
        for (int i = 0; i < 31; i++) { last?.Dispose(); last = await host.Client.PostAsJsonAsync("/api/join", new { invite, nickname = "friend" }); }
        using (last) Assert.Equal(HttpStatusCode.TooManyRequests, last!.StatusCode);
    }
    [Fact]
    public async Task MissingIdentityIs401AndOtherOwnerIs403()
    {
        await using var host = await HostFixture.Start();
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.PostAsJsonAsync("/api/streams", new { })).StatusCode);
        var invite = host.Invite; var owner = await host.Join(invite); var viewer = await host.Join(invite);
        var publication = await host.Client.SendAsync(host.Request(HttpMethod.Post, "/api/streams", owner.SessionToken));
        var stream = (await publication.Content.ReadFromJsonAsync<PublishReservation>())!;
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.SendAsync(host.Request(HttpMethod.Delete, $"/api/streams/{stream.StreamId}", viewer.SessionToken))).StatusCode);
    }
    [Fact]
    public async Task PublicCookiesAreSecureHttpOnlyAndStrict()
    {
        await using var host = await HostFixture.Start();
        var response = await host.Client.PostAsJsonAsync("/api/join", new { invite = host.Invite, nickname = "friend" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase); Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase); Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("", (await response.Content.ReadFromJsonAsync<SessionGrant>())!.SessionToken);
    }
    [Theory]
    [InlineData("/api/join")]
    [InlineData("/hubs/room/negotiate?negotiateVersion=1")]
    public async Task CrossOriginJoinAndSignalRRejected(string url)
    {
        await using var host = await HostFixture.Start();
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(new { invite = host.Invite, nickname = "friend" }) };
        request.Headers.Add("Origin", "https://untrusted.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.SendAsync(request)).StatusCode);
    }
    [Fact]
    public async Task UnicodeHtmlNicknameIsPlainDataAndIdentitySurvivesRename()
    {
        await using var host = await HostFixture.Start(); var owner = await host.Join(nickname: "<b>日本語</b>");
        var response = await host.Client.SendAsync(host.Request(HttpMethod.Post, "/api/me/nickname", owner.SessionToken, new { nickname = "🎮 <script>" }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var room = (await response.Content.ReadFromJsonAsync<RoomSnapshot>())!;
        Assert.Equal(owner.SessionId, room.Participants[0].SessionId); Assert.Equal("🎮 <script>", room.Participants[0].Nickname);
    }
    [Fact]
    public async Task PublicListenerCannotReachPrivateBootstrapOrAuth()
    {
        await using var host = await HostFixture.Start();
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync("/internal/bootstrap")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.PostAsJsonAsync("/internal/media-auth", new { })).StatusCode);
    }
}
