using JKiller.Contracts;
using JKiller.Host.Diagnostics;
using Xunit;

namespace JKiller.Host.Tests;
public sealed class HostBandwidthEstimateTests
{
    private readonly RoomSnapshot room = new(Guid.NewGuid(), 1, [], [new(Guid.NewGuid(), Guid.NewGuid(), "A", "live"), new(Guid.NewGuid(), Guid.NewGuid(), "B", "live")]);

    [Fact]
    public void EstimateUsesEachStreamsReaders()
    {
        var estimate = HostBandwidthEstimate.EstimateMbps(room, Readers(2, 1), [Publisher(0, 15), Publisher(1, 6)]);
        Assert.Equal(36d, estimate);
    }

    [Fact]
    public void MissingActivePublisherRateStaysUnavailable()
    {
        Assert.Null(HostBandwidthEstimate.EstimateMbps(room, Readers(2, 1), [Publisher(0, 15)]));
    }

    [Fact]
    public void LatestUnavailablePublisherRateDoesNotReuseEarlierRate()
    {
        Assert.Null(HostBandwidthEstimate.EstimateMbps(room, Readers(2, 1), [Publisher(0, 15), Publisher(1, 6), Publisher(1, null)]));
    }

    [Fact]
    public void LatestPublisherObservationIsUsedAndForeignStreamsAreIgnored()
    {
        var foreign = new DiagnosticsEnvelope(Guid.NewGuid(), "publisher", new Dictionary<string, double?> { ["appliedMbps"] = 100 }, new Dictionary<string, string>());
        var viewer = Publisher(0, 100) with { Role = "viewer" };
        Assert.Equal(36d, HostBandwidthEstimate.EstimateMbps(room, Readers(2, 1), [Publisher(0, 10), Publisher(1, 6), Publisher(0, 15), foreign, viewer]));
    }

    [Fact]
    public void StreamKnownToHaveNoReadersContributesZeroWithoutPublisherSample()
    {
        Assert.Equal(30d, HostBandwidthEstimate.EstimateMbps(room, Readers(2, 0), [Publisher(0, 15)]));
    }

    [Fact]
    public void IncompleteReaderInventoryStaysUnavailable()
    {
        Assert.Null(HostBandwidthEstimate.EstimateMbps(room, new Dictionary<Guid, int?> { [room.Streams[0].StreamId] = 2 }, [Publisher(0, 15), Publisher(1, 6)]));
        Assert.Null(HostBandwidthEstimate.EstimateMbps(room, Readers(2, null), [Publisher(0, 15), Publisher(1, 6)]));
    }

    private Dictionary<Guid, int?> Readers(int? a, int? b) => new() { [room.Streams[0].StreamId] = a, [room.Streams[1].StreamId] = b };
    private DiagnosticsEnvelope Publisher(int index, double? mbps) => new(room.Streams[index].StreamId, "publisher", new Dictionary<string, double?> { ["appliedMbps"] = mbps }, new Dictionary<string, string>());
}
