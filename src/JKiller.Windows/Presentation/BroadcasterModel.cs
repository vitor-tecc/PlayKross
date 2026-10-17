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
public class BroadcasterModel(Func<bool, IMediaEngine> engineFactory, IRoomClient room, IWindowsSourceCatalog catalog, SessionVault? vault = null, IWindowsAudioCatalog? audioCatalog = null) : INotifyPropertyChanged, IAsyncDisposable
{
    private BroadcastController? controller;
    private IMediaEngine? engine;
    private Uri? origin;
    private bool disposed;
    private CancellationTokenSource? diagnosticsCancellation;
    private Task? diagnosticsTask;
    private CancellationTokenSource? presenceCancellation;
    private Task? presenceTask;
    private string observedText = "Observed: unavailable until broadcasting.";
    public string ObservedText { get => observedText; private set { observedText = value; Notify(); } }
    private string status = "Join a room and select a source. Nothing is captured until Start.";
    public string RoomLink { get; set; } = "";
    public string Nickname { get; set; } = "Player";
    private SourceDescriptor? video;
    private AudioSelection audio = AudioSelection.Off;
    public SourceDescriptor? Video
    {
        get => video;
        set
        {
            video = value; Notify();
            if (video?.Kind == SourceKind.Window && audio.Mode == AudioMode.SystemOutput)
            {
                Audio = AudioSelection.Off;
                Status = CaptureLive
                    ? "Audio off is staged for the selected window; current capture continues until Apply sources and settings (restart). System output requires sharing a display."
                    : "Audio turned off: system output requires sharing a display. Select application audio for a window.";
            }
            Notify(nameof(AudioEligibility));
        }
    }
    public AudioSelection Audio { get => audio; set { ArgumentNullException.ThrowIfNull(value); if (audio == value) return; audio = value; Notify(); Notify(nameof(AudioEligibility)); } }
    public IReadOnlyList<AudioOutputDescriptor> AudioOutputs { get; private set; } = [];
    public AudioEligibility AudioEligibility
    {
        get
        {
            var eligibility = AudioPolicy.EvaluateSelection(Audio, Video?.Kind ?? SourceKind.Window, Environment.ProcessId);
            return eligibility.Allowed && Audio.Mode == AudioMode.SystemOutput && !AudioOutputs.Any(output => output.Id == Audio.RenderEndpointId)
                ? new(false, "The selected output device is unavailable. Select an available output again; capture will not follow the default device.") : eligibility;
        }
    }
    public void SelectApplicationAudio(SourceDescriptor? source) => Audio = source is null ? AudioSelection.Off : new(AudioMode.Application, source.Process);
    public void SelectSystemAudio(AudioOutputDescriptor output) => Audio = new(AudioMode.SystemOutput, RenderEndpointId: output.Id);
    public bool AllowCpuFallback { get; set; }
    public BroadcastSettings Settings { get; set; } = new(1920, 1080, 60, 15);
    public IReadOnlyList<SourceDescriptor> Sources { get; private set; } = [];
    public IReadOnlyList<SourceDescriptor> AudioApplications => Sources.Where(source => source.Kind == SourceKind.Window).ToArray();
    public string Status { get => status; private set { status = value; Notify(); } }
    public string AppliedText => controller?.Applied is { } applied ? $"Applied: {applied.Settings.Width}×{applied.Settings.Height}, {applied.Settings.Fps} FPS target, {applied.Settings.BitrateMbps} Mbps target; {applied.Encoder}/{applied.Codec}. Hardware initialized: {applied.HardwareVerified}. Delivered FPS/bitrate require measurement." : "No active encoder.";
    public bool CaptureLive => controller?.State == BroadcastState.Broadcasting;
    public bool IsJoined => origin is not null;
    public static AudioEligibility AudioEligibilityFor(SourceDescriptor? source)
    {
        if (source is null) return new(false, "Select a running application for application audio.");
        return AudioPolicy.EvaluateApplication(source.Process, Environment.ProcessId);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    public async Task RefreshAsync(CancellationToken token)
    {
        Sources = await catalog.ListSourcesAsync(token); Notify(nameof(Sources)); Notify(nameof(AudioApplications));
        if (audioCatalog is not null) AudioOutputs = await audioCatalog.ListOutputsAsync(token);
        Notify(nameof(AudioOutputs)); Notify(nameof(AudioEligibility));
    }
    public async Task JoinAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (CaptureLive) throw new InvalidOperationException("Stop broadcasting before changing rooms.");
        var parsed = RoomHttpClient.ParseLink(RoomLink);
        if (origin is not null && origin != parsed.Origin) await LeaveRoomCore(token);
        if (origin is null && room is RoomHttpClient client && vault is not null && await vault.RestoreAsync(parsed.Origin, token) is { } saved) client.Restore(saved.Origin, saved.Grant);
        var joined = await room.JoinAsync(parsed.Origin, parsed.Invite, Nickname, token); origin = parsed.Origin;
        await StopPresenceAsync();
        await ReleaseControllerAsync();
        Notify(nameof(IsJoined));
        presenceCancellation = new(); presenceTask = MaintainPresence(presenceCancellation.Token);
        await room.HeartbeatAsync(token);
        if (vault is not null) await vault.SaveAsync(new(origin, joined), token);
        Status = "Joined. Select video, then Start. Audio stays off until you explicitly select application or system output audio.";
        await RefreshAsync(token);
    }
    public async Task RenameAsync(CancellationToken token) { await room.RenameAsync(Nickname, token); Status = "Nickname updated; your session identity stays the same."; }
    public async Task StartAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (origin is null) throw new InvalidOperationException("Join a room first.");
        var video = Video ?? throw new InvalidOperationException("Select the display or window to capture.");
        if (CaptureLive) return;
        if (!AudioEligibility.Allowed) throw new InvalidOperationException(AudioEligibility.Reason);
        await ReleaseControllerAsync();
        engine = engineFactory(AllowCpuFallback); controller = new(engine, room, origin, TimeProvider.System);
        controller.BroadcastStateChanged += OnState;
        await controller.StartAsync(new(video, Audio, Settings), token);
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
        var selectedVideo = Video ?? throw new InvalidOperationException("Select the display or window to capture.");
        if (!AudioEligibility.Allowed) throw new InvalidOperationException(AudioEligibility.Reason);
        await controller.ChangeSourcesAsync(new(selectedVideo, Audio, Settings), token);
    }
    public async Task StopAsync(CancellationToken token) { await StopDiagnostics(); if (controller is not null) await controller.StopAsync(token); }
    public Task LeaveAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return LeaveRoomCore(token);
    }
    private async Task MaintainPresence(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), token);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(5));
                await room.HeartbeatAsync(deadline.Token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception error) when (error is HttpRequestException or OperationCanceledException)
            {
                // A temporary network interruption may recover within the server's
                // reconnect grace. Publication has its separate fail-closed lease.
                if (!disposed) Status = "Room connection interrupted; trying to reconnect.";
            }
            catch (Exception error) when (error is InvalidOperationException or InvalidDataException or System.Text.Json.JsonException)
            {
                if (!disposed) Status = "Room presence could not be renewed. Join the room again.";
                break;
            }
        }
    }
    private async Task StopPresenceAsync()
    {
        presenceCancellation?.Cancel();
        if (presenceTask is not null) await presenceTask;
        presenceCancellation?.Dispose(); presenceCancellation = null; presenceTask = null;
    }
    private async Task LeaveRoomCore(CancellationToken token)
    {
        await StopPresenceAsync(); await ReleaseControllerAsync();
        try { if (origin is not null) await room.LeaveAsync(token); }
        finally { origin = null; Notify(nameof(IsJoined)); Status = "Left room."; Notify(nameof(CaptureLive)); }
    }
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
                ObservedText = $"Observed: source {current.CaptureWidth}×{current.CaptureHeight}, encoded {current.EncodedWidth}×{current.EncodedHeight}; capture {Format(capture)}, encoded {Format(encoded)}, sent {Format(sent)} FPS; {Format(mbps)} Mbps to host. Encode API {Format(current.EncodeApiCallMs)} ms; input→packet {Format(current.InputToOutputPacketMs)} ms; fresh capture→packet {Format(current.FreshCaptureToPacketMs)} ms. GPU kernel timing unavailable. Audio {current.AudioModeName ?? "unavailable"}; capture initialized/running: {current.AudioRunning}; captured/emitted frames {current.CapturedAudioFrames}/{current.EmittedAudioFrames}; queue {Format(current.AudioQueueMs)} ms, dropped frames {current.DroppedAudioFrames}; active owned video/audio encoders {current.ActiveVideoEncoders}/{current.ActiveAudioEncoders}. Audio error: {current.AudioError ?? "unavailable"}. Viewer audibility requires a playback check. Viewer/network measurements are in Playback diagnostics.";
                if (room is RoomHttpClient client && controller?.StreamId is { } id && controller.Applied is { } applied)
                {
                    var metrics = new Dictionary<string, double?> { ["audioQueueMs"] = current.AudioQueueMs, ["droppedAudioFrames"] = current.DroppedAudioFrames, ["activeVideoEncoders"] = current.ActiveVideoEncoders, ["activeAudioEncoders"] = current.ActiveAudioEncoders,
                        ["captureFps"] = capture, ["encodedFps"] = encoded, ["sentFps"] = sent, ["sendMbps"] = mbps,
                        ["compositorFps"] = Rate(previous?.CompositorFrames, current.CompositorFrames), ["encodeApiCallMs"] = current.EncodeApiCallMs, ["inputToOutputPacketMs"] = current.InputToOutputPacketMs,
                        ["captureToRenderMs"] = current.CaptureToRenderMs, ["freshCaptureToPacketMs"] = current.FreshCaptureToPacketMs, ["nativeRenderAverageMs"] = current.NativeRenderAverageMs,
                        ["width"] = current.EncodedWidth, ["height"] = current.EncodedHeight, ["sourceWidth"] = current.CaptureWidth, ["sourceHeight"] = current.CaptureHeight,
                        ["requestedWidth"] = Settings.Width, ["requestedHeight"] = Settings.Height, ["requestedFps"] = Settings.Fps, ["requestedMbps"] = (double)Settings.BitrateMbps,
                        ["appliedWidth"] = applied.Settings.Width, ["appliedHeight"] = applied.Settings.Height, ["appliedFps"] = applied.Settings.Fps, ["appliedMbps"] = (double)applied.Settings.BitrateMbps };
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(3));
                    var metadata = new Dictionary<string, string> { ["encoder"] = current.Encoder, ["codec"] = current.Codec, ["captureApi"] = current.CaptureApi, ["hardwareInitialized"] = current.HardwareInitialized ? "true" : "false", ["provenance"] = "native-monotonic-interval" };
                    if (current.AudioModeName is "off" or "application" or "system-output") metadata["audioMode"] = current.AudioModeName;
                    try { await client.ReportDiagnosticsAsync(new(id, "publisher", metrics, metadata), deadline.Token); }
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
    public async ValueTask DisposeAsync()
    {
        if (disposed) return; disposed = true;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { await LeaveRoomCore(deadline.Token); }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException) { /* Server presence expires if close cannot reach it. */ }
    }
}
