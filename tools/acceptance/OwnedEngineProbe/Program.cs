using System.Text.Json;
using JKiller.Windows.Obs;
using JKiller.Windows.Capture;
using JKiller.Contracts;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Net.Http.Json;
using System.Net.Http.Headers;

await using var helper = new OwnedObsProcess(Environment.CurrentDirectory);
try
{
    var handle = await helper.StartAsync(CancellationToken.None);
    await using var rpc = await ObsRpcClient.ConnectAsync(handle.PipeName, handle.Identity, CancellationToken.None);
    var version = await rpc.RequestAsync("GetVersion", JsonSerializer.SerializeToElement(new { }));
    Console.WriteLine("Actual isolated OBS: " + version.GetProperty("obsVersion").GetString());
    async Task<JsonElement> Vendor(string type, object data) => (await rpc.RequestAsync("CallVendorRequest", JsonSerializer.SerializeToElement(new { vendorName = "jkiller", requestType = type, requestData = data }))).GetProperty("responseData").Clone();
    var caps = await Vendor("GetCapabilities", new { });
    Console.WriteLine("Actual H264 encoders: " + string.Join(",", caps.GetProperty("encoders").EnumerateArray().Where(x => x.GetProperty("codec").GetString() == "h264").Select(x => x.GetProperty("id").GetString())));
    if (!caps.GetProperty("sources").EnumerateArray().Any(x => x.GetProperty("id").GetString() == "jk_bound_capture")) throw new Exception("Exact-bound native source absent.");
    var applied = await Vendor("PreparePipeline", new { encoder = "obs_nvenc_h264_tex", bitrateKbps = 15000, generation = 1 });
    Console.WriteLine("Actual pipeline initialization: " + applied);
    if (!applied.GetProperty("success").GetBoolean() || !applied.GetProperty("hardwareInitialized").GetBoolean()) throw new Exception("NVENC did not initialize.");
    await Vendor("StopPipeline", new { });
    if (args.Length > 0 && args[0] == "publish")
    {
        await rpc.DisposeAsync(); await helper.StopAsync(CancellationToken.None);
        var link = new Uri(Environment.GetEnvironmentVariable("JKILLER_PROBE_JOIN_LINK") ?? throw new Exception("Probe invitation missing."));
        var invitation = Uri.UnescapeDataString(link.Fragment["#invite=".Length..]);
        using var http = new HttpClient(new HttpClientHandler { UseCookies = false }) { BaseAddress = new Uri(link.GetLeftPart(UriPartial.Authority)) };
        var joinResponse = await http.PostAsJsonAsync("/api/join", new { invite = invitation, nickname = "Synthetic publisher", desktop = true }); joinResponse.EnsureSuccessStatusCode();
        var joined = (await joinResponse.Content.ReadFromJsonAsync<SessionGrant>())!;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", joined.SessionToken);
        var reservedResponse = await http.PostAsync("/api/streams", null); reservedResponse.EnsureSuccessStatusCode();
        var reservation = (await reservedResponse.Content.ReadFromJsonAsync<PublishReservation>())!;
        var fixtureId = int.Parse(Environment.GetEnvironmentVariable("JKILLER_FIXTURE_PID")!);
        using var fixture = Process.GetProcessById(fixtureId); fixture.Refresh();
        var identity = ProcessIdentityReader.Read(fixtureId)!;
        if (!string.Equals(Path.GetFullPath(identity.ExecutablePath), Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".artifacts/native/Debug/CaptureFixture.exe")), StringComparison.OrdinalIgnoreCase)) throw new Exception("Protocol probe accepts only the synthetic fixture.");
        var video = new SourceDescriptor("fixture", SourceKind.Window, unchecked((ulong)(long)fixture.MainWindowHandle), identity, "Synthetic fixture");
        IObsRpcClient? publisherControl = null;
        await using var engine = new ObsMediaEngine(helper, async (owned, cancellation) => publisherControl = await ObsRpcClient.ConnectAsync(owned.PipeName, owned.Identity, cancellation));
        try
        {
            var requested = new BroadcastSettings(1280, 720, 30, 6);
            Console.WriteLine("Actual native applied settings: " + await engine.PrepareAsync(new(video, new(identity), requested), CancellationToken.None));
            await engine.StartAsync(new Uri(http.BaseAddress!, reservation.WhipUri), reservation.Token, CancellationToken.None);
            var directory = Path.Combine(Environment.CurrentDirectory, ".artifacts/protocol"); Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "publisher-ready.local.json"), JsonSerializer.Serialize(new { streamId = reservation.StreamId }));
            Console.WriteLine("Native publication requested; real browser negotiation is the next check.");
            var deadline = DateTimeOffset.UtcNow.AddSeconds(90);
            bool revoked = false;
            while (DateTimeOffset.UtcNow < deadline && !File.Exists(Path.Combine(directory, "stop-publisher")))
            {
                if (!revoked && File.Exists(Path.Combine(directory, "revoke-publication")))
                {
                    async Task<JsonElement> PipelineState() => (await publisherControl!.RequestAsync("CallVendorRequest", JsonSerializer.SerializeToElement(new { vendorName = "jkiller", requestType = "GetPipelineState", requestData = new { } }), CancellationToken.None)).GetProperty("responseData").Clone();
                    if (!(await PipelineState()).GetProperty("outputActive").GetBoolean()) throw new Exception("Native output was not active before the revocation check.");
                    var stopped = await http.DeleteAsync($"/api/streams/{reservation.StreamId}"); stopped.EnsureSuccessStatusCode();
                    revoked = true; Console.WriteLine("Publication ownership revoked through the host; native capture remains present for disconnection validation.");
                    var disconnectDeadline = DateTimeOffset.UtcNow.AddSeconds(5);
                    while ((await PipelineState()).GetProperty("outputActive").GetBoolean())
                    {
                        if (DateTimeOffset.UtcNow >= disconnectDeadline) throw new Exception("Router revocation did not stop the active native output.");
                        await Task.Delay(100);
                    }
                    Console.WriteLine("PASS: real router revocation stopped the active native output before engine Stop.");
                }
                await Task.Delay(250);
            }
        }
        finally { await engine.StopAsync(CancellationToken.None); await http.DeleteAsync($"/api/streams/{reservation.StreamId}"); }
        return;
    }
    if (args.Length > 0 && args[0] == "capture")
    {
        await rpc.DisposeAsync(); await helper.StopAsync(CancellationToken.None);
        using var fixture = Process.Start(new ProcessStartInfo(Path.Combine(Environment.CurrentDirectory, ".artifacts/native/Debug/CaptureFixture.exe")) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden })!;
        try
        {
            await Task.Delay(800);
            var identity = ProcessIdentityReader.Read(fixture.Id)!;
            fixture.Refresh(); var window = unchecked((ulong)(long)fixture.MainWindowHandle);
            await using var engine = new ObsMediaEngine(helper);
            var lost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            engine.SafetyFault += (_, _) => lost.TrySetResult();
            var appliedCapture = await engine.PrepareAsync(new BroadcastRequest(new("fixture", SourceKind.Window, window, identity, "Synthetic fixture"), new(null), new(1280, 720, 30, 6)), CancellationToken.None);
            Console.WriteLine("PASS: actual WGC source prepared: " + appliedCapture);
            Native.SetWindowText(fixture.MainWindowHandle, "Renamed synthetic fixture");
            await Task.Delay(1000);
            if (lost.Task.IsCompleted) throw new Exception("Title change invalidated exact-bound capture.");
            Console.WriteLine("PASS: title change preserves the original window binding.");
            Native.PostMessage(fixture.MainWindowHandle, 0x8002, 0, 0);
            await lost.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Console.WriteLine("PASS: same-process exact-title replacement invalidates original capture.");
            await engine.StopAsync(CancellationToken.None);
            await AssertStopped(engine);
        }
        finally { if (!fixture.HasExited) fixture.Kill(); }
    }
    Console.WriteLine("PASS: owned helper launch, capabilities, NVENC/Opus initialization, cleanup.");
}
finally { await helper.StopAsync(CancellationToken.None); }
static async Task AssertStopped(ObsMediaEngine engine)
{
    try { await engine.StartAsync(new("http://localhost:5000/whip"), "test", CancellationToken.None); throw new Exception("Lost source could start publication."); }
    catch (InvalidOperationException) { Console.WriteLine("PASS: original fixture exit invalidates prepared publication."); }
}
internal static class Native
{
    [DllImport("user32.dll", EntryPoint = "SetWindowTextW", CharSet = CharSet.Unicode)]
    internal static extern bool SetWindowText(nint handle, string title);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    internal static extern bool PostMessage(nint handle, uint message, nuint wparam, nint lparam);
}
