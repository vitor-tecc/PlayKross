using JKiller.Acceptance.Probes;
using JKiller.Contracts;
using Xunit;
namespace JKiller.Windows.Tests;
public sealed class ProbeCaptureModeTests
{
    private static SourceDescriptor Source => new("fixture", SourceKind.Window, 42, new(123, 456, "CaptureFixture.exe"), "Synthetic");
    [Fact]
    public void VideoOnlyProbeNeverRequestsApplicationAudio() => Assert.Equal(AudioSelection.Off, ProbeCaptureMode.SelectAudio("video-only", Source));
    [Fact]
    public void DefaultProbeStillRequestsAudioRatherThanSilentlyPassingVideoOnly()
    {
        var source = Source;
        Assert.Equal(new(AudioMode.Application, source.Process), ProbeCaptureMode.SelectAudio(null, source));
        Assert.Equal(new(AudioMode.Application, source.Process), ProbeCaptureMode.SelectAudio("application", source));
    }
    [Fact]
    public void SystemOutputRequiresConcreteRenderEndpoint()
    {
        Assert.Throws<ArgumentException>(() => ProbeCaptureMode.SelectAudio("system-output", Source));
        Assert.Throws<ArgumentException>(() => ProbeCaptureMode.SelectAudio("system-output", Source, "default"));
        Assert.Equal(new(AudioMode.SystemOutput, RenderEndpointId: "render:selected"), ProbeCaptureMode.SelectAudio("system-output", Source, "render:selected"));
    }
    [Fact]
    public void UnknownModeCannotSilentlyChangeTheMeasurement() => Assert.Throws<ArgumentException>(() => ProbeCaptureMode.SelectAudio("typo", Source));
}
