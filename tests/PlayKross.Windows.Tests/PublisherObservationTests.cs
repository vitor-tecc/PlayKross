using PlayKross.Contracts;
using PlayKross.Windows.Presentation;
using Xunit;

namespace PlayKross.Windows.Tests;
public sealed class PublisherObservationTests
{
    private static MediaDiagnostics Sample(long ns, long frames, long bytes, string? audioMode = "application", string? family = "amf") => new(
        "h264_texture_amf", "h264", true, "wgc", true, false, frames, frames, 0, frames, frames, bytes, frames, 1280, 720, 30, 1, 2.5,
        4.0, 1.0, 6.0, 10, 10, 0, "native", null, "unavailable", 100, 100, true, true, ns, 1.5, frames, 0,
        CaptureWidth: 1920, CaptureHeight: 1080, AudioModeName: audioMode, EncoderFamily: family, EncoderTransferRoute: "unverified");

    [Fact]
    public void RatesNeedTwoSamplesAndUseTheMonotonicInterval()
    {
        Assert.Equal(PublisherRates.Unavailable, PublisherRates.Between(null, Sample(0, 0, 0)));
        var rates = PublisherRates.Between(Sample(0, 0, 0), Sample(2_000_000_000, 60, 1_000_000));
        Assert.Equal(30, rates.CaptureFps); Assert.Equal(30, rates.EncodedFps); Assert.Equal(30, rates.SentFps); Assert.Equal(30, rates.CompositorFps);
        Assert.Equal(4, rates.SendMbps);
    }

    [Fact]
    public void CounterResetsProduceNoRate()
    {
        var rates = PublisherRates.Between(Sample(0, 100, 5000), Sample(1_000_000_000, 10, 100));
        Assert.Null(rates.CaptureFps); Assert.Null(rates.SendMbps);
    }

    [Fact]
    public void EnvelopeCarriesRequestedAndAppliedSettingsAndOnlyKnownEnumerations()
    {
        var current = Sample(1_000_000_000, 30, 500_000);
        var applied = new AppliedMediaSettings(new(1280, 720, 30, 4), "h264_texture_amf", "h264", true);
        var envelope = PublisherObservation.Envelope(Guid.NewGuid(), current, PublisherRates.Unavailable, new(1920, 1080, 60, 15), applied);
        Assert.Equal("publisher", envelope.Role);
        Assert.Equal(1920, envelope.Metrics["requestedWidth"]); Assert.Equal(1280, envelope.Metrics["appliedWidth"]); Assert.Equal(15, envelope.Metrics["requestedMbps"]);
        Assert.Equal("application", envelope.Metadata["audioMode"]); Assert.Equal("amf", envelope.Metadata["encoderFamily"]); Assert.Equal("unverified", envelope.Metadata["encoderTransferRoute"]);
        var unknown = PublisherObservation.Envelope(Guid.NewGuid(), Sample(0, 0, 0, audioMode: "C:\\private", family: "custom"), PublisherRates.Unavailable, new(1920, 1080, 60, 15), applied);
        Assert.False(unknown.Metadata.ContainsKey("audioMode")); Assert.False(unknown.Metadata.ContainsKey("encoderFamily"));
    }

    [Fact]
    public void DescriptionReportsUnavailableRatesExplicitly()
    {
        var text = PublisherObservation.Describe(Sample(0, 0, 0), PublisherRates.Unavailable);
        Assert.StartsWith("Observed: source 1920×1080, encoded 1280×720; capture unavailable", text);
        Assert.Contains("Encode API 2.5 ms", text);
    }
}
