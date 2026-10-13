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
namespace JKiller.Windows.Presentation;
public class BroadcasterModel(Func<bool, IMediaEngine> engineFactory, IRoomClient room, IWindowsSourceCatalog catalog, SessionVault? vault = null) : INotifyPropertyChanged, IAsyncDisposable
{
    private BroadcastController? controller;
    private IMediaEngine? engine;
    private Uri? origin;
    private bool disposed;
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
    public async Task StopAsync(CancellationToken token) { if (controller is not null) await controller.StopAsync(token); }
    private async Task ReleaseControllerAsync()
    {
        if (controller is not null) { controller.BroadcastStateChanged -= OnState; await controller.DisposeAsync(); controller = null; }
        if (engine is IAsyncDisposable disposable) await disposable.DisposeAsync(); engine = null;
    }
    public async ValueTask DisposeAsync() { if (disposed) return; disposed = true; await ReleaseControllerAsync(); }
}
