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
internal static class Program
{
    [STAThread] private static int Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; int result = 1;
        app.Startup += async (_, _) => { try { await Run(); result = 0; } catch (Exception error) { Console.Error.WriteLine(error); } finally { app.Shutdown(); } };
        app.Run(); return result;
    }
    private static async Task Run()
    {
        var root = Environment.CurrentDirectory; using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var token = deadline.Token;
        using var fixture = Process.GetProcessById(int.Parse(Environment.GetEnvironmentVariable("JKILLER_FIXTURE_PID")!)); fixture.Refresh();
        var identity = ProcessIdentityReader.Read(fixture.Id) ?? throw new Exception("Fixture disappeared.");
        if (!string.Equals(Path.GetFullPath(identity.ExecutablePath), Path.GetFullPath(Path.Combine(root, ".artifacts/native/Debug/CaptureFixture.exe")), StringComparison.OrdinalIgnoreCase)) throw new Exception("Only the synthetic fixture may be used in this probe.");
        using var http = new HttpClient(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false }); var room = new RoomHttpClient(http);
        await using var model = new BroadcasterModel(fallback => new ObsMediaEngine(new OwnedObsProcess(root), fallback), room, new WindowsSourceCatalog());
        var link = Environment.GetEnvironmentVariable("JKILLER_PROBE_JOIN_LINK")!; model.RoomLink = link; model.Nickname = "Embedded viewer probe";
        await model.JoinAsync(token); model.Video = model.Sources.Single(s => s.Kind == SourceKind.Window && s.NativeHandle == unchecked((ulong)(long)fixture.MainWindowHandle)); model.Audio = model.Video;
        await model.StartAsync(token);
        using var viewer = new RoomWebView(root, room); var window = new Window { Content = viewer, Width = 1080, Height = 850, Title = "JKiller synthetic WebView validation" }; window.Show();
        try
        {
            await viewer.OpenRoomAsync(new Uri(link), token);
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
            var source = viewer.Core.Source; viewer.Core.Navigate("https://untrusted.invalid/"); await Task.Delay(300, token);
            if (new Uri(viewer.Core.Source).Host != new Uri(source).Host) throw new Exception("Untrusted navigation was permitted.");
            try { await viewer.Core.CallDevToolsProtocolMethodAsync("Page.crash", "{}").WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception e) when (e is System.Runtime.InteropServices.COMException or TimeoutException) { }
            await Wait(() => Task.FromResult(viewer.Children.OfType<System.Windows.Controls.StackPanel>().Any()), token);
            if (!model.CaptureLive) throw new Exception("Viewer failure stopped the native publication.");
            await viewer.OpenRoomAsync(new Uri(link), token);
            await Wait(async () => await viewer.Core.ExecuteScriptAsync("[...document.querySelectorAll('button')].some(b=>b.textContent==='Watch stream')") == "true", token);
            await viewer.Core.ExecuteScriptAsync("[...document.querySelectorAll('button')].find(b=>b.textContent==='Watch stream').click()");
            await Wait(async () => await viewer.Core.ExecuteScriptAsync("document.querySelector('video')?.getVideoPlaybackQuality().totalVideoFrames>60") == "true", token);
            Console.WriteLine("PASS: real WebView2 native broadcast+1080p playback, nickname update, fullscreen return, continuing decoded frames, stable native session, bridge disabled, off-origin navigation canceled and renderer-crash recovery without stopping publication.");
            Console.WriteLine("Runtime: " + viewer.Core.Environment.BrowserVersionString + "; synthetic audio source only; no screen/audio recording saved.");
        }
        finally { window.Close(); await model.StopAsync(CancellationToken.None); }
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
