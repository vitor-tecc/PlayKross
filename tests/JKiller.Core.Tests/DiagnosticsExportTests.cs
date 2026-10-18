using System.Text.Json;
using JKiller.Contracts;
using JKiller.Core.Diagnostics;
using Xunit;
namespace JKiller.Core.Tests;
public sealed class DiagnosticsExportTests
{
    [Fact]
    public void AmfMetadataIsRetainedWithoutAllowingPrivateOrInventedRouteClaims()
    {
        var report = DiagnosticsExport.Redact([new(Guid.Empty, "publisher", new Dictionary<string, double?>(),
            new Dictionary<string, string> { ["encoder"] = "h264_texture_amf", ["encoderFamily"] = "amf", ["encoderTransferRoute"] = "unverified", ["driverError"] = "private path" })]);
        var metadata = Assert.Single(report.Samples).Metadata;
        Assert.Equal("h264_texture_amf", metadata["encoder"]); Assert.Equal("amf", metadata["encoderFamily"]); Assert.Equal("unverified", metadata["encoderTransferRoute"]); Assert.Equal(3, metadata.Count);
        Assert.Empty(Assert.Single(DiagnosticsExport.Redact([report.Samples[0] with { Metadata = new Dictionary<string, string> { ["encoderFamily"] = "private", ["encoderTransferRoute"] = "zero-copy" } }]).Samples).Metadata);
    }
    [Theory]
    [InlineData("off")]
    [InlineData("application")]
    [InlineData("system-output")]
    public void AudioObservationsExportOnlyEnumeratedModeAndNumbers(string mode)
    {
        var sample = Assert.Single(DiagnosticsExport.Redact([new(Guid.Empty, "publisher",
            new Dictionary<string, double?> { ["audioQueueMs"] = 2, ["droppedAudioFrames"] = 48, ["activeVideoEncoders"] = 1, ["activeAudioEncoders"] = 1 },
            new Dictionary<string, string> { ["audioMode"] = mode, ["audioError"] = "private device error", ["audioIdentity"] = "endpoint:private" })]).Samples);
        Assert.Equal(4, sample.Metrics.Count); Assert.Equal(mode, Assert.Single(sample.Metadata).Value);
        Assert.Empty(Assert.Single(DiagnosticsExport.Redact([sample with { Metadata = new Dictionary<string, string> { ["audioMode"] = "private" } }]).Samples).Metadata);
    }
    [Fact]
    public void VideoPayloadEstimateBasisSurvivesRedactionWithoutAllowingArbitraryStrings()
    {
        var host = new DiagnosticsEnvelope(Guid.Empty, "host", new Dictionary<string, double?> { ["estimatedViewerMbps"] = 36 }, new Dictionary<string, string> { ["estimateBasis"] = "video-payload-only", ["sessionToken"] = "session-secret" });
        var sample = Assert.Single(DiagnosticsExport.Redact([host]).Samples);
        Assert.Equal("video-payload-only", sample.Metadata["estimateBasis"]);
        Assert.DoesNotContain("sessionToken", sample.Metadata.Keys);
        var malicious = host with { Metadata = new Dictionary<string, string> { ["estimateBasis"] = "session-secret" } };
        Assert.Empty(Assert.Single(DiagnosticsExport.Redact([malicious]).Samples).Metadata);
    }
    [Fact]
    public void ExportContainsNoCredentialsOrPrivateImagePaths()
    {
        var report = DiagnosticsExport.Redact([new(Guid.NewGuid(), "viewer", new Dictionary<string, double?> { ["receiveMbps"] = 40, ["C:/Private/game.exe"] = 1 }, new Dictionary<string, string> { ["codec"] = "video/H264", ["sessionToken"] = "session-secret", ["imagePath"] = "C:/Private/game.exe", ["candidateAddress"] = "192.168.1.1", ["encoder"] = "C:/Private/game.exe" })]);
        var json = JsonSerializer.Serialize(report);
        Assert.DoesNotContain("session-secret", json); Assert.DoesNotContain("Private", json); Assert.DoesNotContain("192.168", json); Assert.Contains("video/H264", json); Assert.Contains("40", json);
    }
    [Fact]
    public void InvalidOrUnknownNumericObservationsAreDropped()
    {
        var report = DiagnosticsExport.Redact([new(Guid.NewGuid(), "publisher", new Dictionary<string, double?> { ["captureFps"] = double.NaN, ["encodedFps"] = null, ["publishToken"] = 123, ["receiveMbps"] = -1 }, new Dictionary<string, string>())]);
        var sample = Assert.Single(report.Samples); Assert.Single(sample.Metrics); Assert.True(sample.Metrics.ContainsKey("encodedFps")); Assert.Null(sample.Metrics["encodedFps"]);
    }
}
