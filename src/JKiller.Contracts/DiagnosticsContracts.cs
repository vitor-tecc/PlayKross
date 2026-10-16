namespace JKiller.Contracts;
// Missing nullable observations mean unavailable, never a fabricated zero.
public sealed record MediaDiagnostics(
    string Encoder, string Codec, bool HardwareInitialized, string CaptureApi,
    bool AcquisitionHookAvailable, bool TraceEnabled, long? CaptureFrames, long? CompositorFrames, long? RepeatedSourceFrames,
    long? EncodedPackets, long ActualEncodedFrames, long SentMediaBytes, long SentFrames, int EncodedWidth, int EncodedHeight,
    int RequestedFpsNumerator, int RequestedFpsDenominator, double? EncodeApiCallMs,
    double? InputToOutputPacketMs, double? CaptureToRenderMs, double? FreshCaptureToPacketMs, long? TimingSamples, long? CaptureTimingSamples, long? UnmatchedPackets,
    string TimingProvenance, string? CaptureUnavailableReason, string GpuKernelTiming,
    long? CapturedAudioFrames, long? EmittedAudioFrames, bool AudioRunning, bool OutputActive, long MonotonicNs,
    double NativeRenderAverageMs, long RenderClockFrames, long RenderLagFrames, long? EncodingClockFrames = null, long? EncodingSkippedFrames = null, int? CaptureWidth = null, int? CaptureHeight = null,
    string? AudioModeName = null, string? AudioError = null, double? AudioQueueMs = null, long? DroppedAudioFrames = null, int? ActiveVideoEncoders = null, int? ActiveAudioEncoders = null);
