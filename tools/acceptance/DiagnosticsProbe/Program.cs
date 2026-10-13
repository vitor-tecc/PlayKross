using System.Diagnostics;
using System.Text.Json;
using System.Runtime.InteropServices;
using JKiller.Contracts;
using JKiller.Windows.Capture;
using JKiller.Windows.Obs;
using JKiller.Windows.Presentation;
using JKiller.Windows.Rooms;
using JKiller.Core.Diagnostics;
using JKiller.Core.Settings;
var root = Environment.CurrentDirectory;
var fixtureId = int.Parse(Environment.GetEnvironmentVariable("JKILLER_FIXTURE_PID") ?? throw new Exception("Synthetic fixture required."));
using var fixture = Process.GetProcessById(fixtureId); fixture.Refresh();
var identity = ProcessIdentityReader.Read(fixtureId) ?? throw new Exception("Original fixture unavailable.");
if (!string.Equals(Path.GetFullPath(identity.ExecutablePath), Path.GetFullPath(Path.Combine(root, ".artifacts/native/Debug/CaptureFixture.exe")), StringComparison.OrdinalIgnoreCase)) throw new Exception("This diagnostic benchmark accepts only the synthetic fixture.");
using var http = new HttpClient(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false });
IObsRpcClient? control = null; OwnedEngineHandle? handle = null;
await using var model = new BroadcasterModel(fallback => new ObsMediaEngine(new OwnedObsProcess(root), async (owned, token) => { handle = owned; return control = await ObsRpcClient.ConnectAsync(owned.PipeName, owned.Identity, token); }, fallback), new RoomHttpClient(http), new WindowsSourceCatalog());
model.RoomLink = Environment.GetEnvironmentVariable("JKILLER_PROBE_JOIN_LINK") ?? throw new Exception("Invitation required."); model.Nickname = "Diagnostic calibration";
await model.JoinAsync(CancellationToken.None);
model.Video = model.Sources.Single(value => value.NativeHandle == unchecked((ulong)(long)fixture.MainWindowHandle) && value.Kind == SourceKind.Window); model.Audio = model.Video;
model.Settings = StreamSettings.DefaultFor(1080, 60);
using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
await model.StartAsync(deadline.Token);
try
{
    if (Native.GetPropW(fixture.MainWindowHandle, "JKFixturePaintCount") == 0) throw new Exception("Actual fixture paint counter is unavailable; timer requests are not measured capture FPS.");
    var staticFixture = Environment.GetEnvironmentVariable("JKILLER_FIXTURE_STATIC") == "1";
    if (staticFixture) Native.SendMessageW(fixture.MainWindowHandle, 0x8003, 0, 0);
    await Task.Delay(5000, deadline.Token);
    var measurements = new List<object>(); var gpu = new List<string>();
    using var publisher = Process.GetProcessById(handle!.Identity.Pid); var cpuStart = publisher.TotalProcessorTime;
    var started = Stopwatch.GetTimestamp(); var previousTime = started; var previous = await Diagnostics();
    if (staticFixture && (!previous.TraceEnabled || !previous.AcquisitionHookAvailable || previous.CaptureFrames is not > 0)) throw new Exception("Static acquisition validation requires the real capture hook.");
    var previousPaint = (long)Native.GetPropW(fixture.MainWindowHandle, "JKFixturePaintCount");
    for (var second = 0; second < (staticFixture ? 5 : 20); second++)
    {
        await Task.Delay(1000, deadline.Token);
        var current = await Diagnostics(); var now = Stopwatch.GetTimestamp(); var interval = Stopwatch.GetElapsedTime(previousTime, now);
        var paints = (long)Native.GetPropW(fixture.MainWindowHandle, "JKFixturePaintCount");
        if (staticFixture && (paints != previousPaint || current.CaptureFrames != previous.CaptureFrames || current.ActualEncodedFrames <= previous.ActualEncodedFrames)) throw new Exception($"Static observation: paints delta={paints-previousPaint}, WGC acquisitions delta={current.CaptureFrames-previous.CaptureFrames}, encoded delta={current.ActualEncodedFrames-previous.ActualEncodedFrames}; acquisition is not output FPS.");
        measurements.Add(new { seconds = Stopwatch.GetElapsedTime(started, now).TotalSeconds,
            fixturePaintFps = CounterRates.Calculate(previousPaint, paints, interval),
            captureFps = previous.CaptureFrames is { } before && current.CaptureFrames is { } after ? CounterRates.Calculate(before, after, interval) : null,
            compositorFps = previous.CompositorFrames is { } beforeRender && current.CompositorFrames is { } afterRender ? CounterRates.Calculate(beforeRender, afterRender, interval) : null,
            encodedFps = CounterRates.Calculate(previous.ActualEncodedFrames, current.ActualEncodedFrames, interval),
            sentFps = CounterRates.Calculate(previous.SentFrames, current.SentFrames, interval),
            mediaMbps = CounterRates.Calculate(previous.SentMediaBytes, current.SentMediaBytes, interval) * 8 / 1_000_000,
            current.NativeRenderAverageMs, current.RenderLagFrames, current.EncodeApiCallMs, current.InputToOutputPacketMs, current.CaptureToRenderMs, current.FreshCaptureToPacketMs, current.TimingSamples, current.CaptureTimingSamples, current.UnmatchedPackets });
        previous = current; previousTime = now; previousPaint = paints;
        if (second % 5 == 0) gpu.Add(await Gpu());
    }
    var elapsed = Stopwatch.GetElapsedTime(started).TotalSeconds; publisher.Refresh();
    var result = new { type = "native-hook-calibration", topology = "local-loopback-publisher-without-viewer", settings = model.Settings,
        mode = previous.TraceEnabled ? "instrumented" : "baseline-disabled-trace", staticFixture, acquisitionHook = previous.AcquisitionHookAvailable,
        hardwareInitialized = previous.HardwareInitialized, encoder = previous.Encoder, gpuActivityProvenance = "nvidia-smi aggregate device encoder utilization; not per-process attribution", gpu,
        elapsedSeconds = elapsed, normalizedPublisherCpuPercent = (publisher.TotalProcessorTime - cpuStart).TotalSeconds / elapsed / Environment.ProcessorCount * 100,
        renderingMetric = "OBS rolling average CPU render time, not per-frame p95 or GPU execution", measurements };
    var directory = Path.Combine(root, ".artifacts/diagnostics"); Directory.CreateDirectory(directory);
    var mode = staticFixture ? "static" : previous.TraceEnabled ? "instrumented" : "baseline";
    await File.WriteAllTextAsync(Path.Combine(directory, mode + ".json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), deadline.Token);
    Console.WriteLine($"PASS: {mode}, {elapsed:0.0}s actual native counters, normalized publisher CPU {((publisher.TotalProcessorTime-cpuStart).TotalSeconds/elapsed/Environment.ProcessorCount*100):0.00}%; GPU samples: {string.Join(";", gpu)}");
}
finally { await model.StopAsync(CancellationToken.None); }
async Task<MediaDiagnostics> Diagnostics()
{
    if (!model.CaptureLive) throw new Exception("Native publication stopped during calibration.");
    var response = await control!.RequestAsync("CallVendorRequest", JsonSerializer.SerializeToElement(new { vendorName = "jkiller", requestType = "GetDiagnostics", requestData = new { } }), deadline.Token);
    return response.GetProperty("responseData").Deserialize<MediaDiagnostics>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new Exception("Invalid diagnostics response.");
}
async Task<string> Gpu()
{
    var launch = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe")) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
    launch.ArgumentList.Add("--query-gpu=name,driver_version,utilization.encoder"); launch.ArgumentList.Add("--format=csv,noheader,nounits");
    using var query = Process.Start(launch) ?? throw new Exception("GPU query could not start.");
    try { var output = await query.StandardOutput.ReadToEndAsync(deadline.Token); await query.WaitForExitAsync(deadline.Token); return query.ExitCode == 0 ? output.Trim() : "unavailable"; }
    finally { if (!query.HasExited) { query.Kill(); await query.WaitForExitAsync(); } }
}
internal static class Native {
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetPropW(nint window, string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint SendMessageW(nint window, uint message, nuint wparam, nint lparam);
}
