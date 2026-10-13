using System.Text.Json;
using JKiller.Contracts;
using JKiller.Windows.Obs;
using Xunit;

namespace JKiller.Windows.Tests;

public sealed class ObsMediaEngineTests
{
    private static BroadcastRequest Request => new(new("window", SourceKind.Window, 42, new(222, 333, @"C:\Game.exe"), "Game"), new(null), new(1920, 1080, 60, 15));
    [Fact]
    public async Task InvalidSettingsRejectedBeforeHelperLaunch()
    {
        var helper = new FakeHelper(); var rpc = new FakeRpc();
        var engine = new ObsMediaEngine(helper, (_, _) => Task.FromResult<IObsRpcClient>(rpc));
        await Assert.ThrowsAsync<ArgumentException>(() => engine.PrepareAsync(Request with { Settings = new(1920, 1080, 240, 15) }, CancellationToken.None));
        Assert.Empty(rpc.PreparedEncoders);
    }
    [Fact]
    public async Task RefusesNvencFailureWithoutExplicitFallback()
    {
        var rpc = new FakeRpc { FailNvenc = true }; var helper = new FakeHelper();
        var engine = new ObsMediaEngine(helper, (_, _) => Task.FromResult<IObsRpcClient>(rpc));
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.PrepareAsync(Request, CancellationToken.None));
        Assert.Equal(0, rpc.Starts); Assert.DoesNotContain("obs_x264", rpc.PreparedEncoders); Assert.Equal(1, helper.Stops);
    }
    [Fact]
    public async Task ExplicitCpuFallbackIsReported()
    {
        var rpc = new FakeRpc { FailNvenc = true };
        var engine = new ObsMediaEngine(new FakeHelper(), (_, _) => Task.FromResult<IObsRpcClient>(rpc), allowCpuFallback: true);
        var applied = await engine.PrepareAsync(Request, CancellationToken.None);
        Assert.Equal("obs_x264", applied.Encoder); Assert.False(applied.HardwareVerified);
        await engine.StopAsync(CancellationToken.None);
    }
    [Fact]
    public async Task ReadbackMustMatchRequestedSettings()
    {
        var rpc = new FakeRpc { WrongReadback = true };
        var engine = new ObsMediaEngine(new FakeHelper(), (_, _) => Task.FromResult<IObsRpcClient>(rpc));
        await Assert.ThrowsAsync<InvalidDataException>(() => engine.PrepareAsync(Request, CancellationToken.None));
        Assert.Equal(0, rpc.Starts);
    }
    [Fact]
    public async Task ConcurrentStopInvalidatesLateStart()
    {
        var rpc = new FakeRpc { DelayStart = true };
        var engine = new ObsMediaEngine(new FakeHelper(), (_, _) => Task.FromResult<IObsRpcClient>(rpc));
        await engine.PrepareAsync(Request, CancellationToken.None);
        var start = engine.StartAsync(new("http://localhost:5000/whip"), "credential", CancellationToken.None);
        await rpc.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var stop = engine.StopAsync(CancellationToken.None);
        rpc.CompleteStart.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        await stop; Assert.False(rpc.Publishing);
    }
    [Fact]
    public async Task WindowCaptureBindsHandleAndCreationIdentityInsteadOfTitle()
    {
        var rpc = new FakeRpc();
        var engine = new ObsMediaEngine(new FakeHelper(), (_, _) => Task.FromResult<IObsRpcClient>(rpc));
        var applied = await engine.PrepareAsync(Request, CancellationToken.None);
        Assert.Equal(Request.Settings, applied.Settings);
        var source = rpc.VideoSettings;
        Assert.Equal("42", source.GetProperty("native_handle").GetString());
        Assert.Equal("333", source.GetProperty("creation_filetime").GetString());
        Assert.False(source.TryGetProperty("window", out _));
        Assert.Equal("jk_bound_capture", rpc.VideoKind);
        await engine.StopAsync(CancellationToken.None);
    }
    [Fact]
    public async Task CapturedSourceFitsTheSelectedOutputWithoutCroppingOrStretching()
    {
        var rpc = new FakeRpc();
        var engine = new ObsMediaEngine(new FakeHelper(), (_, _) => Task.FromResult<IObsRpcClient>(rpc));
        await engine.PrepareAsync(Request, CancellationToken.None);
        Assert.Equal("OBS_BOUNDS_SCALE_INNER", rpc.Transform.GetProperty("boundsType").GetString());
        Assert.Equal(1920, rpc.Transform.GetProperty("boundsWidth").GetInt32()); Assert.Equal(1080, rpc.Transform.GetProperty("boundsHeight").GetInt32());
        await engine.StopAsync(CancellationToken.None);
    }

    private sealed class FakeHelper : IOwnedObsProcess
    {
        public int Stops;
        public Task<OwnedEngineHandle> StartAsync(CancellationToken cancellationToken) => Task.FromResult(new OwnedEngineHandle(new(1, 2, "obs64.exe"), "test", new(100, 200, "JKiller.exe")));
        public Task StopAsync(CancellationToken cancellationToken) { Stops++; return Task.CompletedTask; }
    }
    private sealed class FakeRpc : IObsRpcClient
    {
        public bool FailNvenc, WrongReadback, DelayStart, Publishing;
        public int Starts;
        public List<string> PreparedEncoders = [];
        public JsonElement VideoSettings;
        public JsonElement Transform = JsonSerializer.SerializeToElement(new { });
        public string? VideoKind;
        public TaskCompletionSource StartEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CompleteStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<JsonElement> RequestAsync(string requestType, JsonElement data, CancellationToken cancellationToken)
        {
            if (requestType == "CreateInput" && data.GetProperty("inputName").GetString() == "JK Video") { VideoSettings = data.GetProperty("inputSettings").Clone(); VideoKind = data.GetProperty("inputKind").GetString(); }
            if (requestType == "GetSceneItemList") return JsonSerializer.SerializeToElement(new { sceneItems = new[] { new { sourceName = "JK Video", sceneItemId = 1 } } });
            if (requestType == "SetSceneItemTransform") Transform = data.GetProperty("sceneItemTransform").Clone();
            if (requestType == "GetVideoSettings") return JsonSerializer.SerializeToElement(new { outputWidth = WrongReadback ? 1280 : 1920, outputHeight = 1080, fpsNumerator = 60, fpsDenominator = 1 });
            if (requestType != "CallVendorRequest") return JsonSerializer.SerializeToElement(new { });
            var vendor = data.GetProperty("requestType").GetString();
            var args = data.GetProperty("requestData");
            object response = new { success = true, armed = true, videoReady = true };
            if (vendor == "GetCapabilities") response = new { encoders = new[] { new { id = "obs_nvenc_h264_tex" }, new { id = "obs_x264" }, new { id = "ffmpeg_opus" } }, sources = new[] { new { id = "jk_bound_capture" } }, services = new[] { new { id = "whip_custom" } } };
            if (vendor == "PreparePipeline")
            {
                var encoder = args.GetProperty("encoder").GetString()!; PreparedEncoders.Add(encoder);
                response = new { success = !(FailNvenc && encoder.Contains("nvenc")), encoder, codec = "h264", hardwareInitialized = encoder.Contains("nvenc"), bitrateKbps = 15000, bFrames = 0, lookahead = false };
            }
            if (vendor == "StartPipeline") { Starts++; StartEntered.TrySetResult(); if (DelayStart) await CompleteStart.Task; Publishing = true; }
            if (vendor is "StopPipeline" or "Disarm") Publishing = false;
            return JsonSerializer.SerializeToElement(new { responseData = response });
        }
    }
}
