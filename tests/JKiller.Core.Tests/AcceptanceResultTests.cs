using JKiller.Core.Diagnostics;
using Xunit;
namespace JKiller.Core.Tests;
public sealed class AcceptanceResultTests
{
    private static AcceptanceEvidence Complete => new(true, true, 600, 60, 100, 100, .1, .1, 200, 20, -20, true, true, true, true, true, true, "direct", true, ["measured-report.json"], 60, 60);
    [Fact] public void MissingHardwareOrExternalEvidenceCannotPass()
    {
        var result = AcceptanceReportValidator.Validate(Complete with { HardwarePacketActivity = null, ExternalNetworkConfirmed = null });
        Assert.Equal("pending", result["hardware"]); Assert.Equal("pending", result["external"]);
    }
    [Fact] public void MockOnlyEvidenceCannotPassStreamingGate()
    {
        var result = AcceptanceReportValidator.Validate(Complete with { RealMedia = false });
        Assert.NotEqual("passed", result["streaming"]); Assert.NotEqual("passed", result["milestone"]);
    }
    [Fact] public void MissingLatencyAndAudioCannotBeFilledByFpsOrIceRtt()
    {
        var result = AcceptanceReportValidator.Validate(Complete with { LatencyP95Ms = null, DiscordBrowserExcluded = null });
        Assert.Equal("pending", result["streaming"]); Assert.Equal("pending", result["audio"]);
    }
    [Fact] public void BadMeasurementsFailAndCompleteRealEvidencePasses()
    {
        Assert.Equal("failed", AcceptanceReportValidator.Validate(Complete with { AveragePresentedFps = 10 })["streaming"]);
        Assert.Equal("pending", AcceptanceReportValidator.Validate(Complete with { LatencyP95Ms = double.NaN })["streaming"]);
        Assert.Equal("pending", AcceptanceReportValidator.Validate(Complete with { Evidence = [] })["milestone"]);
        Assert.Equal("passed", AcceptanceReportValidator.Validate(Complete)["milestone"]);
    }
    [Fact] public void MissingEvidenceCollectionRemainsPending() => Assert.Equal("pending", AcceptanceReportValidator.Validate(Complete with { Evidence = null! })["milestone"]);
    [Fact] public void RepeatedEncodedFramesCannotReplaceFreshHighMotionCapture() => Assert.Equal("failed", AcceptanceReportValidator.Validate(Complete with { AverageCaptureFps = 40 })["streaming"]);
}
