using JKiller.Windows.Capture;
using JKiller.Windows.Obs;
using JKiller.Windows.Presentation;
using JKiller.Windows.Rooms;
using JKiller.Core.Settings;
using System.Text.Json;
using System.Diagnostics;
using JKiller.Acceptance.Probes;
var options = new Dictionary<string, string>(StringComparer.Ordinal);
var concurrent = false; var checkOptions = false;
for (var index = 0; index < args.Length; index++)
{
    if (args[index] == "--concurrent") { concurrent = true; continue; }
    if (args[index] == "--check-options") { checkOptions = true; continue; }
    if (args[index] is not ("--root" or "--artifact-dir" or "--video-source" or "--render-endpoint" or "--nickname") || index + 1 == args.Length || !options.TryAdd(args[index], args[++index])) throw new ArgumentException("Unknown, duplicate or missing probe option.");
}
string Option(string name, string fallback) => options.GetValueOrDefault(name, fallback);
var root = Option("--root", Environment.CurrentDirectory);
var directory = Option("--artifact-dir", Path.Combine(Environment.CurrentDirectory, ".artifacts/live-flow"));
if (!Path.IsPathFullyQualified(root) || !Path.IsPathFullyQualified(directory)) throw new ArgumentException("Root and artifact directory must be absolute.");
var videoSource = Option("--video-source", Environment.GetEnvironmentVariable("JKILLER_PROBE_CAPTURE_MODE") == "system-output" ? "monitor:" + Environment.GetEnvironmentVariable("JKILLER_PROBE_MONITOR_ID") : "fixture-window");
if (videoSource != "fixture-window" && (!videoSource.StartsWith("monitor:", StringComparison.Ordinal) || videoSource.Length == 8)) throw new ArgumentException("Use fixture-window or monitor:<concrete-source-id>.");
var endpoint = Option("--render-endpoint", Environment.GetEnvironmentVariable("JKILLER_PROBE_RENDER_ENDPOINT_ID") ?? "");
var captureMode = Environment.GetEnvironmentVariable("JKILLER_PROBE_CAPTURE_MODE") ?? "application";
if (captureMode is not ("application" or "video-only" or "system-output")) throw new ArgumentException("Unknown capture mode.");
if (captureMode == "system-output" && (videoSource == "fixture-window" || string.IsNullOrWhiteSpace(endpoint) || string.Equals(endpoint.Trim(), "default", StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("System output requires explicit monitor video and render endpoint.");
if (captureMode != "system-output" && (videoSource != "fixture-window" || endpoint.Length != 0)) throw new ArgumentException("Monitor/endpoint options require system-output mode.");
if (checkOptions) { Console.WriteLine("PASS: explicit probe options validated without launching media."); return; }
Directory.CreateDirectory(directory);
using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
IObsRpcClient? control = null;
OwnedEngineHandle? ownedHandle = null;
var forceCpuFallback = Environment.GetEnvironmentVariable("JKILLER_PROBE_CPU_FALLBACK") == "1";
await using var model = new BroadcasterModel(fallback => new ObsMediaEngine(new OwnedObsProcess(root), async (handle, token) => { ownedHandle = handle; control = await ObsRpcClient.ConnectAsync(handle.PipeName, handle.Identity, token); return forceCpuFallback ? new CpuFallbackProbeRpc(control) : control; }, fallback), new RoomHttpClient(http), new WindowsSourceCatalog(), audioCatalog: new WindowsAudioCatalog());
model.AllowCpuFallback = forceCpuFallback;
model.RoomLink = Environment.GetEnvironmentVariable("JKILLER_PROBE_JOIN_LINK") ?? throw new Exception("Invitation missing.");
model.Nickname = Option("--nickname", "Native flow check");
await model.JoinAsync(CancellationToken.None);
var pid = int.Parse(Environment.GetEnvironmentVariable("JKILLER_TARGET_PID") ?? throw new Exception("Explicit target PID required."));
var identity = ProcessIdentityReader.Read(pid) ?? throw new Exception("Original target unavailable.");
var image = Path.GetFileName(identity.ExecutablePath);
if (image is not ("GeometryDash.exe" or "CaptureFixture.exe")) throw new Exception("This acceptance probe only accepts Geometry Dash or the synthetic fixture.");
using var target = Process.GetProcessById(pid); target.Refresh();
model.Video = captureMode == "system-output"
    ? model.Sources.Single(value => value.Kind == JKiller.Contracts.SourceKind.Monitor && value.Id == videoSource[8..])
    : model.Sources.Single(value => value.Kind == JKiller.Contracts.SourceKind.Window && value.NativeHandle == unchecked((ulong)(long)target.MainWindowHandle));
model.Audio = ProbeCaptureMode.SelectAudio(captureMode, model.Video, endpoint);
Console.WriteLine("Probe capture mode: " + captureMode + "; audio/synchronization acceptance is pending in video-only mode.");
var sustainedSeconds = int.TryParse(Environment.GetEnvironmentVariable("JKILLER_SUSTAINED_SECONDS"), out var measuredDuration) ? Math.Clamp(measuredDuration, 10, 1800) : 0;
using var deadline = new CancellationTokenSource(sustainedSeconds > 0 ? TimeSpan.FromSeconds(sustainedSeconds + 90) : TimeSpan.FromMinutes(3));
try
{
    if (concurrent)
    {
        model.Settings = StreamSettings.DefaultFor(720, 30) with { BitrateMbps = 4 };
        await model.StartAsync(deadline.Token); await Ready(720);
        while (!File.Exists(Path.Combine(directory, "stop")))
        {
            deadline.Token.ThrowIfCancellationRequested();
            if (File.Exists(Path.Combine(directory, "stop-publication")))
            {
                File.Delete(Path.Combine(directory, "stop-publication"));
                await model.StopAsync(deadline.Token);
                await File.WriteAllTextAsync(Path.Combine(directory, "stopped"), "stopped", deadline.Token);
                await WaitForRestart();
                if (File.Exists(Path.Combine(directory, "stop"))) break;
                await model.StartAsync(deadline.Token); await Ready(720);
                await File.WriteAllTextAsync(Path.Combine(directory, "restarted"), "restarted", deadline.Token);
            }
            if (!model.CaptureLive) throw new Exception("Concurrent publication ended: " + model.Status);
            await Task.Delay(100, deadline.Token);
        }
        return;
    }
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
async Task WaitForRestart()
{
    while (!File.Exists(Path.Combine(directory, "restart-publication")) && !File.Exists(Path.Combine(directory, "stop"))) await Task.Delay(100, deadline.Token);
    File.Delete(Path.Combine(directory, "restart-publication"));
}
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
    var nativeResponse = await control.RequestAsync("CallVendorRequest", JsonSerializer.SerializeToElement(new { vendorName = "jkiller", requestType = "GetDiagnostics", requestData = new { } }), deadline.Token);
    var native = nativeResponse.GetProperty("responseData");
    var videoOutput = await control.RequestAsync("GetVideoSettings", JsonSerializer.SerializeToElement(new { }), deadline.Token);
    if (forceCpuFallback && (native.GetProperty("encoder").GetString() != "obs_x264" || native.GetProperty("hardwareInitialized").GetBoolean() || native.GetProperty("encodedHeight").GetInt32() > 720 || videoOutput.GetProperty("fpsNumerator").GetInt32() > 30 || native.GetProperty("bitrateKbps").GetInt32() > 4000)) throw new Exception("Actual software fallback exceeds its applied limits.");
    await File.WriteAllTextAsync(Path.Combine(directory, "ready.json"), JsonSerializer.Serialize(new { requestedHeight = height, height = native.GetProperty("encodedHeight").GetInt32(), model.CaptureLive, source = image, captureMode, applicationAudioRequested = model.Audio is not null, cpuFallbackInjected = forceCpuFallback, helperPid = ownedHandle!.Identity.Pid, helperCreation = ownedHandle.Identity.CreationFileTime.ToString() }), deadline.Token);
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
