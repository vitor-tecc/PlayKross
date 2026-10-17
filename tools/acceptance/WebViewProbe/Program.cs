using JKiller.App.Viewing;
using JKiller.Windows.Presentation;
using JKiller.Windows.Rooms;
using JKiller.Windows.Capture;
using JKiller.Windows.Obs;
using JKiller.Contracts;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.IO;
using System.Net.Http;
using JKiller.Acceptance.Probes;
using JKiller.Windows.Viewing;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;
internal static class Program
{
    [STAThread] private static int Main(string[] args)
    {
        // Pure WPF measure/arrange: no Window, WebView/Core initialization, capture or device access.
        if (args.SequenceEqual(new[] { "--check-playback-notice-layout" }))
        {
            try { CheckPlaybackNoticeLayout(); return 0; }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; int result = 1;
        app.Startup += async (_, _) => { try { await Run(); result = 0; } catch (Exception error) { Console.Error.WriteLine(error); } finally { app.Shutdown(); } };
        app.Run(); return result;
    }
    private static void CheckPlaybackNoticeLayout()
    {
        using var http = new HttpClient(); var playbackGate = new PlaybackAudioGate();
        using var viewer = new RoomWebView(Environment.CurrentDirectory, new RoomHttpClient(http), playbackGate);
        var area = viewer.Children.OfType<Grid>().SingleOrDefault() ?? throw new Exception("Viewer browser area is not available without initializing WebView.");
        var notice = viewer.Children.OfType<Border>().Single();
        var reload = ((DockPanel)notice.Child).Children.OfType<Button>().Single();
        var explanation = ((DockPanel)notice.Child).Children.OfType<TextBlock>().Single();
        explanation.Text = "Room sound is muted during system audio sharing. Reload the viewer to refresh sound controls, or stop system audio sharing to restore sound.";
        var surface = new Border(); area.Children.Add(surface);
        void Layout() { viewer.Measure(new Size(900, 600)); viewer.Arrange(new Rect(0, 0, 900, 600)); viewer.UpdateLayout(); }
        Layout();
        if (Math.Abs(area.ActualHeight - 600) > 0.5 || notice.ActualHeight > 0.5) throw new Exception("Collapsed notice consumes normal viewer/fullscreen space.");
        notice.Visibility = Visibility.Visible; Layout();
        var noticeTop = notice.TranslatePoint(new Point(), viewer).Y;
        var browserTop = area.TranslatePoint(new Point(), viewer).Y;
        if (notice.ActualHeight <= 0 || explanation.ActualHeight <= 0 || reload.ActualHeight <= 0 || !reload.IsEnabled || area.ActualHeight <= 0 || noticeTop + notice.ActualHeight > browserTop + 0.5)
            throw new Exception("Failure guidance/reload overlaps the browser airspace or leaves no usable browser surface.");
        if (!ReferenceEquals(surface, area.Children.OfType<Border>().Single())) throw new Exception("Showing guidance replaced the viewer surface.");
        notice.Visibility = Visibility.Collapsed; Layout();
        if (Math.Abs(area.ActualHeight - 600) > 0.5) throw new Exception("Dismissing the notice did not restore normal viewer/fullscreen space.");
        Console.WriteLine("PASS: headless real viewer layout gives failure guidance/reload separate space, preserves surface identity, and restores full browser space when collapsed; no Window or WebView initialized.");
    }
    private static async Task Run()
    {
        var root = Environment.CurrentDirectory; using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var token = deadline.Token;
        using var fixture = Process.GetProcessById(int.Parse(Environment.GetEnvironmentVariable("JKILLER_FIXTURE_PID")!)); fixture.Refresh();
        var identity = ProcessIdentityReader.Read(fixture.Id) ?? throw new Exception("Fixture disappeared.");
        if (!string.Equals(Path.GetFullPath(identity.ExecutablePath), Path.GetFullPath(Path.Combine(root, ".artifacts/native/Debug/CaptureFixture.exe")), StringComparison.OrdinalIgnoreCase)) throw new Exception("Only the synthetic fixture may be used in this probe.");
        using var http = new HttpClient(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false }); var room = new RoomHttpClient(http);
        var playbackAudioGate = new PlaybackAudioGate();
        await using var model = new BroadcasterModel(fallback => new ObsMediaEngine(new OwnedObsProcess(root), fallback, playbackAudioGate), room, new WindowsSourceCatalog(), audioCatalog: new WindowsAudioCatalog());
        var link = Environment.GetEnvironmentVariable("JKILLER_PROBE_JOIN_LINK")!; model.RoomLink = link; model.Nickname = "Embedded viewer probe";
        await model.JoinAsync(token);
        var captureMode = Environment.GetEnvironmentVariable("JKILLER_PROBE_CAPTURE_MODE") ?? "application";
        model.Video = captureMode == "system-output"
            ? model.Sources.Single(s => s.Kind == SourceKind.Monitor && s.Id == (Environment.GetEnvironmentVariable("JKILLER_PROBE_MONITOR_ID") ?? throw new Exception("System-output probe requires an explicit monitor ID.")))
            : model.Sources.Single(s => s.Kind == SourceKind.Window && s.NativeHandle == unchecked((ulong)(long)fixture.MainWindowHandle));
        model.Audio = ProbeCaptureMode.SelectAudio(captureMode, model.Video, Environment.GetEnvironmentVariable("JKILLER_PROBE_RENDER_ENDPOINT_ID"));
        await model.StartAsync(token);
        using var viewer = new RoomWebView(root, room, playbackAudioGate); var window = new Window { Content = viewer, Width = 1080, Height = 850, Title = "JKiller synthetic WebView validation" }; window.Show();
        try
        {
            await viewer.OpenRoomAsync(new Uri(link), token);
            if (viewer.Core.IsMuted != playbackAudioGate.IsBlocked) throw new Exception("Late-opened viewer did not inherit native playback mute.");
            await Wait(async () => await viewer.Core.ExecuteScriptAsync("[...document.querySelectorAll('button')].some(b=>b.textContent==='Watch stream')") == "true", token);
            await viewer.Core.ExecuteScriptAsync("[...document.querySelectorAll('button')].find(b=>b.textContent==='Watch stream').click()");
            await Wait(async () => await viewer.Core.ExecuteScriptAsync("document.querySelector('video')?.videoHeight===1080 && document.querySelector('video').getVideoPlaybackQuality().totalVideoFrames>120") == "true", token);
            var sessionId = room.Session!.SessionId.ToString();
            var before = int.Parse(await viewer.Core.ExecuteScriptAsync("document.querySelector('video').getVideoPlaybackQuality().totalVideoFrames"));
            // Change the nickname through the native control path while the embedded peer stays active.
            model.Nickname = "Renamed embedded probe"; await model.RenameAsync(token);
            await Wait(async () => await viewer.Core.ExecuteScriptAsync("document.body.textContent.includes('Renamed embedded probe')") == "true", token);
            await Click(viewer, "Fullscreen"); await Wait(() => Task.FromResult(viewer.IsFullscreen), token);
            await Click(viewer, "Return to room"); await Wait(() => Task.FromResult(!viewer.IsFullscreen), token);
            await Wait(async () => int.Parse(await viewer.Core.ExecuteScriptAsync("document.querySelector('video').getVideoPlaybackQuality().totalVideoFrames")) > before + 60, token);
            await viewer.Core.ExecuteScriptAsync("fetch('/api/me').then(r=>r.json()).then(s=>window.jkProbeSession=s.sessionId)");
            await Wait(async () => await viewer.Core.ExecuteScriptAsync($"window.jkProbeSession==='{sessionId}'") == "true", token);
            if (room.Session.SessionId.ToString() != sessionId || !model.CaptureLive) throw new Exception("Viewing disturbed publication ownership.");
            if (viewer.Core.Settings.AreHostObjectsAllowed || viewer.Core.Settings.IsWebMessageEnabled) throw new Exception("Native bridge is exposed.");
            await CheckScriptFailureNotice(viewer, playbackAudioGate, token);
            var source = viewer.Core.Source; viewer.Core.Navigate("https://untrusted.invalid/"); await Task.Delay(300, token);
            if (new Uri(viewer.Core.Source).Host != new Uri(source).Host) throw new Exception("Untrusted navigation was permitted.");
            try { await viewer.Core.CallDevToolsProtocolMethodAsync("Page.crash", "{}").WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception e) when (e is System.Runtime.InteropServices.COMException or TimeoutException) { }
            await Wait(() => Task.FromResult(viewer.Children.OfType<Grid>().Single().Children.OfType<StackPanel>().Any()), token);
            if (!model.CaptureLive) throw new Exception("Viewer failure stopped the native publication.");
            await viewer.OpenRoomAsync(new Uri(link), token);
            await Wait(async () => await viewer.Core.ExecuteScriptAsync("[...document.querySelectorAll('button')].some(b=>b.textContent==='Watch stream')") == "true", token);
            await viewer.Core.ExecuteScriptAsync("[...document.querySelectorAll('button')].find(b=>b.textContent==='Watch stream').click()");
            await Wait(async () => await viewer.Core.ExecuteScriptAsync("document.querySelector('video')?.getVideoPlaybackQuality().totalVideoFrames>60") == "true", token);
            Console.WriteLine("PASS: real WebView2 native broadcast+1080p playback, nickname update, fullscreen return, continuing decoded frames, stable native session, bridge disabled, off-origin navigation canceled and renderer-crash recovery without stopping publication.");
            Console.WriteLine("Runtime: " + viewer.Core.Environment.BrowserVersionString + "; captureMode=" + captureMode + "; no screen/audio recording saved. Video-only does not validate audio exclusion or A/V sync.");
        }
        finally { window.Close(); await model.StopAsync(CancellationToken.None); }
    }
    private static async Task CheckScriptFailureNotice(RoomWebView viewer, PlaybackAudioGate playbackGate, CancellationToken token)
    {
        var core = viewer.Core;
        var browser = viewer.Children.OfType<Grid>().Single().Children.OfType<WebView2>().Single();
        await core.ExecuteScriptAsync("window.jkProbeVideo=document.querySelector('video');window.jkProbeMedia=window.jkProbeVideo.srcObject;Object.defineProperty(window,'playkrossPlaybackAudioBlocked',{value:'forced-script-update-failure',configurable:false});");
        await using (var lease = await playbackGate.AcquireAsync(token))
        {
            viewer.UpdateLayout();
            var notice = viewer.Children.OfType<Border>().Single();
            var reload = ((DockPanel)notice.Child).Children.OfType<Button>().Single();
            var noticeTop = notice.TranslatePoint(new Point(), viewer).Y;
            var browserTop = browser.TranslatePoint(new Point(), viewer).Y;
            if (!notice.IsVisible || notice.ActualHeight <= 0 || !reload.IsVisible || !reload.IsEnabled || !browser.IsVisible || browser.ActualHeight <= 0 || noticeTop + notice.ActualHeight > browserTop + 0.5)
                throw new Exception("Forced script failure did not expose usable native guidance outside the still-visible WebView airspace.");
            if (!ReferenceEquals(core, viewer.Core) || !core.IsMuted || await core.ExecuteScriptAsync("document.querySelector('video')===window.jkProbeVideo && window.jkProbeVideo.srcObject===window.jkProbeMedia") != "true")
                throw new Exception("Showing script-failure guidance replaced browser/media or lost native mute.");
            // Exercise the native action, not a page command bridge. Explicit reload may
            // restart page playback but must retain this WebView/Core and native mute.
            reload.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Wait(() => Task.FromResult(notice.Visibility == Visibility.Collapsed), token);
            if (!ReferenceEquals(core, viewer.Core) || !core.IsMuted) throw new Exception("Reload action replaced WebView or lost mute protection.");
            await Wait(async () => await core.ExecuteScriptAsync("window.playkrossPlaybackAudioBlocked===true && [...document.querySelectorAll('button')].some(b=>b.textContent==='Watch stream')") == "true", token);
            await core.ExecuteScriptAsync("[...document.querySelectorAll('button')].find(b=>b.textContent==='Watch stream').click()");
            await Wait(async () => await core.ExecuteScriptAsync("document.querySelector('video')?.getVideoPlaybackQuality().totalVideoFrames>60") == "true", token);
        }
        if (core.IsMuted != playbackGate.IsBlocked) throw new Exception("Forced-failure probe released another publisher's mute state.");
        Console.WriteLine("PASS: forced script-update failure exposes nonoverlapping native guidance/reload, retains browser/video/media until explicit reload, and keeps native mute protected.");
    }
    private static async Task Wait(Func<Task<bool>> condition, CancellationToken token)
    {
        var until = DateTimeOffset.UtcNow.AddSeconds(35);
        while (!await condition()) { token.ThrowIfCancellationRequested(); if (DateTimeOffset.UtcNow >= until) throw new TimeoutException("Embedded viewer did not reach the expected state."); await Task.Delay(100, token); }
    }
    private static async Task Click(RoomWebView viewer, string label)
    {
        await Wait(async () => await viewer.Core.ExecuteScriptAsync($"[...document.querySelectorAll('button')].some(b=>b.textContent==={JsonSerializer.Serialize(label)})") == "true", CancellationToken.None);
        var json = await viewer.Core.ExecuteScriptAsync($"(()=>{{const b=[...document.querySelectorAll('button')].find(b=>b.textContent==={JsonSerializer.Serialize(label)}); b.scrollIntoView(); const r=b.getBoundingClientRect(); return {{x:r.x+r.width/2,y:r.y+r.height/2}};}})()");
        using var point = JsonDocument.Parse(json); var x = point.RootElement.GetProperty("x").GetDouble(); var y = point.RootElement.GetProperty("y").GetDouble();
        foreach (var type in new[] { "mousePressed", "mouseReleased" }) await viewer.Core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", JsonSerializer.Serialize(new { type, x, y, button = "left", clickCount = 1 }));
    }
}
