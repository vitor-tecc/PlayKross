using System.Text.Json;
using JKiller.Contracts;
using JKiller.Core.Diagnostics;
using Xunit;
namespace JKiller.Core.Tests;
public sealed class DiagnosticsExportTests
{
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
