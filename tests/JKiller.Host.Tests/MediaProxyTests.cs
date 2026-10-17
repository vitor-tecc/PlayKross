using System.Net;
using System.Net.Http.Json;
using System.Text;
using JKiller.Contracts;
using Xunit;
namespace JKiller.Host.Tests;
public sealed class MediaProxyTests
{
    private sealed class Upstream : HttpMessageHandler
    {
        public bool ExternalLocation;
        public bool Empty204Options;
        public List<Uri> Requests = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            var response = new HttpResponseMessage(request.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.NoContent);
            if (request.Method == HttpMethod.Options && Empty204Options) response.Content = new StringContent("unexpected upstream body");
            if (request.Method == HttpMethod.Post)
            {
                response.Content = new StringContent("v=0\r\na=answer\r\n", Encoding.UTF8, "application/sdp");
                response.Headers.Location = new Uri(ExternalLocation ? "https://untrusted.test/resource" : request.RequestUri!.AbsolutePath + "/" + Guid.NewGuid().ToString(), UriKind.RelativeOrAbsolute);
                response.Headers.TryAddWithoutValidation("ETag", "*"); response.Headers.Add("ID", Guid.NewGuid().ToString());
            }
            return Task.FromResult(response);
        }
    }
    [Fact]
    public async Task StoppingOnePublicationKeepsOtherPublishAndReadResourcesUsable()
    {
        await using var host = await HostFixture.Start(new Upstream()); var invite = host.Invite;
        var aOwner = await host.Join(invite); var bOwner = await host.Join(invite); var viewer = await host.Join(invite);
        var rooms = (JKiller.Host.Rooms.RoomStore)host.App.Services.GetService(typeof(JKiller.Host.Rooms.RoomStore))!;
        var a = rooms.ReservePublish(aOwner.SessionId); var b = rooms.ReservePublish(bOwner.SessionId);
        var read = rooms.GetPlayback(viewer.SessionId, b.StreamId);
        using var aResponse = await host.Client.SendAsync(Offer(host, a.WhipUri.ToString(), a.Token));
        using var bResponse = await host.Client.SendAsync(Offer(host, b.WhipUri.ToString(), b.Token));
        using var readResponse = await host.Client.SendAsync(Offer(host, read.WhepUri.ToString(), read.Token));
        Assert.Equal(HttpStatusCode.Created, aResponse.StatusCode); Assert.Equal(HttpStatusCode.Created, bResponse.StatusCode); Assert.Equal(HttpStatusCode.Created, readResponse.StatusCode);
        rooms.StopPublish(aOwner.SessionId, a.StreamId);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.SendAsync(host.Request(HttpMethod.Delete, aResponse.Headers.Location!.ToString(), a.Token))).StatusCode);
        foreach (var (location, credential) in new[] { (bResponse.Headers.Location!, b.Token), (readResponse.Headers.Location!, read.Token) })
        {
            using var patch = host.Request(HttpMethod.Patch, location.ToString(), credential);
            patch.Content = new StringContent("a=ice-ufrag:test\r\n", Encoding.UTF8, "application/trickle-ice-sdpfrag");
            Assert.Equal(HttpStatusCode.NoContent, (await host.Client.SendAsync(patch)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Created, (await host.Client.SendAsync(Offer(host, b.WhipUri.ToString(), b.Token))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await host.Client.SendAsync(Offer(host, read.WhepUri.ToString(), read.Token))).StatusCode);
    }
    [Fact]
    public async Task NoContentOptionsNeverWritesAnUpstreamBody()
    {
        await using var host = await HostFixture.Start(new Upstream { Empty204Options = true }); var owner = await host.Join();
        var stream = (await (await host.Client.SendAsync(host.Request(HttpMethod.Post, "/api/streams", owner.SessionToken))).Content.ReadFromJsonAsync<PublishReservation>())!;
        var response = await host.Client.SendAsync(host.Request(HttpMethod.Options, stream.WhipUri.ToString(), stream.Token));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode); Assert.Equal("", await response.Content.ReadAsStringAsync());
    }
    private static HttpRequestMessage Offer(HostFixture host, string url, string token)
    {
        var request = host.Request(HttpMethod.Post, url, token); request.Content = new StringContent("v=0\r\na=offer\r\n", Encoding.UTF8, "application/sdp"); return request;
    }
    [Fact]
    public async Task SdpForwardingRewritesLocationAndResourceRequiresOwner()
    {
        var upstream = new Upstream(); await using var host = await HostFixture.Start(upstream);
        var invite = host.Invite; var owner = await host.Join(invite); var viewer = await host.Join(invite);
        var publication = (await (await host.Client.SendAsync(host.Request(HttpMethod.Post, "/api/streams", owner.SessionToken))).Content.ReadFromJsonAsync<PublishReservation>())!;
        var playback = (await (await host.Client.SendAsync(host.Request(HttpMethod.Get, $"/api/streams/{publication.StreamId}/playback", viewer.SessionToken))).Content.ReadFromJsonAsync<PlaybackGrant>())!;
        var response = await host.Client.SendAsync(Offer(host, playback.WhepUri.ToString(), playback.Token));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); Assert.Equal("application/sdp", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("v=0\r\na=answer\r\n", await response.Content.ReadAsStringAsync()); Assert.Equal("*", Assert.Single(response.Headers.GetValues("ETag")));
        var location = response.Headers.Location!; Assert.StartsWith("/media-resources/", location.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.DeleteAsync(location)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.SendAsync(host.Request(HttpMethod.Delete, location.ToString(), owner.SessionToken))).StatusCode);
        var patch = host.Request(HttpMethod.Patch, location.ToString(), playback.Token);
        patch.Content = new StringContent("a=ice-ufrag:test\r\n", Encoding.UTF8, "application/trickle-ice-sdpfrag"); patch.Headers.TryAddWithoutValidation("If-Match", "*");
        Assert.Equal(HttpStatusCode.NoContent, (await host.Client.SendAsync(patch)).StatusCode);
        host.Clock.Now = host.Clock.Now.AddSeconds(61);
        Assert.Equal(HttpStatusCode.NoContent, (await host.Client.SendAsync(host.Request(HttpMethod.Delete, location.ToString(), playback.Token))).StatusCode);
        Assert.All(upstream.Requests, uri => Assert.Equal("127.0.0.1:8889", uri.Authority));
    }
    [Fact]
    public async Task MissingExpiredWrongActionAndTraversalGrantsRejected()
    {
        await using var host = await HostFixture.Start(new Upstream()); var owner = await host.Join();
        var publication = (await (await host.Client.SendAsync(host.Request(HttpMethod.Post, "/api/streams", owner.SessionToken))).Content.ReadFromJsonAsync<PublishReservation>())!;
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.PostAsync(publication.WhipUri, new StringContent("v=0"))).StatusCode);
        var playback = host.App.Services.GetService(typeof(JKiller.Host.Rooms.RoomStore)) is JKiller.Host.Rooms.RoomStore rooms ? rooms.GetPlayback(owner.SessionId, publication.StreamId) : throw new Exception("Room store missing");
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.SendAsync(Offer(host, publication.WhipUri.ToString(), playback.Token))).StatusCode);
        host.Clock.Now = host.Clock.Now.AddSeconds(61);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.SendAsync(Offer(host, publication.WhipUri.ToString(), publication.Token))).StatusCode);
        Assert.NotEqual(HttpStatusCode.Created, (await host.Client.SendAsync(Offer(host, "/media/..%2fprivate/whip", publication.Token))).StatusCode);
    }
    [Fact]
    public async Task ExternalUpstreamResourceLocationCannotBecomeProxyTarget()
    {
        var upstream = new Upstream { ExternalLocation = true }; await using var host = await HostFixture.Start(upstream); var owner = await host.Join();
        var stream = (await (await host.Client.SendAsync(host.Request(HttpMethod.Post, "/api/streams", owner.SessionToken))).Content.ReadFromJsonAsync<PublishReservation>())!;
        Assert.Equal(HttpStatusCode.BadGateway, (await host.Client.SendAsync(Offer(host, stream.WhipUri.ToString(), stream.Token))).StatusCode);
        Assert.Single(upstream.Requests); // No request to the external Location.
    }
}
