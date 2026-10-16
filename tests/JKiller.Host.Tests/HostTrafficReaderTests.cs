using System.Net;
using System.Net.Http.Json;
using JKiller.Contracts;
using JKiller.Host.Diagnostics;
using Xunit;

namespace JKiller.Host.Tests;
public sealed class HostTrafficReaderTests
{
    [Theory]
    [InlineData(-3600)]
    [InlineData(3600)]
    public async Task WallClockJumpsDoNotChangeRateOrCacheAge(int wallJump)
    {
        var clock = new Clock(); var handler = new Inventory(clock); using var reader = Create(handler, clock);
        var room = handler.Room;
        await reader.ReadAsync(room, CancellationToken.None);
        clock.Advance(4); clock.Now = clock.Now.AddSeconds(wallJump);
        var second = await reader.ReadAsync(room, CancellationToken.None);
        Assert.Equal(2, handler.Requests);
        Assert.Equal(2d, second.Metrics["mediaSendMbps"]!.Value, 6);
        clock.Now = clock.Now.AddHours(3);
        await reader.ReadAsync(room, CancellationToken.None);
        Assert.Equal(2, handler.Requests);
    }
    [Fact]
    public async Task RateUsesTheSameSamplingPointWhenInventoryRequestsAreSlow()
    {
        var clock = new Clock(); var handler = new Inventory(clock) { RequestSeconds = 2 };
        using var reader = Create(handler, clock);
        await reader.ReadAsync(handler.Room, CancellationToken.None);
        clock.Advance(4);
        var sample = await reader.ReadAsync(handler.Room, CancellationToken.None);
        Assert.Equal(8d / 6, sample.Metrics["mediaSendMbps"]!.Value, 6);
    }
    private static HostTrafficReader Create(Inventory handler, Clock clock) => new(new(new("https://room.test"), new("http://127.0.0.1:8889"), new("http://127.0.0.1:9997"), new string('b', 43), new string('a', 43)), clock, handler);
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 14, 0, 0, 0, TimeSpan.Zero);
        private long elapsed;
        public override DateTimeOffset GetUtcNow() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => elapsed;
        public void Advance(int seconds) { elapsed += seconds * TimeSpan.TicksPerSecond; Now = Now.AddSeconds(seconds); }
    }
    private sealed class Inventory(Clock clock) : HttpMessageHandler
    {
        public int Requests, RequestSeconds;
        public RoomSnapshot Room = new(Guid.NewGuid(), 1, [], [new(Guid.NewGuid(), Guid.NewGuid(), "publisher", "live")]);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal("/v3/webrtc/sessions/list", request.RequestUri!.AbsolutePath);
            Requests++; clock.Advance(RequestSeconds);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { pageCount = 1, items = new[] { new { id = "reader-1", path = $"rooms/{Room.RoomId:N}/streams/{Room.Streams[0].StreamId:N}/main", state = "read", outboundBytes = Requests * 1000000, inboundBytes = Requests * 500000 } } }) });
        }
    }
}
