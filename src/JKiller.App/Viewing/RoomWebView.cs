using JKiller.Windows.Rooms;
using JKiller.Windows.Viewing;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Windows;
using System.Windows.Controls;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
namespace JKiller.App.Viewing;
public sealed class RoomWebView : Grid, IDisposable
{
    private readonly string root;
    private readonly RoomHttpClient room;
    private readonly PlaybackAudioGate playbackGate;
    private readonly IDisposable playbackRegistration;
    private bool playbackBlocked;
    private readonly SemaphoreSlim playbackScriptGate = new(1);
    private string? playbackScriptId;
    private readonly Grid browserArea = new();
    private readonly TextBlock playbackNoticeText = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border playbackNotice = new() { Padding = new Thickness(12), Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0E, 0x18, 0x2A)), VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed };
    public RoomWebView(string root, RoomHttpClient room, PlaybackAudioGate playbackAudioGate)
    {
        this.root = root; this.room = room; playbackGate = playbackAudioGate;
        // WebView2 owns HWND airspace. Native guidance needs its own nonoverlapping
        // Auto row; collapsing it leaves the browser's normal/fullscreen area intact.
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(playbackNotice, 0); Grid.SetRow(browserArea, 1);
        var noticeContents = new DockPanel { LastChildFill = true };
        var reload = new Button { Content = "Reload viewer", Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(reload, Dock.Right); noticeContents.Children.Add(reload); noticeContents.Children.Add(playbackNoticeText);
        playbackNoticeText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x82, 0xA7, 0xBA));
        playbackNotice.Child = noticeContents;
        reload.Click += (_, _) =>
        {
            try
            {
                if (disposed || browser?.CoreWebView2 is not { } core) return;
                // Reload the page in the existing browser; preserve the native safety boundary.
                core.IsMuted = playbackBlocked; core.Reload();
            }
            catch (Exception) { ShowPlaybackNotice(); }
        };
        Children.Add(browserArea); Children.Add(playbackNotice);
        playbackBlocked = playbackGate.IsBlocked;
        playbackRegistration = playbackGate.Register(SetPlaybackAudioBlockedAsync);
    }
    private WebView2? browser;
    private Uri? currentRoom;
    private RoomWebViewPolicy? policy;
    private bool disposed, opening;
    public event EventHandler? FullscreenChanged;
    public bool IsFullscreen => browser?.CoreWebView2?.ContainsFullScreenElement == true;
    // Available to the application-owned integration probe, not exposed to web content.
    internal CoreWebView2 Core => browser?.CoreWebView2 ?? throw new InvalidOperationException("Viewer is not initialized.");
    public async Task OpenRoomAsync(Uri roomUri, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (opening) throw new InvalidOperationException("Viewer is already opening.");
        var parsed = RoomHttpClient.ParseLink(roomUri.AbsoluteUri);
        if (room.Origin != parsed.Origin || room.Session is not { } session || session.ExpiresAt <= DateTimeOffset.UtcNow) throw new InvalidOperationException("Join this room in the native app before opening its viewer.");
        opening = true;
        try
        {
            if (string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString())) throw new InvalidOperationException("Install Microsoft's Evergreen WebView2 Runtime to use the in-app viewer. Browser viewing remains available.");
            browser?.Dispose(); browserArea.Children.Clear(); playbackNotice.Visibility = Visibility.Collapsed; playbackScriptId = null; currentRoom = roomUri; policy = new(parsed.Origin);
            var profile = WebViewProfilePath.ForRoom(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), root, parsed.Origin);
            var created = new WebView2(); browser = created; browserArea.Children.Add(created);
            try
            {
                var info = Directory.CreateDirectory(profile); var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(true, false);
                var user = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Windows identity unavailable.");
                foreach (var sid in new[] { user, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) }) acl.AddAccessRule(new(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                info.SetAccessControl(acl);
                var environment = await CoreWebView2Environment.CreateAsync(null, profile);
                token.ThrowIfCancellationRequested(); if (disposed) { created.Dispose(); return; }
                await created.EnsureCoreWebView2Async(environment);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                throw new InvalidOperationException($"The in-app viewer could not open its browser data folder: {profile}. Check that your Windows account can write there and that Microsoft's Evergreen WebView2 Runtime is installed, then reopen the viewer. Browser viewing remains available.", error);
            }
            token.ThrowIfCancellationRequested(); if (disposed) { created.Dispose(); return; }
            var core = created.CoreWebView2; core.Settings.AreHostObjectsAllowed = false; core.Settings.IsWebMessageEnabled = false;
            playbackBlocked = playbackGate.IsBlocked;
            core.IsMuted = playbackBlocked;
            core.IsMutedChanged += (_, _) => { if (playbackBlocked && !disposed && core.IsMuted == false) core.IsMuted = true; };
            core.Settings.AreDevToolsEnabled = false; core.Settings.AreDefaultContextMenusEnabled = false;
            core.NavigationStarting += (_, args) => { core.IsMuted = playbackBlocked; args.Cancel = !Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) || !policy.AllowsNavigation(uri); };
            core.NavigationCompleted += async (_, _) => { try { await RefreshPlaybackScriptAsync(core); } catch (Exception) { ShowPlaybackNotice(); } };
            core.FrameNavigationStarting += (_, args) => args.Cancel = !Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) || !policy.AllowsNavigation(uri);
            core.NewWindowRequested += (_, args) => args.Handled = !policy.AllowsPopup;
            core.PermissionRequested += (_, args) => { args.State = CoreWebView2PermissionState.Deny; args.SavesInProfile = false; args.Handled = true; };
            core.DownloadStarting += (_, args) => args.Cancel = true;
            core.ServerCertificateErrorDetected += (_, args) => args.Action = CoreWebView2ServerCertificateErrorAction.Cancel;
            core.ContainsFullScreenElementChanged += (_, _) => FullscreenChanged?.Invoke(this, EventArgs.Empty);
            core.ProcessFailed += (_, args) => ShowRecovery(args.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited ? "Viewer browser process stopped." : "Viewer rendering stopped.");
            var cookie = core.CookieManager.CreateCookie("jk-session", session.SessionToken, parsed.Origin.Host, "/");
            cookie.IsHttpOnly = true; cookie.IsSecure = parsed.Origin.Scheme == "https"; cookie.SameSite = CoreWebView2CookieSameSiteKind.Strict; cookie.Expires = session.ExpiresAt.UtcDateTime;
            core.CookieManager.AddOrUpdateCookie(cookie);
            await RefreshPlaybackScriptAsync(core);
            token.ThrowIfCancellationRequested(); if (disposed) return;
            // Async initialization can overlap acquire/release; consult the shared state once
            // more immediately before navigation. Native mute is the authoritative boundary.
            playbackBlocked = playbackGate.IsBlocked; core.IsMuted = playbackBlocked;
            core.Navigate(roomUri.AbsoluteUri);
        }
        finally { opening = false; }
    }
    public Task SetPlaybackAudioBlockedAsync(bool blocked, CancellationToken token) => Dispatcher.InvokeAsync(async () =>
    {
        token.ThrowIfCancellationRequested(); if (disposed) return;
        playbackBlocked = blocked;
        if (browser?.CoreWebView2 is not { } core) return;
        // A native mute failure propagates to AcquireAsync and prevents capture startup.
        core.IsMuted = blocked;
        await RefreshPlaybackScriptAsync(core);
    }, System.Windows.Threading.DispatcherPriority.Send, token).Task.Unwrap();

    private async Task RefreshPlaybackScriptAsync(CoreWebView2 core)
    {
        await playbackScriptGate.WaitAsync();
        try
        {
            if (disposed || browser?.CoreWebView2 != core) return;
            var script = PlaybackScript(playbackBlocked);
            var previous = playbackScriptId;
            var added = await core.AddScriptToExecuteOnDocumentCreatedAsync(script);
            if (disposed || browser?.CoreWebView2 != core) return;
            playbackScriptId = added;
            if (previous is not null) core.RemoveScriptToExecuteOnDocumentCreated(previous);
            var result = await core.ExecuteScriptAsync(script);
            // Before the first navigation, about:blank is expected and receives no room flag.
            if (currentRoom is not null && Uri.TryCreate(core.Source, UriKind.Absolute, out var source) && policy?.AllowsNavigation(source) == true && result != "true")
                throw new InvalidOperationException("Viewer sound controls could not be updated.");
            playbackNotice.Visibility = Visibility.Collapsed;
        }
        catch (Exception) { ShowPlaybackNotice(); }
        finally { playbackScriptGate.Release(); }
    }

    private string PlaybackScript(bool blocked)
    {
        var origin = JsonSerializer.Serialize(room.Origin!.GetLeftPart(UriPartial.Authority));
        var value = blocked ? "true" : "false";
        return $"(()=>{{if(location.origin!=={origin})return false;Object.defineProperty(window,'playkrossPlaybackAudioBlocked',{{value:{value},configurable:true}});window.dispatchEvent(new CustomEvent('playkross-playback-audio-blocked',{{detail:{value}}}));return true;}})()";
    }
    private void ShowPlaybackNotice()
    {
        if (disposed) return;
        playbackNoticeText.Text = playbackBlocked
            ? "Room sound is muted during system audio sharing. Reload the viewer to refresh sound controls, or stop system audio sharing to restore sound."
            : "Viewer sound controls could not be refreshed. Reload the viewer to restore your sound choice.";
        playbackNotice.Visibility = Visibility.Visible;
    }
    private void ShowRecovery(string message)
    {
        if (disposed) return;
        var panel = new StackPanel { Margin = new Thickness(24) }; panel.Children.Add(new TextBlock { Text = message + " Broadcasting controls remain separate.", TextWrapping = TextWrapping.Wrap });
        var retry = new Button { Content = "Reload viewer", Margin = new Thickness(0, 12, 0, 0) };
        retry.Click += async (_, _) => { try { if (currentRoom is not null) await OpenRoomAsync(currentRoom, CancellationToken.None); } catch (Exception error) { MessageBox.Show(error.Message, "PlayKross viewer"); } };
        panel.Children.Add(retry); playbackNotice.Visibility = Visibility.Collapsed; browserArea.Children.Clear(); browserArea.Children.Add(panel);
    }
    public void Dispose() { disposed = true; playbackRegistration.Dispose(); browser?.Dispose(); browser = null; Children.Clear(); }
}
