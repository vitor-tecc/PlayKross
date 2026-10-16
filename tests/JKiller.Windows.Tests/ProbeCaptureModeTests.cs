using JKiller.Acceptance.Probes;
using JKiller.Contracts;
using Xunit;
namespace JKiller.Windows.Tests;
public sealed class ProbeCaptureModeTests
{
    private static SourceDescriptor Source => new("fixture", SourceKind.Window, 42, new(123, 456, "CaptureFixture.exe"), "Synthetic");
    [Fact]
    public void VideoOnlyProbeNeverRequestsApplicationAudio() => Assert.Null(ProbeCaptureMode.SelectAudio("video-only", Source));
    [Fact]
    public void DefaultProbeStillRequestsAudioRatherThanSilentlyPassingVideoOnly()
    {
        var source = Source;
        Assert.Same(source, ProbeCaptureMode.SelectAudio(null, source));
        Assert.Same(source, ProbeCaptureMode.SelectAudio("application", source));
    }
    [Fact]
    public void UnknownModeCannotSilentlyChangeTheMeasurement() => Assert.Throws<ArgumentException>(() => ProbeCaptureMode.SelectAudio("typo", Source));
}
