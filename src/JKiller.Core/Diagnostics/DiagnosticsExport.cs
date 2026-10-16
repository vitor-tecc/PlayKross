using JKiller.Contracts;
namespace JKiller.Core.Diagnostics;
public static class DiagnosticsExport
{
    private static readonly HashSet<string> Metrics = new(StringComparer.Ordinal) {
        "captureFps", "sourceWidth", "sourceHeight", "compositorFps", "encodedFps", "sentFps", "sendMbps", "receiveMbps", "decodedFps", "presentedFps",
        "iceRttMs", "videoLatencyMs", "packetLossPercent", "jitterMs", "width", "height", "startupMs", "stalls", "longestGapMs",
        "encodeApiCallMs", "inputToOutputPacketMs", "captureToRenderMs", "freshCaptureToPacketMs", "nativeRenderAverageMs",
        "requestedWidth", "requestedHeight", "requestedFps", "requestedMbps", "appliedWidth", "appliedHeight", "appliedFps", "appliedMbps",
        "mediaBytesSent", "mediaBytesReceived", "mediaSendMbps", "mediaReceiveMbps", "activeMediaSessions", "activeReaders", "hostNicBytesSent", "hostNicBytesReceived", "estimatedViewerMbps", "serverReceivedUnixMs" };
    private static readonly Dictionary<string, HashSet<string>> Metadata = new(StringComparer.Ordinal) {
        ["codec"] = new(["h264", "H264", "opus", "video/H264", "audio/opus", "video/VP8", "video/VP9", "video/AV1"], StringComparer.Ordinal),
        ["encoder"] = new(["obs_nvenc_h264_tex", "obs_x264"], StringComparer.Ordinal),
        ["captureApi"] = new(["Windows Graphics Capture", "Windows Graphics Capture / D3D11 texture", "WGC"], StringComparer.Ordinal),
        ["connectionPath"] = new(["unknown", "direct", "relayed"], StringComparer.Ordinal),
        ["hardwareInitialized"] = new(["true", "false"], StringComparer.Ordinal),
        ["estimateBasis"] = new(["video-payload-only"], StringComparer.Ordinal),
        ["provenance"] = new(["browser-rolling-5s", "native-monotonic-interval", "mediamtx-api-and-all-active-nics"], StringComparer.Ordinal) };
    public static RedactedReport Redact(IEnumerable<DiagnosticsEnvelope> samples) => new(1, samples.Take(4096)
        .Where(s => s.Role is "publisher" or "viewer" or "host")
        .Select(s => new DiagnosticsEnvelope(s.StreamId, s.Role,
            s.Metrics.Where(p => Metrics.Contains(p.Key) && (p.Value is null || double.IsFinite(p.Value.Value) && p.Value >= 0)).ToDictionary(),
            s.Metadata.Where(p => Metadata.TryGetValue(p.Key, out var allowed) && allowed.Contains(p.Value)).ToDictionary(), s.ObserverId)).ToArray());
}
