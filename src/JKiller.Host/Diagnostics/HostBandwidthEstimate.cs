using JKiller.Contracts;

namespace JKiller.Host.Diagnostics;
public static class HostBandwidthEstimate
{
    public static double? EstimateMbps(RoomSnapshot room, IReadOnlyDictionary<Guid, int?> readers, IEnumerable<DiagnosticsEnvelope> samples)
    {
        var publishers = samples.Where(s => s.Role == "publisher").GroupBy(s => s.StreamId).ToDictionary(g => g.Key, g => g.Last());
        double estimate = 0;
        foreach (var stream in room.Streams)
        {
            if (!readers.TryGetValue(stream.StreamId, out var count) || count is null || count < 0) return null;
            if (count == 0) continue;
            if (!publishers.TryGetValue(stream.StreamId, out var sample) ||
                !sample.Metrics.TryGetValue("appliedMbps", out var rate) || rate is null || !double.IsFinite(rate.Value) || rate < 0) return null;
            estimate += rate.Value * count.Value;
        }
        return double.IsFinite(estimate) ? estimate : null;
    }
}
