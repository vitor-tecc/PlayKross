using JKiller.Windows.Capture;
using JKiller.Windows.Obs;
using JKiller.Windows.Presentation;
using JKiller.Windows.Rooms;
using JKiller.Core.Settings;
using System.Text.Json;
using System.Diagnostics;
var directory = Path.Combine(Environment.CurrentDirectory, ".artifacts/live-flow"); Directory.CreateDirectory(directory);
using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
IObsRpcClient? control = null;
OwnedEngineHandle? ownedHandle = null;
await using var model = new BroadcasterModel(fallback => new ObsMediaEngine(new OwnedObsProcess(Environment.CurrentDirectory), async (handle, token) => { ownedHandle = handle; return control = await ObsRpcClient.ConnectAsync(handle.PipeName, handle.Identity, token); }, fallback), new RoomHttpClient(http), new WindowsSourceCatalog());
model.RoomLink = Environment.GetEnvironmentVariable("JKILLER_PROBE_JOIN_LINK") ?? throw new Exception("Invitation missing.");
model.Nickname = "Native flow check";
await model.JoinAsync(CancellationToken.None);
var pid = int.Parse(Environment.GetEnvironmentVariable("JKILLER_TARGET_PID") ?? throw new Exception("Explicit target PID required."));
var identity = ProcessIdentityReader.Read(pid) ?? throw new Exception("Original target unavailable.");
var image = Path.GetFileName(identity.ExecutablePath);
if (image is not ("GeometryDash.exe" or "CaptureFixture.exe")) throw new Exception("This acceptance probe only accepts Geometry Dash or the synthetic fixture.");
using var target = Process.GetProcessById(pid); target.Refresh();
model.Video = model.Sources.Single(value => value.Kind == JKiller.Contracts.SourceKind.Window && value.NativeHandle == unchecked((ulong)(long)target.MainWindowHandle));
model.Audio = model.Video;
var sustainedSeconds = int.TryParse(Environment.GetEnvironmentVariable("JKILLER_SUSTAINED_SECONDS"), out var measuredDuration) ? Math.Clamp(measuredDuration, 10, 1800) : 0;
using var deadline = new CancellationTokenSource(sustainedSeconds > 0 ? TimeSpan.FromSeconds(sustainedSeconds + 90) : TimeSpan.FromMinutes(3));
try
{
    if (sustainedSeconds > 0)
    {
        if (image != "CaptureFixture.exe") throw new Exception("Sustained timing accepts only the synthetic source.");
        model.Settings = StreamSettings.DefaultFor(1080, 60); await model.StartAsync(deadline.Token);
        var baseline = await control!.RequestAsync("CallVendorRequest", JsonSerializer.SerializeToElement(new { vendorName = "jkiller", requestType = "GetDiagnostics", requestData = new { } }), deadline.Token);
        if (!baseline.GetProperty("responseData").TryGetProperty("encodingSkippedFrames", out _)) throw new Exception("Measured encoding skip counters are missing.");
        if (!baseline.GetProperty("responseData").TryGetProperty("captureMinIntervalMs", out var captureInterval) || captureInterval.GetDouble() > 4) throw new Exception("Native capture still uses the measured 16ms throttle instead of the verified 4ms request.");
        await Ready(1080); var samples = new List<object>(); var started = Stopwatch.GetTimestamp();
        using var helper = Process.GetProcessById(ownedHandle!.Identity.Pid); var previousCpu = helper.TotalProcessorTime; var previousAt = started;
        while (!File.Exists(Path.Combine(directory, "stop")))
        {
            deadline.Token.ThrowIfCancellationRequested(); if (!model.CaptureLive) throw new Exception("Sustained native publication ended: " + model.Status);
            var response = await control.RequestAsync("CallVendorRequest", JsonSerializer.SerializeToElement(new { vendorName = "jkiller", requestType = "GetDiagnostics", requestData = new { } }), deadline.Token);
            var now = Stopwatch.GetTimestamp(); helper.Refresh(); var cpu = helper.TotalProcessorTime;
            string? gpu = null;
            if (samples.Count % 10 == 0 && File.Exists(@"C:\Windows\System32\nvidia-smi.exe"))
            {
                var launch = new ProcessStartInfo(@"C:\Windows\System32\nvidia-smi.exe", "--query-gpu=name,driver_version,utilization.gpu,utilization.encoder --format=csv,noheader,nounits") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
                using var query = Process.Start(launch)!;
                try { await query.WaitForExitAsync(deadline.Token).WaitAsync(TimeSpan.FromSeconds(3), deadline.Token); if (query.ExitCode == 0) gpu = (await query.StandardOutput.ReadToEndAsync(deadline.Token)).Trim(); }
                catch (TimeoutException) { if (!query.HasExited) query.Kill(); }
            }
            samples.Add(new { seconds = Stopwatch.GetElapsedTime(started).TotalSeconds, native = response.GetProperty("responseData").Clone(), helperCpuPercentOfMachine = (cpu - previousCpu).TotalSeconds / Stopwatch.GetElapsedTime(previousAt, now).TotalSeconds / Environment.ProcessorCount * 100,
                fixturePaintCount = FixtureNative.GetPropW(target.MainWindowHandle,"JKFixturePaintCount").ToInt64(), deviceAggregateGpu = gpu });
            previousCpu = cpu; previousAt = now;
            await File.WriteAllTextAsync(Path.Combine(directory, "sustained-native.json"), JsonSerializer.Serialize(samples), deadline.Token);
            await Task.Delay(1000, deadline.Token);
        }
        return;
    }
    model.Settings = StreamSettings.DefaultFor(720, 30) with { BitrateMbps = 6 };
    await model.StartAsync(deadline.Token); await Ready(720);
    await WaitFor("next-preset");
    model.Settings = StreamSettings.DefaultFor(1080, 60);
    await model.ApplySettingsAsync(deadline.Token); await Ready(1080);
    await WaitFor("stop");
}
finally { await model.StopAsync(CancellationToken.None); }
async Task Ready(int height)
{
    if (Environment.GetEnvironmentVariable("JKILLER_EXPECT_TRACE_DISABLED") == "1")
    {
        var disabledResponse = await control!.RequestAsync("CallVendorRequest", JsonSerializer.SerializeToElement(new { vendorName = "jkiller", requestType = "GetDiagnostics", requestData = new { } }), deadline.Token);
        var disabled = disabledResponse.GetProperty("responseData");
        if (disabled.GetProperty("traceEnabled").GetBoolean() || disabled.TryGetProperty("compositorFrames", out _)) throw new Exception("Disabled tracing must report unavailable trace observations, not fake zeros.");
    }
    if (Environment.GetEnvironmentVariable("JKILLER_REQUIRE_DIAGNOSTICS") == "1")
    {
        var diagnosticResponse = await control!.RequestAsync("CallVendorRequest", JsonSerializer.SerializeToElement(new { vendorName = "jkiller", requestType = "GetDiagnostics", requestData = new { } }), deadline.Token);
        var diagnostics = diagnosticResponse.GetProperty("responseData");
        if (!diagnostics.GetProperty("acquisitionHookAvailable").GetBoolean() || diagnostics.GetProperty("captureFrames").GetInt64() <= 0) throw new Exception("Actual capture acquisition hook is unavailable.");
        Console.WriteLine("Actual native diagnostics: " + diagnostics);
    }
    var response = await control!.RequestAsync("CallVendorRequest", JsonSerializer.SerializeToElement(new { vendorName = "jkiller", requestType = "GetSafetyStatus", requestData = new { } }), deadline.Token);
    Console.WriteLine($"Live flow {height}p: {model.AppliedText}; safety={response.GetProperty("responseData")}");
    await File.WriteAllTextAsync(Path.Combine(directory, "ready.json"), JsonSerializer.Serialize(new { height, model.CaptureLive, source = image }), deadline.Token);
}
async Task WaitFor(string name)
{
    while (!File.Exists(Path.Combine(directory, name)))
    {
        deadline.Token.ThrowIfCancellationRequested();
        if (!model.CaptureLive) throw new Exception("The selected source or publication disconnected during the smoke check: " + model.Status);
        await Task.Delay(100, deadline.Token);
    }
}
