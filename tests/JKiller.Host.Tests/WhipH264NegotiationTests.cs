using System.Net;
using System.Text;
using JKiller.Host.Rooms;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JKiller.Host.Tests;

public sealed class WhipH264NegotiationTests
{
    private static string Sdp(string profile, string? asymmetry = "1", string mode = "1", string codec = "H264", string direction = "sendonly") =>
        "v=0\r\nm=video 9 UDP/TLS/RTP/SAVPF 126\r\na=mid:0\r\na=" + direction + "\r\na=rtpmap:126 " + codec + "/90000\r\n" +
        "a=fmtp:126 profile-level-id=" + profile + (asymmetry is null ? "" : ";level-asymmetry-allowed=" + asymmetry) + ";packetization-mode=" + mode + "\r\n" +
        "m=audio 9 UDP/TLS/RTP/SAVPF 111\r\na=mid:1\r\na=rtpmap:111 opus/48000/2\r\na=fmtp:111 minptime=10;useinbandfec=1\r\n";

    private sealed class Upstream(string answer) : HttpMessageHandler
    {
        public string? Offer;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Offer = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(HttpStatusCode.Created)
            {
                Content = new StringContent(answer, Encoding.UTF8, "application/sdp"),
                Headers = { Location = new Uri(request.RequestUri!.AbsolutePath + "/" + Guid.NewGuid(), UriKind.Relative) },
            };
        }
    }

    [Theory]
    [InlineData("42e01f", "42e02a")]
    [InlineData("42001f", "42002a")]
    public async Task WhipAdvertises1080p60ReceivingCapabilityForAsymmetricH264(string profile, string expected)
    {
        var offer = Sdp(profile); var answer = Sdp(profile, direction: "recvonly");
        var upstream = new Upstream(answer);
        var body = await Exchange(upstream, offer);
        Assert.Equal(answer.Replace("profile-level-id=" + profile, "profile-level-id=" + expected), body);
        Assert.Equal(offer, upstream.Offer);
    }

    [Theory]
    [InlineData("whep", "1", "1", "1", "H264", "42e01f", "42e01f", "recvonly")]
    [InlineData("whip", "0", "1", "1", "H264", "42e01f", "42e01f", "recvonly")]
    [InlineData("whip", null, "1", "1", "H264", "42e01f", "42e01f", "recvonly")]
    [InlineData("whip", "1", "0", "1", "H264", "42e01f", "42e01f", "recvonly")]
    [InlineData("whip", "1", null, "1", "H264", "42e01f", "42e01f", "recvonly")]
    [InlineData("whip", "1", "1", "0", "H264", "42e01f", "42e01f", "recvonly")]
    [InlineData("whip", "1", "1", "1", "VP8", "42e01f", "42e01f", "recvonly")]
    [InlineData("whip", "1", "1", "1", "H264", "42e01f", "42e02a", "recvonly")]
    [InlineData("whip", "1", "1", "1", "H264", "42001f", "42e01f", "recvonly")]
    [InlineData("whip", "1", "1", "1", "H264", "42e01f", "42e01f", "sendonly")]
    public async Task UnrelatedOrNonAsymmetricNegotiationsKeepTheirAnswer(string mode, string? offerAsymmetry, string? answerAsymmetry,
        string packetization, string codec, string offerProfile, string answerProfile, string answerDirection)
    {
        var answer = Sdp(answerProfile, answerAsymmetry, packetization, codec, answerDirection);
        Assert.Equal(answer, await Exchange(new(answer), Sdp(offerProfile, offerAsymmetry, packetization, codec), mode));
    }

    [Theory]
    [InlineData("mid")]
    [InlineData("rejected")]
    [InlineData("payload")]
    [InlineData("duplicate-fmtp")]
    [InlineData("duplicate-parameter")]
    [InlineData("lower-receive-level")]
    [InlineData("lower-frame-size")]
    public async Task AmbiguousRejectedOrExplicitlyLimitedAnswersRemainUnchanged(string scenario)
    {
        var offer = Sdp("42e01f"); var answer = Sdp("42e01f", direction: "recvonly");
        answer = scenario switch
        {
            "mid" => answer.Replace("a=mid:0", "a=mid:other"),
            "rejected" => answer.Replace("m=video 9", "m=video 0"),
            "payload" => answer.Replace("m=video 9 UDP/TLS/RTP/SAVPF 126", "m=video 9 UDP/TLS/RTP/SAVPF 127"),
            "duplicate-fmtp" => answer.Replace("a=mid:0\r\n", "a=mid:0\r\na=fmtp:126 profile-level-id=42e01f;level-asymmetry-allowed=1;packetization-mode=1\r\n"),
            "duplicate-parameter" => answer.Replace("packetization-mode=1", "packetization-mode=1;packetization-mode=1"),
            "lower-receive-level" => answer.Replace("packetization-mode=1", "packetization-mode=1;max-recv-level=32"),
            "lower-frame-size" => answer.Replace("packetization-mode=1", "packetization-mode=1;max-fs=3600"),
            _ => throw new InvalidOperationException(),
        };
        Assert.Equal(answer, await Exchange(new(answer), offer));
    }

    [Fact]
    public async Task ReusedPayloadInAnotherMediaSectionIsNotRewritten()
    {
        var offer = Sdp("42e01f"); var answer = Sdp("42e01f", direction: "recvonly");
        var otherSection = "m=application 9 UDP/DTLS/SCTP webrtc-datachannel\r\na=mid:2\r\na=fmtp:126 profile-level-id=42e01f;level-asymmetry-allowed=1;packetization-mode=1\r\n";
        var body = await Exchange(new(answer + otherSection), offer + otherSection);
        Assert.Equal(answer.Replace("profile-level-id=42e01f", "profile-level-id=42e02a") + otherSection, body);
    }

    private static async Task<string> Exchange(Upstream upstream, string offer, string mode = "whip")
    {
        await using var host = await HostFixture.Start(upstream); var owner = await host.Join();
        var rooms = host.App.Services.GetRequiredService<RoomStore>(); var publication = rooms.ReservePublish(owner.SessionId);
        var grant = mode == "whip" ? publication.Token : rooms.GetPlayback(owner.SessionId, publication.StreamId).Token;
        using var request = host.Request(HttpMethod.Post, publication.WhipUri.ToString().Replace("/whip", "/" + mode), grant);
        request.Content = new StringContent(offer, Encoding.UTF8, "application/sdp");
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("application/sdp", response.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("/media-resources/", response.Headers.Location!.ToString());
        return await response.Content.ReadAsStringAsync();
    }
}
