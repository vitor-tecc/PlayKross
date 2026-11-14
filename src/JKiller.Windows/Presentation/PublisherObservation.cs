using System.Globalization;
using JKiller.Contracts;
using JKiller.Core.Diagnostics;
namespace JKiller.Windows.Presentation;

/// <summary>Per-second publisher rates derived from two consecutive native counter samples.</summary>
public sealed record PublisherRates(double? CaptureFps, double? EncodedFps, double? SentFps, double? SendMbps, double? CompositorFps)
{
    public static readonly PublisherRates Unavailable = new(null, null, null, null, null);

    public static PublisherRates Between(MediaDiagnostics? previous, MediaDiagnostics current)
    {
        if (previous is null) return Unavailable;
        var interval = TimeSpan.FromSeconds((current.MonotonicNs - previous.MonotonicNs) / 1e9);
        double? Rate(long? a, long? b) => a is null || b is null ? null : CounterRates.Calculate(a.Value, b.Value, interval);
        return new(Rate(previous.CaptureFrames, current.CaptureFrames), Rate(previous.ActualEncodedFrames, current.ActualEncodedFrames),
            Rate(previous.SentFrames, current.SentFrames), Rate(previous.SentMediaBytes, current.SentMediaBytes) * 8 / 1e6,
            Rate(previous.CompositorFrames, current.CompositorFrames));
    }
}

/// <summary>Formats native publisher diagnostics for the UI and the room's diagnostics report.</summary>
public static class PublisherObservation
{
    private static string Format(double? value) => value is null ? "unavailable" : value.Value.ToString("0.0", CultureInfo.InvariantCulture);

    public static string Describe(MediaDiagnostics current, PublisherRates rates) =>
        $"Observed: source {current.CaptureWidth}×{current.CaptureHeight}, encoded {current.EncodedWidth}×{current.EncodedHeight}; capture {Format(rates.CaptureFps)}, encoded {Format(rates.EncodedFps)}, sent {Format(rates.SentFps)} FPS; {Format(rates.SendMbps)} Mbps to host. Encode API {Format(current.EncodeApiCallMs)} ms; input→packet {Format(current.InputToOutputPacketMs)} ms; fresh capture→packet {Format(current.FreshCaptureToPacketMs)} ms. GPU kernel timing unavailable. Audio {current.AudioModeName ?? "unavailable"}; capture initialized/running: {current.AudioRunning}; captured/emitted frames {current.CapturedAudioFrames}/{current.EmittedAudioFrames}; queue {Format(current.AudioQueueMs)} ms, dropped frames {current.DroppedAudioFrames}; active owned video/audio encoders {current.ActiveVideoEncoders}/{current.ActiveAudioEncoders}. Audio error: {current.AudioError ?? "unavailable"}. Viewer audibility requires a playback check. Viewer/network measurements are in Playback diagnostics.";

    public static DiagnosticsEnvelope Envelope(Guid streamId, MediaDiagnostics current, PublisherRates rates, BroadcastSettings requested, AppliedMediaSettings applied)
    {
        var metrics = new Dictionary<string, double?>
        {
            ["audioQueueMs"] = current.AudioQueueMs, ["droppedAudioFrames"] = current.DroppedAudioFrames, ["activeVideoEncoders"] = current.ActiveVideoEncoders, ["activeAudioEncoders"] = current.ActiveAudioEncoders,
            ["captureFps"] = rates.CaptureFps, ["encodedFps"] = rates.EncodedFps, ["sentFps"] = rates.SentFps, ["sendMbps"] = rates.SendMbps,
            ["compositorFps"] = rates.CompositorFps, ["encodeApiCallMs"] = current.EncodeApiCallMs, ["inputToOutputPacketMs"] = current.InputToOutputPacketMs,
            ["captureToRenderMs"] = current.CaptureToRenderMs, ["freshCaptureToPacketMs"] = current.FreshCaptureToPacketMs, ["nativeRenderAverageMs"] = current.NativeRenderAverageMs,
            ["width"] = current.EncodedWidth, ["height"] = current.EncodedHeight, ["sourceWidth"] = current.CaptureWidth, ["sourceHeight"] = current.CaptureHeight,
            ["requestedWidth"] = requested.Width, ["requestedHeight"] = requested.Height, ["requestedFps"] = requested.Fps, ["requestedMbps"] = (double)requested.BitrateMbps,
            ["appliedWidth"] = applied.Settings.Width, ["appliedHeight"] = applied.Settings.Height, ["appliedFps"] = applied.Settings.Fps, ["appliedMbps"] = (double)applied.Settings.BitrateMbps,
        };
        var metadata = new Dictionary<string, string>
        {
            ["encoder"] = current.Encoder, ["codec"] = current.Codec, ["captureApi"] = current.CaptureApi,
            ["hardwareInitialized"] = current.HardwareInitialized ? "true" : "false", ["provenance"] = "native-monotonic-interval",
        };
        // Only known enumerations leave the broadcaster; free-form native strings stay local.
        if (current.AudioModeName is "off" or "application" or "system-output") metadata["audioMode"] = current.AudioModeName;
        if (current.EncoderFamily is "nvenc" or "amf" or "x264") metadata["encoderFamily"] = current.EncoderFamily;
        if (current.EncoderTransferRoute == "unverified") metadata["encoderTransferRoute"] = current.EncoderTransferRoute;
        return new(streamId, "publisher", metrics, metadata);
    }
}
