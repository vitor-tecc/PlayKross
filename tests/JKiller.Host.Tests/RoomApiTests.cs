using System.Net;
using System.Net.Http.Json;
using JKiller.Contracts;
using Xunit;
namespace JKiller.Host.Tests;
public sealed class RoomApiTests
{
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
