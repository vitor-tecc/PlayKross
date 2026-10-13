using System.ComponentModel;
using JKiller.Contracts;
using JKiller.Core.Media;
using JKiller.Core.Rooms;
using JKiller.Windows.Capture;
using JKiller.Core.Broadcast;
using JKiller.Core.Capture;
using JKiller.Windows.Rooms;
using JKiller.Windows.Security;
using System.Runtime.CompilerServices;
using JKiller.Windows.Obs;
using JKiller.Core.Diagnostics;
namespace JKiller.Windows.Presentation;
public class BroadcasterModel(Func<bool, IMediaEngine> engineFactory, IRoomClient room, IWindowsSourceCatalog catalog, SessionVault? vault = null) : INotifyPropertyChanged, IAsyncDisposable
{
    private BroadcastController? controller;
    private IMediaEngine? engine;
    private Uri? origin;
    private bool disposed;
    private CancellationTokenSource? diagnosticsCancellation;
    private Task? diagnosticsTask;
    private string observedText = "Observed: unavailable until broadcasting.";
    public string ObservedText { get => observedText; private set { observedText = value; Notify(); } }
    private string status = "Join a room and select a source. Nothing is captured until Start.";
    public string RoomLink { get; set; } = "";
    public string Nickname { get; set; } = "Player";
    public SourceDescriptor? Video { get; set; }
    public SourceDescriptor? Audio { get; set; }
    public bool AllowCpuFallback { get; set; }
    public BroadcastSettings Settings { get; set; } = new(1920, 1080, 60, 15);
    public IReadOnlyList<SourceDescriptor> Sources { get; private set; } = [];
    public string Status { get => status; private set { status = value; Notify(); } }
    public string AppliedText => controller?.Applied is { } applied ? $"Applied: {applied.Settings.Width}×{applied.Settings.Height}, {applied.Settings.Fps} FPS target, {applied.Settings.BitrateMbps} Mbps target; {applied.Encoder}/{applied.Codec}. Hardware initialized: {applied.HardwareVerified}. Delivered FPS/bitrate require measurement." : "No active encoder.";
    public bool CaptureLive => controller?.State == BroadcastState.Broadcasting;
    public static AudioEligibility AudioEligibilityFor(SourceDescriptor? source)
    {
        if (source is null) return new(false, "Audio off. Choose an explicitly supported application; displays never select system audio.");
        var image = source.Process?.ExecutablePath ?? "";
        var name = Path.GetFileName(image.Replace('/', '\\'));
        var classification = name.ToLowerInvariant() switch
        {
            "discord.exe" => ProcessClassification.Discord,
            "chrome.exe" or "msedge.exe" or "firefox.exe" or "brave.exe" or "opera.exe" => ProcessClassification.Browser,
            "geometrydash.exe" when image.Replace('/', '\\').EndsWith(@"\steamapps\common\Geometry Dash\GeometryDash.exe", StringComparison.OrdinalIgnoreCase) => ProcessClassification.VerifiedApplication,
            "capturefixture.exe" => ProcessClassification.VerifiedApplication,
            _ => ProcessClassification.Unknown
        };
        if (source.Process?.Pid == Environment.ProcessId) classification = ProcessClassification.OwnProcessTree;
        return AudioPolicy.Evaluate(source, classification);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    public async Task RefreshAsync(CancellationToken token) { Sources = await catalog.ListSourcesAsync(token); Notify(nameof(Sources)); }
    public async Task JoinAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (CaptureLive) throw new InvalidOperationException("Stop broadcasting before changing rooms.");
        await ReleaseControllerAsync();
        var parsed = RoomHttpClient.ParseLink(RoomLink);
        if (room is RoomHttpClient client && vault is not null && await vault.RestoreAsync(parsed.Origin, token) is { } saved) client.Restore(saved.Origin, saved.Grant);
        var joined = await room.JoinAsync(parsed.Origin, parsed.Invite, Nickname, token); origin = parsed.Origin;
        if (vault is not null) await vault.SaveAsync(new(origin, joined), token);
        Status = "Joined. Select video and optional application audio, then Start.";
        await RefreshAsync(token);
    }
    public async Task RenameAsync(CancellationToken token) { await room.RenameAsync(Nickname, token); Status = "Nickname updated; your session identity stays the same."; }
    public async Task StartAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (origin is null) throw new InvalidOperationException("Join a room first.");
        var video = Video ?? throw new InvalidOperationException("Select the display or window to capture.");
        if (Audio is not null && !AudioEligibilityFor(Audio).Allowed) throw new InvalidOperationException(AudioEligibilityFor(Audio).Reason);
        if (CaptureLive) return;
        await ReleaseControllerAsync();
        engine = engineFactory(AllowCpuFallback); controller = new(engine, room, origin, TimeProvider.System);
        controller.BroadcastStateChanged += OnState;
        await controller.StartAsync(new(video, new(Audio?.Process), Settings), token);
        if (engine is ObsMediaEngine native)
        {
            diagnosticsCancellation = new(); diagnosticsTask = Observe(native, diagnosticsCancellation.Token);
        }
    }
    private void OnState(object? sender, EventArgs args)
    {
        Status = controller?.LastError is { } error && controller.State == BroadcastState.Faulted ? "Broadcast stopped: " + error.Message : controller?.State.ToString() ?? "Stopped";
        Notify(nameof(AppliedText)); Notify(nameof(CaptureLive));
    }
    public async Task ApplySettingsAsync(CancellationToken token)
    {
        if (controller is null) throw new InvalidOperationException("Start a broadcast before applying settings.");
        await controller.ChangeSettingsAsync(Settings, token);
    }
    public async Task StopAsync(CancellationToken token) { await StopDiagnostics(); if (controller is not null) await controller.StopAsync(token); }
    public async Task<RedactedReport> ExportDiagnosticsAsync(CancellationToken token) => room is RoomHttpClient client ? await client.ExportDiagnosticsAsync(token) : throw new InvalidOperationException("Diagnostic export requires a room connection.");
    private async Task Observe(ObsMediaEngine native, CancellationToken token)
    {
        MediaDiagnostics? previous = null;
        while (!token.IsCancellationRequested)
        {
            try
            {
                var current = await native.GetDiagnosticsAsync(token);
                var interval = TimeSpan.FromSeconds(previous is null ? 0 : (current.MonotonicNs - previous.MonotonicNs) / 1e9);
                double? Rate(long? a, long? b) => a is null || b is null ? null : CounterRates.Calculate(a.Value, b.Value, interval);
                var capture = Rate(previous?.CaptureFrames, current.CaptureFrames); var encoded = Rate(previous?.ActualEncodedFrames, current.ActualEncodedFrames);
                var sent = Rate(previous?.SentFrames, current.SentFrames); var mbps = Rate(previous?.SentMediaBytes, current.SentMediaBytes) * 8 / 1e6;
                string Format(double? value) => value is null ? "unavailable" : value.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
                ObservedText = $"Observed: {current.EncodedWidth}×{current.EncodedHeight}; capture {Format(capture)}, encoded {Format(encoded)}, sent {Format(sent)} FPS; {Format(mbps)} Mbps to host. Encode API {Format(current.EncodeApiCallMs)} ms; input→packet {Format(current.InputToOutputPacketMs)} ms; fresh capture→packet {Format(current.FreshCaptureToPacketMs)} ms. GPU kernel timing unavailable. Viewer/network measurements are in Playback diagnostics.";
                if (room is RoomHttpClient client && controller?.StreamId is { } id && controller.Applied is { } applied)
                {
                    var metrics = new Dictionary<string, double?> { ["captureFps"] = capture, ["encodedFps"] = encoded, ["sentFps"] = sent, ["sendMbps"] = mbps,
                        ["compositorFps"] = Rate(previous?.CompositorFrames, current.CompositorFrames), ["encodeApiCallMs"] = current.EncodeApiCallMs, ["inputToOutputPacketMs"] = current.InputToOutputPacketMs,
                        ["captureToRenderMs"] = current.CaptureToRenderMs, ["freshCaptureToPacketMs"] = current.FreshCaptureToPacketMs, ["nativeRenderAverageMs"] = current.NativeRenderAverageMs,
                        ["width"] = current.EncodedWidth, ["height"] = current.EncodedHeight,
                        ["requestedWidth"] = Settings.Width, ["requestedHeight"] = Settings.Height, ["requestedFps"] = Settings.Fps, ["requestedMbps"] = (double)Settings.BitrateMbps,
                        ["appliedWidth"] = applied.Settings.Width, ["appliedHeight"] = applied.Settings.Height, ["appliedFps"] = applied.Settings.Fps, ["appliedMbps"] = (double)applied.Settings.BitrateMbps };
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(3));
                    try { await client.ReportDiagnosticsAsync(new(id, "publisher", metrics, new Dictionary<string, string> { ["encoder"] = current.Encoder, ["codec"] = current.Codec, ["hardwareInitialized"] = current.HardwareInitialized ? "true" : "false", ["provenance"] = "native-monotonic-interval" }), deadline.Token); }
                    catch (Exception e) when (e is HttpRequestException || e is OperationCanceledException && !token.IsCancellationRequested) { }
                }
                previous = current;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception) { previous = null; ObservedText = "Observed: diagnostics unavailable; native safety monitoring remains active."; }
            try { await Task.Delay(1000, token); } catch (OperationCanceledException) { break; }
        }
    }
    private async Task StopDiagnostics()
    {
        diagnosticsCancellation?.Cancel(); if (diagnosticsTask is not null) await diagnosticsTask;
        diagnosticsCancellation?.Dispose(); diagnosticsCancellation = null; diagnosticsTask = null;
        ObservedText = "Observed: unavailable until broadcasting.";
    }
    private async Task ReleaseControllerAsync()
    {
        await StopDiagnostics();
        if (controller is not null) { controller.BroadcastStateChanged -= OnState; await controller.DisposeAsync(); controller = null; }
        if (engine is IAsyncDisposable disposable) await disposable.DisposeAsync(); engine = null;
    }
    public async ValueTask DisposeAsync() { if (disposed) return; disposed = true; await ReleaseControllerAsync(); }
}
