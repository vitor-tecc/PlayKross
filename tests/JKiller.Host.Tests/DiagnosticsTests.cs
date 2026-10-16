using JKiller.Contracts;
using Xunit;
using System.Net;
using System.Net.Http.Json;
namespace JKiller.Host.Tests;
public sealed class DiagnosticsTests
{
    [Fact]
    public async Task ReportsRequireAuthenticationRoomMembershipAndPublisherOwnership()
    {
        await using var f = await HostFixture.Start(); var invite = f.Invite; var owner = await f.Join(invite); var viewer = await f.Join(invite); var stranger = await f.Join();
        using var reserved = await f.Client.SendAsync(f.Request(HttpMethod.Post, "/api/streams", owner.SessionToken));
        var stream = (await reserved.Content.ReadFromJsonAsync<PublishReservation>())!;
        object body = new DiagnosticsEnvelope(stream.StreamId, "publisher", new Dictionary<string, double?> { ["encodedFps"] = 60 }, new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Client.PostAsJsonAsync("/api/diagnostics", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await f.Client.SendAsync(f.Request(HttpMethod.Post, "/api/diagnostics", viewer.SessionToken, body))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await f.Client.SendAsync(f.Request(HttpMethod.Post, "/api/diagnostics", stranger.SessionToken, body))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await f.Client.SendAsync(f.Request(HttpMethod.Post, "/api/diagnostics", owner.SessionToken, body))).StatusCode);
    }
    [Fact]
    public async Task ExportIsRedactedRateLimitedAndDoesNotCrossRooms()
    {
        await using var f = await HostFixture.Start(); var owner = await f.Join(); var other = await f.Join();
        using var reserved = await f.Client.SendAsync(f.Request(HttpMethod.Post, "/api/streams", owner.SessionToken)); var stream = (await reserved.Content.ReadFromJsonAsync<PublishReservation>())!;
        var body = new DiagnosticsEnvelope(stream.StreamId, "publisher", new Dictionary<string, double?> { ["encodedFps"] = 60 }, new Dictionary<string, string> { ["sessionToken"] = "private-secret" });
        Assert.Equal(HttpStatusCode.OK, (await f.Client.SendAsync(f.Request(HttpMethod.Post, "/api/diagnostics", owner.SessionToken, body))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await f.Client.SendAsync(f.Request(HttpMethod.Post, "/api/diagnostics", owner.SessionToken, body))).StatusCode);
        using var export = await f.Client.SendAsync(f.Request(HttpMethod.Get, "/api/diagnostics", owner.SessionToken)); Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        var text = await export.Content.ReadAsStringAsync(); Assert.Contains("encodedFps", text); Assert.DoesNotContain("private-secret", text);
        using var different = await f.Client.SendAsync(f.Request(HttpMethod.Get, "/api/diagnostics", other.SessionToken)); Assert.DoesNotContain("encodedFps", await different.Content.ReadAsStringAsync());
    }
}
