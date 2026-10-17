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
        Assert.Equal(2d, second.Aggregate.Metrics["mediaSendMbps"]!.Value, 6);
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
        Assert.Equal(8d / 6, sample.Aggregate.Metrics["mediaSendMbps"]!.Value, 6);
    }
    [Fact]
    public async Task ReaderCountsUseExactPathsAndReadStateFromTheAggregateInventory()
    {
        var clock = new Clock(); var handler = new Inventory(clock);
        var a = handler.Room.Streams[0]; var b = new StreamSnapshot(Guid.NewGuid(), Guid.NewGuid(), "B", "live"); var c = new StreamSnapshot(Guid.NewGuid(), Guid.NewGuid(), "C", "live");
        handler.Room = handler.Room with { Streams = [a, b, c] };
        string Path(Guid roomId, Guid streamId) => $"rooms/{roomId:N}/streams/{streamId:N}/main";
        handler.Items = _ => [
            new("a-reader-1", Path(handler.Room.RoomId, a.StreamId), "read", 100, 10),
            new("a-reader-2", Path(handler.Room.RoomId, a.StreamId), "read", 200, 20),
            new("a-publisher", Path(handler.Room.RoomId, a.StreamId), "publish", 300, 30),
            new("b-reader", Path(handler.Room.RoomId, b.StreamId), "read", 400, 40),
            new("foreign-room", Path(Guid.NewGuid(), a.StreamId), "read", 9000, 9000),
            new("path-suffix", Path(handler.Room.RoomId, a.StreamId) + "/other", "read", 9000, 9000)];
        using var reader = Create(handler, clock);
        var snapshot = await reader.ReadAsync(handler.Room, CancellationToken.None);
        Assert.Equal(2, snapshot.ReaderCounts[a.StreamId]); Assert.Equal(1, snapshot.ReaderCounts[b.StreamId]); Assert.Equal(0, snapshot.ReaderCounts[c.StreamId]);
        Assert.Equal(1000d, snapshot.Aggregate.Metrics["mediaBytesSent"]); Assert.Equal(100d, snapshot.Aggregate.Metrics["mediaBytesReceived"]);
        Assert.Equal(4d, snapshot.Aggregate.Metrics["activeMediaSessions"]); Assert.Equal(3d, snapshot.Aggregate.Metrics["activeReaders"]);
        Assert.Null(snapshot.Aggregate.Metrics["mediaSendMbps"]); Assert.Null(snapshot.Aggregate.Metrics["mediaReceiveMbps"]);
        Assert.Equal("mediamtx-api-and-all-active-nics", snapshot.Aggregate.Metadata["provenance"]);
    }
    [Fact]
    public async Task FailedOrIncompleteInventoryDoesNotReportZeroReadersOrReuseOldCounters()
    {
        var clock = new Clock(); var handler = new Inventory(clock); using var reader = Create(handler, clock);
        await reader.ReadAsync(handler.Room, CancellationToken.None);
        foreach (var pageCount in new[] { 5, 1 })
        {
            clock.Advance(4); handler.PageCount = pageCount; handler.Fail = pageCount == 1;
            var snapshot = await reader.ReadAsync(handler.Room, CancellationToken.None);
            Assert.Null(snapshot.ReaderCounts[handler.Room.Streams[0].StreamId]);
            Assert.Null(snapshot.Aggregate.Metrics["mediaBytesSent"]); Assert.Null(snapshot.Aggregate.Metrics["estimatedViewerMbps"]);
        }
    }
    [Fact]
    public async Task CounterResetKeepsRatesUnavailableAndObservedCountersIntact()
    {
        var clock = new Clock(); var handler = new Inventory(clock); using var reader = Create(handler, clock);
        await reader.ReadAsync(handler.Room, CancellationToken.None);
        handler.Items = _ => [new("reader-1", $"rooms/{handler.Room.RoomId:N}/streams/{handler.Room.Streams[0].StreamId:N}/main", "read", 10, 5)];
        clock.Advance(4);
        var snapshot = await reader.ReadAsync(handler.Room, CancellationToken.None);
        Assert.Null(snapshot.Aggregate.Metrics["mediaSendMbps"]); Assert.Null(snapshot.Aggregate.Metrics["mediaReceiveMbps"]);
        Assert.Equal(10d, snapshot.Aggregate.Metrics["mediaBytesSent"]); Assert.Equal(5d, snapshot.Aggregate.Metrics["mediaBytesReceived"]);
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
        public int PageCount = 1;
        public bool Fail;
        public Func<int, InventoryItem[]>? Items;
        public RoomSnapshot Room = new(Guid.NewGuid(), 1, [], [new(Guid.NewGuid(), Guid.NewGuid(), "publisher", "live")]);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal("/v3/webrtc/sessions/list", request.RequestUri!.AbsolutePath);
            Requests++; clock.Advance(RequestSeconds);
            return Task.FromResult(new HttpResponseMessage(Fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK) { Content = JsonContent.Create(new { pageCount = PageCount, items = Items?.Invoke(Requests) ?? [new InventoryItem("reader-1", $"rooms/{Room.RoomId:N}/streams/{Room.Streams[0].StreamId:N}/main", "read", Requests * 1000000, Requests * 500000)] }) });
        }
    }
    private sealed record InventoryItem(string id, string path, string state, double outboundBytes, double inboundBytes);
}
