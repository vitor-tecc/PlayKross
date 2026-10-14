using JKiller.Windows.Rooms;
using JKiller.Windows.Viewing;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Windows;
using System.Windows.Controls;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text;
namespace JKiller.App.Viewing;
public sealed class RoomWebView(string root, RoomHttpClient room) : Grid, IDisposable
{
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
            browser?.Dispose(); Children.Clear(); currentRoom = roomUri; policy = new(parsed.Origin);
            var profile = Path.Combine(root, ".runtime/webview", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(parsed.Origin.AbsoluteUri))));
            var info = Directory.CreateDirectory(profile); var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(true, false);
            var user = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Windows identity unavailable.");
            foreach (var sid in new[] { user, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) }) acl.AddAccessRule(new(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            info.SetAccessControl(acl);
            var created = new WebView2(); browser = created; Children.Add(created);
            var environment = await CoreWebView2Environment.CreateAsync(null, profile);
            token.ThrowIfCancellationRequested(); if (disposed) { created.Dispose(); return; }
            await created.EnsureCoreWebView2Async(environment);
            token.ThrowIfCancellationRequested(); if (disposed) { created.Dispose(); return; }
            var core = created.CoreWebView2; core.Settings.AreHostObjectsAllowed = false; core.Settings.IsWebMessageEnabled = false;
            core.Settings.AreDevToolsEnabled = false; core.Settings.AreDefaultContextMenusEnabled = false;
            core.NavigationStarting += (_, args) => args.Cancel = !Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) || !policy.AllowsNavigation(uri);
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
            core.Navigate(roomUri.AbsoluteUri);
        }
        finally { opening = false; }
    }
    private void ShowRecovery(string message)
    {
        if (disposed) return;
        var panel = new StackPanel { Margin = new Thickness(24) }; panel.Children.Add(new TextBlock { Text = message + " Broadcasting controls remain separate.", TextWrapping = TextWrapping.Wrap });
        var retry = new Button { Content = "Reload viewer", Margin = new Thickness(0, 12, 0, 0) };
        retry.Click += async (_, _) => { try { if (currentRoom is not null) await OpenRoomAsync(currentRoom, CancellationToken.None); } catch (Exception error) { MessageBox.Show(error.Message, "PlayKross viewer"); } };
        panel.Children.Add(retry); Children.Clear(); Children.Add(panel);
    }
    public void Dispose() { disposed = true; browser?.Dispose(); browser = null; Children.Clear(); }
}
