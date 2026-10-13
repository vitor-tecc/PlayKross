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
await using var model = new BroadcasterModel(fallback => new ObsMediaEngine(new OwnedObsProcess(Environment.CurrentDirectory), async (handle, token) => control = await ObsRpcClient.ConnectAsync(handle.PipeName, handle.Identity, token), fallback), new RoomHttpClient(http), new WindowsSourceCatalog());
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
using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
try
{
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
