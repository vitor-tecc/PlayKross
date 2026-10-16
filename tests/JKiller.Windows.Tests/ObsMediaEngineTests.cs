using System.Text.Json;
using JKiller.Contracts;
using JKiller.Windows.Obs;
using Xunit;

namespace JKiller.Windows.Tests;

public sealed class ObsMediaEngineTests
{
    [Fact]
    public async Task OffCreatesNoAudioSource()
    {
        var rpc = new FakeRpc();
        await using var engine = new ObsMediaEngine(new FakeHelper(), (_, _) => Task.FromResult<IObsRpcClient>(rpc));
        await engine.PrepareAsync(Request, CancellationToken.None);
        Assert.Null(rpc.AudioKind);
        Assert.Equal("Off", rpc.Arm.GetProperty("audioMode").GetString());
        Assert.Equal("", rpc.Arm.GetProperty("audioSource").GetString());
    }
    [Fact]
    public async Task SystemOutputSendsConcreteEndpointAndMode()
    {
        var rpc = new FakeRpc();
        await using var engine = new ObsMediaEngine(new FakeHelper(), (_, _) => Task.FromResult<IObsRpcClient>(rpc));
        await engine.PrepareAsync(Request with { Video = new("display", SourceKind.Monitor, 8, null, "Display"), Audio = new(AudioMode.SystemOutput, RenderEndpointId: "render:選択") }, CancellationToken.None);
        Assert.Equal("jk_system_audio", rpc.AudioKind);
        Assert.Equal("render:選択", rpc.AudioSettings.GetProperty("endpoint_id").GetString());
        Assert.Equal("SystemOutput", rpc.AudioSettings.GetProperty("audio_mode").GetString());
        Assert.True(rpc.AudioSettings.GetProperty("selection_generation").GetInt64() > 0);
        Assert.Equal("SystemOutput", rpc.Arm.GetProperty("audioMode").GetString());
        Assert.Equal("endpoint:render:選択", rpc.Arm.GetProperty("audioIdentity").GetString());
        Assert.False(rpc.AudioSettings.TryGetProperty("pid", out _));
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingCapabilityOrCaptureReadinessFailsStart(bool capabilityMissing)
    {
        var rpc = new FakeRpc { MissingAudioCapability = capabilityMissing, AudioReady = false };
        var helper = new FakeHelper();
        await using var engine = new ObsMediaEngine(helper, (_, _) => Task.FromResult<IObsRpcClient>(rpc));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.PrepareAsync(Request with { Audio = new(AudioMode.Application, Request.Video.Process) }, CancellationToken.None));
        Assert.Contains("audio", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, rpc.Starts); Assert.Equal(1, helper.Stops);
    }
    [Fact]
    public async Task QuietInitializedSourceCanStart()
    {
        var rpc = new FakeRpc();
        await using var engine = new ObsMediaEngine(new FakeHelper(), (_, _) => Task.FromResult<IObsRpcClient>(rpc));
        await engine.PrepareAsync(Request with { Audio = new(AudioMode.Application, Request.Video.Process) }, CancellationToken.None);
        await engine.StartAsync(new("http://localhost:5000/whip"), "credential", CancellationToken.None);
        Assert.True(rpc.Publishing);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopOrCancellationCleansPreparedAudio(bool cancel)
    {
        var rpc = new FakeRpc { CancelOnArm = cancel }; var helper = new FakeHelper();
        await using var engine = new ObsMediaEngine(helper, (_, _) => Task.FromResult<IObsRpcClient>(rpc));
        using var cancellation = new CancellationTokenSource(); rpc.Cancellation = cancellation;
        var prepare = engine.PrepareAsync(Request with { Audio = new(AudioMode.Application, Request.Video.Process) }, cancellation.Token);
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => prepare);
        else { await prepare; await engine.StopAsync(CancellationToken.None); }
        Assert.Equal(1, helper.Stops); Assert.False(rpc.Publishing); Assert.True(rpc.Disarmed);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task H264WithoutRepeatedSetupHeadersCannotBePublished(bool cpuFallback)
    {
        var rpc = new FakeRpc { FailNvenc = cpuFallback, RepeatHeaders = false };
        await using var engine = new ObsMediaEngine(new FakeHelper(), (_, _) => Task.FromResult<IObsRpcClient>(rpc), allowCpuFallback: cpuFallback);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => engine.PrepareAsync(Request, CancellationToken.None));
        Assert.Contains("header", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(rpc.Publishing);
    }
    [Fact]
    public async Task DirectEngineAudioRequestCannotBypassExcludedApplicationPolicy()
    {
        var helper = new FakeHelper(); var rpc = new FakeRpc();
        await using var engine = new ObsMediaEngine(helper, (_, _) => Task.FromResult<IObsRpcClient>(rpc));
        var discord = Request.Video.Process! with { ExecutablePath = @"C:\Apps\Discord.exe" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.PrepareAsync(Request with { Audio = new(AudioMode.Application, discord) }, CancellationToken.None));
        Assert.Equal(0, helper.Starts);
    }
    [Fact]
    public async Task ApplicationSendsExactIdentity()
    {
        var rpc = new FakeRpc();
        await using var engine = new ObsMediaEngine(new FakeHelper(), (_, _) => Task.FromResult<IObsRpcClient>(rpc));
        var process = new ProcessIdentity(222, 333, @"C:\Steam\steamapps\common\Geometry Dash\GeometryDash.exe");
        await engine.PrepareAsync(Request with { Audio = new(AudioMode.Application, process) }, CancellationToken.None);
        Assert.Equal("jk_process_audio", rpc.AudioKind);
        Assert.Equal(222, rpc.AudioSettings.GetProperty("pid").GetInt32());
        Assert.Equal("333", rpc.AudioSettings.GetProperty("creation_filetime").GetString());
        Assert.Equal(process.ExecutablePath, rpc.AudioSettings.GetProperty("image_path").GetString());
        Assert.Equal("Application", rpc.Arm.GetProperty("audioMode").GetString());
        Assert.Equal($"process:{process.Pid}:{process.CreationFileTime}:{process.ExecutablePath}", rpc.Arm.GetProperty("audioIdentity").GetString());
    }
    [Fact]
    public async Task NativeReadinessLossAfterArmFailsEvenWhenArmWasAccepted()
    {
        var rpc = new FakeRpc { LoseAudioAfterArm = true };
        await using var engine = new ObsMediaEngine(new FakeHelper(), (_, _) => Task.FromResult<IObsRpcClient>(rpc));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.PrepareAsync(Request with { Audio = new(AudioMode.Application, Request.Video.Process) }, CancellationToken.None));
        Assert.Contains("Audio device initialization failed.", error.Message);
        Assert.True(rpc.Disarmed); Assert.Equal(0, rpc.Starts);
    }
    [Fact]
    public async Task AudioBytesAloneDoNotReportVideoBroadcastReady()
    {
        var rpc = new FakeRpc { VideoReady = false };
        await using var engine = new ObsMediaEngine(new FakeHelper(), (_, _) => Task.FromResult<IObsRpcClient>(rpc));
        await engine.PrepareAsync(Request, CancellationToken.None);
        var start = engine.StartAsync(new("http://localhost:5000/whip"), "credential", CancellationToken.None);
        await rpc.PipelinePolled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(start.IsCompleted); rpc.VideoReady = true;
        await start.WaitAsync(TimeSpan.FromSeconds(2));
    }
    [Fact]
    public async Task AcceptedStartWithFailedHandshakeIsNotReportedAsBroadcasting()
    {
        var rpc = new FakeRpc { HandshakeFailed = true };
        await using var engine = new ObsMediaEngine(new FakeHelper(), (_, _) => Task.FromResult<IObsRpcClient>(rpc));
        await engine.PrepareAsync(Request, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.StartAsync(new("http://localhost:5000/whip"), "credential", CancellationToken.None));
        Assert.False(rpc.Publishing);
    }
    [Fact]
    public async Task OutputLossAfterConnectionRaisesSafetyFault()
    {
        var rpc = new FakeRpc();
        await using var engine = new ObsMediaEngine(new FakeHelper(), (_, _) => Task.FromResult<IObsRpcClient>(rpc));
        var fault = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.SafetyFault += (_, _) => fault.TrySetResult();
        await engine.PrepareAsync(Request, CancellationToken.None);
        await engine.StartAsync(new("http://localhost:5000/whip"), "credential", CancellationToken.None);
        rpc.Publishing = false;
        await fault.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }
    [Theory]
    [InlineData("heartbeat_timeout", "The native capture heartbeat timed out.")]
    [InlineData("video_source_lost", "The selected screen or window became unavailable.")]
    [InlineData("untrusted/private-detail", "Native source or safety guard was lost.")]
    public async Task NativeStopCauseIsActionableWithoutReflectingUnknownDetails(string reason, string expected)
    {
        var rpc = new FakeRpc();
        await using var engine = new ObsMediaEngine(new FakeHelper(), (_, _) => Task.FromResult<IObsRpcClient>(rpc));
        var fault = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.SafetyFault += (_, error) => fault.TrySetResult(error);
        await engine.PrepareAsync(Request, CancellationToken.None);
        await engine.StartAsync(new("http://localhost:5000/whip"), "credential", CancellationToken.None);
        rpc.UnarmedReason = reason;
        Assert.Equal(expected, (await fault.Task.WaitAsync(TimeSpan.FromSeconds(2))).Message);
    }
    private static BroadcastRequest Request => new(new("window", SourceKind.Window, 42, new(222, 333, @"C:\Game.exe"), "Game"), AudioSelection.Off, new(1920, 1080, 60, 15));
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
        Assert.Equal(new BroadcastSettings(1280, 720, 30, 4), applied.Settings);
        Assert.Equal(1280, rpc.Transform.GetProperty("boundsWidth").GetInt32());
        Assert.Equal(720, rpc.Transform.GetProperty("boundsHeight").GetInt32());
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

    [Fact]
    public async Task SystemPrepareAwaitsMuteBeforeCapture()
    {
        var events = new List<string>(); var playback = new JKiller.Windows.Viewing.PlaybackAudioGate();
        var muted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var continueMute = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var viewer = playback.Register(async (blocked, _) => { if (blocked) { muted.TrySetResult(); await continueMute.Task; } events.Add(blocked ? "mute" : "unmute"); });
        var helper = new FakeHelper { Events = events }; var rpc = new FakeRpc { Events = events };
        await using var engine = new ObsMediaEngine(helper, (_, _) => Task.FromResult<IObsRpcClient>(rpc), playbackAudioGate: playback);
        var preparing = engine.PrepareAsync(SystemRequest, CancellationToken.None);
        await muted.Task.WaitAsync(TimeSpan.FromSeconds(2)); Assert.Equal(0, helper.Starts); Assert.Null(rpc.AudioKind);
        continueMute.SetResult(); await preparing; await engine.StopAsync(CancellationToken.None);
        Assert.True(events.IndexOf("mute") < events.IndexOf("helper start"));
        Assert.True(events.IndexOf("helper start") < events.IndexOf("capture"));
        Assert.True(events.IndexOf("helper stop") < events.LastIndexOf("unmute"));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task FailureAndStopReleaseMuteOnlyAfterHelperStops(bool failedStart)
    {
        var playback = new JKiller.Windows.Viewing.PlaybackAudioGate(); var events = new List<string>();
        using var viewer = playback.Register((blocked, _) => { events.Add(blocked ? "mute" : "unmute"); return Task.CompletedTask; });
        var helper = new FakeHelper { Events = events }; var rpc = new FakeRpc { HandshakeFailed = failedStart };
        await using var engine = new ObsMediaEngine(helper, (_, _) => Task.FromResult<IObsRpcClient>(rpc), playbackAudioGate: playback);
        await engine.PrepareAsync(SystemRequest, CancellationToken.None); Assert.True(playback.IsBlocked);
        if (failedStart) await Assert.ThrowsAsync<InvalidOperationException>(() => engine.StartAsync(new("http://localhost/whip"), "credential", CancellationToken.None));
        else await engine.StopAsync(CancellationToken.None);
        Assert.False(playback.IsBlocked); Assert.True(events.IndexOf("helper stop") < events.LastIndexOf("unmute"));
        // A new preparation must receive a fresh lease; duplicate cleanup cannot release it.
        rpc.HandshakeFailed = false; await engine.PrepareAsync(SystemRequest, CancellationToken.None); Assert.True(playback.IsBlocked);
    }
    [Fact]
    public async Task CancelledCleanupStillStopsHelper()
    {
        var playback = new JKiller.Windows.Viewing.PlaybackAudioGate(); var helper = new FakeHelper(); var rpc = new FakeRpc { CancelOnArm = true };
        using var cancellation = new CancellationTokenSource(); rpc.Cancellation = cancellation;
        await using var engine = new ObsMediaEngine(helper, (_, _) => Task.FromResult<IObsRpcClient>(rpc), playbackAudioGate: playback);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.PrepareAsync(SystemRequest, cancellation.Token));
        Assert.Equal(1, helper.Stops); Assert.False(helper.StopTokenCancelled); Assert.False(playback.IsBlocked);
    }
    [Fact]
    public async Task RpcDisposeFailureCannotSkipHelperStop()
    {
        var playback = new JKiller.Windows.Viewing.PlaybackAudioGate(); var helper = new FakeHelper(); var rpc = new FakeRpc { FailDispose = true };
        await using var engine = new ObsMediaEngine(helper, (_, _) => Task.FromResult<IObsRpcClient>(rpc), playbackAudioGate: playback);
        await engine.PrepareAsync(SystemRequest, CancellationToken.None);
        await Assert.ThrowsAsync<IOException>(() => engine.StopAsync(CancellationToken.None));
        Assert.Equal(1, helper.Stops); Assert.False(playback.IsBlocked);
    }
    [Fact]
    public async Task FailedHelperStopRetainsMuteAndRefusesReplacementUntilReleaseConfirmed()
    {
        var playback = new JKiller.Windows.Viewing.PlaybackAudioGate(); var helper = new FakeHelper { FailStop = true }; var rpc = new FakeRpc();
        await using var engine = new ObsMediaEngine(helper, (_, _) => Task.FromResult<IObsRpcClient>(rpc), playbackAudioGate: playback);
        await engine.PrepareAsync(SystemRequest, CancellationToken.None);
        await Assert.ThrowsAsync<IOException>(() => engine.StopAsync(CancellationToken.None)); Assert.True(playback.IsBlocked);
        await Assert.ThrowsAnyAsync<Exception>(() => engine.PrepareAsync(SystemRequest, CancellationToken.None)); Assert.Equal(1, helper.Starts);
        helper.FailStop = false; await engine.StopAsync(CancellationToken.None); Assert.False(playback.IsBlocked);
    }
    [Fact]
    public async Task ApplicationCaptureDoesNotMuteViewers()
    {
        var playback = new JKiller.Windows.Viewing.PlaybackAudioGate();
        await using var engine = new ObsMediaEngine(new FakeHelper(), (_, _) => Task.FromResult<IObsRpcClient>(new FakeRpc()), playbackAudioGate: playback);
        await engine.PrepareAsync(Request with { Audio = new(AudioMode.Application, Request.Video.Process) }, CancellationToken.None);
        Assert.False(playback.IsBlocked);
    }
    [Fact]
    public async Task FailedViewerMutePreventsHelperLaunch()
    {
        var playback = new JKiller.Windows.Viewing.PlaybackAudioGate(); var helper = new FakeHelper();
        using var viewer = playback.Register((blocked, _) => blocked ? Task.FromException(new IOException("mute failed")) : Task.CompletedTask);
        await using var engine = new ObsMediaEngine(helper, (_, _) => Task.FromResult<IObsRpcClient>(new FakeRpc()), playbackAudioGate: playback);
        await Assert.ThrowsAsync<IOException>(() => engine.PrepareAsync(SystemRequest, CancellationToken.None));
        Assert.Equal(0, helper.Starts); Assert.False(playback.IsBlocked);
    }
    [Fact]
    public async Task SafetyFaultStopsHelperBeforeUnmutingAndDoesNotReleaseOtherEngineLease()
    {
        var playback = new JKiller.Windows.Viewing.PlaybackAudioGate(); var events = new List<string>();
        using var viewer = playback.Register((blocked, _) => { events.Add(blocked ? "mute" : "unmute"); return Task.CompletedTask; });
        var rpc = new FakeRpc(); var helper = new FakeHelper { Events = events };
        await using var faulting = new ObsMediaEngine(helper, (_, _) => Task.FromResult<IObsRpcClient>(rpc), playbackAudioGate: playback);
        await using var other = new ObsMediaEngine(new FakeHelper(), (_, _) => Task.FromResult<IObsRpcClient>(new FakeRpc()), playbackAudioGate: playback);
        await faulting.PrepareAsync(SystemRequest, CancellationToken.None); await other.PrepareAsync(SystemRequest, CancellationToken.None);
        var fault = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        faulting.SafetyFault += (_, _) => fault.TrySetResult(); rpc.UnarmedReason = "video_source_lost";
        await fault.Task.WaitAsync(TimeSpan.FromSeconds(2));
        // Wait for the same serialized cleanup after the asynchronous heartbeat fault.
        await faulting.StopAsync(CancellationToken.None);
        Assert.True(playback.IsBlocked); Assert.Equal("helper stop", events[^1]);
        await other.StopAsync(CancellationToken.None); Assert.False(playback.IsBlocked); Assert.Equal("unmute", events[^1]);
    }
    private static BroadcastRequest SystemRequest => Request with { Video = new("display", SourceKind.Monitor, 8, null, "Display"), Audio = new(AudioMode.SystemOutput, RenderEndpointId: "render:selected") };
    private sealed class FakeHelper : IOwnedObsProcess
    {
        public int Stops, Starts;
        public List<string>? Events; public bool FailStop, StopTokenCancelled;
        public Task<OwnedEngineHandle> StartAsync(CancellationToken cancellationToken) { Starts++; Events?.Add("helper start"); return Task.FromResult(new OwnedEngineHandle(new(1, 2, "obs64.exe"), "test", new(100, 200, "JKiller.exe"))); }
        public Task StopAsync(CancellationToken cancellationToken) { Stops++; StopTokenCancelled = cancellationToken.IsCancellationRequested; if (FailStop) throw new IOException("helper stop failed"); Events?.Add("helper stop"); return Task.CompletedTask; }
    }
    private sealed class FakeRpc : IObsRpcClient, IAsyncDisposable
    {
        public List<string>? Events; public bool FailDispose;
        public ValueTask DisposeAsync() => FailDispose ? ValueTask.FromException(new IOException("RPC dispose failed")) : ValueTask.CompletedTask;
        public bool FailNvenc, WrongReadback, DelayStart, Publishing, HandshakeFailed;
        public bool VideoReady = true;
        public bool RepeatHeaders = true;
        public bool MissingAudioCapability, CancelOnArm, Disarmed, LoseAudioAfterArm;
        public bool AudioReady = true;
        public CancellationTokenSource? Cancellation;
        public JsonElement Arm;
        public string? UnarmedReason;
        public TaskCompletionSource PipelinePolled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Starts;
        public List<string> PreparedEncoders = [];
        public JsonElement VideoSettings;
        public JsonElement AudioSettings;
        public string? AudioKind;
        public JsonElement Transform = JsonSerializer.SerializeToElement(new { });
        public string? VideoKind;
        private JsonElement videoOutput = JsonSerializer.SerializeToElement(new { outputWidth = 1920, outputHeight = 1080, fpsNumerator = 60, fpsDenominator = 1 });
        public TaskCompletionSource StartEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CompleteStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<JsonElement> RequestAsync(string requestType, JsonElement data, CancellationToken cancellationToken)
        {
            if (requestType == "CreateInput") Events?.Add("capture");
            if (requestType == "SetVideoSettings") videoOutput = data.Clone();
            if (requestType == "CreateInput" && data.GetProperty("inputName").GetString() == "JK Video") { VideoSettings = data.GetProperty("inputSettings").Clone(); VideoKind = data.GetProperty("inputKind").GetString(); }
            if (requestType == "CreateInput" && data.GetProperty("inputName").GetString() != "JK Video") { AudioSettings = data.GetProperty("inputSettings").Clone(); AudioKind = data.GetProperty("inputKind").GetString(); }
            if (requestType == "GetSceneItemList") return JsonSerializer.SerializeToElement(new { sceneItems = new[] { new { sourceName = "JK Video", sceneItemId = 1 } } });
            if (requestType == "SetSceneItemTransform") Transform = data.GetProperty("sceneItemTransform").Clone();
            if (requestType == "GetVideoSettings") return JsonSerializer.SerializeToElement(new { outputWidth = WrongReadback ? 1280 : videoOutput.GetProperty("outputWidth").GetInt32(), outputHeight = videoOutput.GetProperty("outputHeight").GetInt32(), fpsNumerator = videoOutput.GetProperty("fpsNumerator").GetInt32(), fpsDenominator = 1 });
            if (requestType != "CallVendorRequest") return JsonSerializer.SerializeToElement(new { });
            var vendor = data.GetProperty("requestType").GetString();
            var args = data.GetProperty("requestData");
            object response = new { success = true, armed = true, videoReady = true };
            if (vendor == "Arm") { Arm = args.Clone(); response = new { armed = AudioReady, error = "Selected audio failed initialization." }; if (LoseAudioAfterArm) AudioReady = false; if (CancelOnArm) Cancellation!.Cancel(); }
            if (vendor == "GetSafetyStatus") response = new { videoReady = true, audioRunning = AudioReady, capturedAudioFrames = 0, emittedAudioFrames = 0, audioError = AudioReady ? "" : "Audio device initialization failed." };
            if (vendor == "Heartbeat" && UnarmedReason is not null) response = new { armed = false, stopReason = UnarmedReason };
            if (vendor == "GetCapabilities") response = new { encoders = new[] { new { id = "obs_nvenc_h264_tex" }, new { id = "obs_x264" }, new { id = "ffmpeg_opus" } }, sources = (MissingAudioCapability ? new[] { "jk_bound_capture" } : new[] { "jk_bound_capture", "jk_process_audio", "jk_system_audio" }).Select(id => new { id }), services = new[] { new { id = "whip_custom" } } };
            if (vendor == "PreparePipeline")
            {
                var encoder = args.GetProperty("encoder").GetString()!; PreparedEncoders.Add(encoder);
                response = new { success = !(FailNvenc && encoder.Contains("nvenc")), encoder, codec = "h264", hardwareInitialized = encoder.Contains("nvenc"), bitrateKbps = args.GetProperty("bitrateKbps").GetInt32(), bFrames = 0, lookahead = false, repeatHeaders = RepeatHeaders };
            }
            if (vendor == "StartPipeline") { Starts++; StartEntered.TrySetResult(); if (DelayStart) await CompleteStart.Task; Publishing = true; }
            if (vendor is "StopPipeline" or "Disarm") { Publishing = false; Disarmed = true; }
            if (vendor == "GetPipelineState") { PipelinePolled.TrySetResult(); response = new { outputActive = Publishing && !HandshakeFailed, sentMediaBytes = Publishing && !HandshakeFailed ? 100 : 0, sentFrames = Publishing && VideoReady ? 1 : 0, outputError = HandshakeFailed ? "Handshake refused." : "" }; }
            return JsonSerializer.SerializeToElement(new { responseData = response });
        }
    }
}
