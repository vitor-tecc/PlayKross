using JKiller.Contracts;
using Xunit;
using System.Net;
using System.Net.Http.Json;
using JKiller.Host.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
namespace JKiller.Host.Tests;
public sealed class DiagnosticsTests
{
    [Fact]
    public async Task ExportAttributesVideoPayloadEstimatePerStreamAndExpiresMissingObservations()
    {
        await using var f = await HostFixture.Start(traffic: new PerStreamTraffic());
        var invite = f.Invite; var a = await f.Join(invite); var b = await f.Join(invite);
        using var reservedA = await f.Client.SendAsync(f.Request(HttpMethod.Post, "/api/streams", a.SessionToken));
        using var reservedB = await f.Client.SendAsync(f.Request(HttpMethod.Post, "/api/streams", b.SessionToken));
        var streamA = (await reservedA.Content.ReadFromJsonAsync<PublishReservation>())!;
        var streamB = (await reservedB.Content.ReadFromJsonAsync<PublishReservation>())!;
        var store = f.App.Services.GetRequiredService<DiagnosticStore>();
        store.Add(a.RoomId, a.SessionId, Publisher(streamA.StreamId, 10));
        f.Clock.Now = f.Clock.Now.AddSeconds(1);
        store.Add(a.RoomId, a.SessionId, Publisher(streamA.StreamId, 15));
        store.Add(a.RoomId, b.SessionId, Publisher(streamB.StreamId, 6));
        var host = await ExportHost();
        Assert.Equal(36d, host.Metrics["estimatedViewerMbps"]);
        Assert.Equal(1234d, host.Metrics["mediaBytesSent"]);
        Assert.Null(host.Metrics["mediaSendMbps"]);
        Assert.Equal("video-payload-only", host.Metadata["estimateBasis"]);
        Assert.Equal("mediamtx-api-and-all-active-nics", host.Metadata["provenance"]);
        Assert.DoesNotContain("sessionToken", host.Metadata.Keys);

        f.Clock.Now = f.Clock.Now.AddMinutes(2).AddSeconds(1);
        var expiredRoom = a.Room with { Streams = [new(streamA.StreamId, a.SessionId, "A", "live"), new(streamB.StreamId, b.SessionId, "B", "live")] };
        Assert.Empty(store.Read(expiredRoom));
        Assert.Null(HostBandwidthEstimate.EstimateMbps(expiredRoom, new Dictionary<Guid, int?> { [streamA.StreamId] = 2, [streamB.StreamId] = 1 }, store.Read(expiredRoom)));

        async Task<DiagnosticsEnvelope> ExportHost()
        {
            using var response = await f.Client.SendAsync(f.Request(HttpMethod.Get, "/api/diagnostics", a.SessionToken));
            response.EnsureSuccessStatusCode();
            return Assert.Single((await response.Content.ReadFromJsonAsync<RedactedReport>())!.Samples, s => s.Role == "host");
        }
    }
    private static DiagnosticsEnvelope Publisher(Guid streamId, double mbps) => new(streamId, "publisher", new Dictionary<string, double?> { ["appliedMbps"] = mbps }, new Dictionary<string, string>());
    private sealed class PerStreamTraffic : IHostTrafficReader
    {
        public Task<HostTrafficSnapshot> ReadAsync(RoomSnapshot room, CancellationToken token) => Task.FromResult(new HostTrafficSnapshot(
            new(Guid.Empty, "host", new Dictionary<string, double?> { ["activeReaders"] = 3, ["mediaBytesSent"] = 1234, ["mediaSendMbps"] = null }, new Dictionary<string, string> { ["provenance"] = "mediamtx-api-and-all-active-nics", ["sessionToken"] = "secret" }),
            room.Streams.Select((s, index) => (s.StreamId, Readers: (int?)(index == 0 ? 2 : 1))).ToDictionary(s => s.StreamId, s => s.Readers)));
    }
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
