namespace JKiller.Core.Diagnostics;
public sealed record AcceptanceEvidence(
    bool RealMedia, bool HighMotion, double? DurationSeconds, double? AveragePresentedFps, double? HealthyFiveSecondPercent,
    double? LongestFreezeMs, double? RenderSkippedPercent, double? EncodingSkippedPercent, double? LatencyP95Ms, double? AvOffsetStartMs, double? AvOffsetEndMs,
    bool? HardwareInitialized, bool? HardwarePacketActivity, bool? DiscordDesktopExcluded, bool? DiscordBrowserExcluded, bool? IncomingAudioExcluded,
    bool? ExternalNetworkConfirmed, string? SelectedIcePath, bool? OtherOsVerified, IReadOnlyList<string> Evidence, double? AverageCaptureFps = null, double? AverageDecodedFps = null);
public static class AcceptanceReportValidator
{
    public static IReadOnlyDictionary<string, string> Validate(AcceptanceEvidence e)
    {
        bool Number(double? n) => n is not null && double.IsFinite(n.Value);
        var hasEvidence = e.Evidence is { Count: > 0 } && e.Evidence.All(s => !string.IsNullOrWhiteSpace(s));
        string Booleans(params bool?[] values) => !e.RealMedia || !hasEvidence || values.Any(v => v is null) ? "pending" : values.All(v => v == true) ? "passed" : "failed";
        var result = new Dictionary<string, string> {
            ["hardware"] = Booleans(e.HardwareInitialized, e.HardwarePacketActivity),
            ["audio"] = Booleans(e.DiscordDesktopExcluded, e.DiscordBrowserExcluded, e.IncomingAudioExcluded),
            ["external"] = e.SelectedIcePath is not ("direct" or "relayed") ? "pending" : Booleans(e.ExternalNetworkConfirmed),
            ["otherOs"] = Booleans(e.OtherOsVerified), ["relay"] = e.SelectedIcePath == "relayed" ? Booleans(e.ExternalNetworkConfirmed) : "pending" };
        var numbers = new[] { e.DurationSeconds, e.AverageCaptureFps, e.AverageDecodedFps, e.AveragePresentedFps, e.HealthyFiveSecondPercent, e.LongestFreezeMs, e.RenderSkippedPercent, e.EncodingSkippedPercent, e.LatencyP95Ms, e.AvOffsetStartMs, e.AvOffsetEndMs };
        var knownFailure = e.AverageCaptureFps is < 57 || e.AverageDecodedFps is < 57 || e.AveragePresentedFps is < 57;
        result["streaming"] = !e.RealMedia || !e.HighMotion || !hasEvidence ? "pending" : knownFailure ? "failed" : !numbers.All(Number) ? "pending" :
            e.DurationSeconds >= 600 && e.AveragePresentedFps >= 57 && e.HealthyFiveSecondPercent >= 95 && e.HealthyFiveSecondPercent <= 100 &&
            e.LongestFreezeMs is >= 0 and <= 500 && e.RenderSkippedPercent is >= 0 and < 1 && e.EncodingSkippedPercent is >= 0 and < 1 &&
            e.LatencyP95Ms is >= 0 and <= 500 && Math.Abs(e.AvOffsetStartMs!.Value) <= 100 && Math.Abs(e.AvOffsetEndMs!.Value) <= 100 ? "passed" : "failed";
        var required = new[] { "hardware", "audio", "external", "otherOs", "streaming" }.Select(k => result[k]).ToArray();
        result["milestone"] = required.Contains("failed") ? "failed" : required.All(v => v == "passed") ? "passed" : "pending";
        return result;
    }
}
