using JKiller.Contracts;
using JKiller.Core.Media;
using JKiller.Core.Settings;
using System.Globalization;
using System.Text.Json;
using JKiller.Windows.Viewing;

namespace JKiller.Windows.Obs;

public sealed class ObsMediaEngine(IOwnedObsProcess helper, Func<OwnedEngineHandle, CancellationToken, Task<IObsRpcClient>> connect, bool allowCpuFallback = false, IPlaybackAudioGate? playbackAudioGate = null) : IMediaEngine, IAsyncDisposable
{
    private readonly IPlaybackAudioGate playbackGate = playbackAudioGate ?? new PlaybackAudioGate();
    private IAsyncDisposable? playbackLease;
    private readonly SemaphoreSlim gate = new(1);
    private IObsRpcClient? rpc;
    private AppliedMediaSettings? prepared;
    private long epoch;
    private long generation;
    private CancellationTokenSource? heartbeatCancellation;
    private Task? heartbeat;
    private volatile bool connected;
    public event EventHandler<Exception>? SafetyFault;

    public ObsMediaEngine(OwnedObsProcess helper, bool allowCpuFallback = false, IPlaybackAudioGate? playbackAudioGate = null)
        : this(helper, async (handle, token) => await ObsRpcClient.ConnectAsync(handle.PipeName, handle.Identity, token), allowCpuFallback, playbackAudioGate) { }

    public async Task<AppliedMediaSettings> PrepareAsync(BroadcastRequest request, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        var current = Interlocked.Read(ref epoch);
        try
        {
            if (prepared is not null || rpc is not null || playbackLease is not null) throw new InvalidOperationException("Stop the current capture before preparing another selection.");
            var validation = StreamSettings.Validate(request.Settings);
            if (!validation.IsValid) throw new ArgumentException(string.Join(" ", validation.Errors), nameof(request));
            var audioEligibility = JKiller.Core.Capture.AudioPolicy.EvaluateSelection(request.Audio, request.Video.Kind, Environment.ProcessId);
            if (!audioEligibility.Allowed) throw new InvalidOperationException(audioEligibility.Reason);
            if (request.Video.NativeHandle == 0 || (request.Video.Kind == SourceKind.Window && request.Video.Process is null))
                throw new InvalidOperationException("Select an identifiable live display or window.");
            // Sources begin capture during CreateInput. Await native viewer mute before even
            // starting the helper, rather than waiting for the later StartPipeline operation.
            if (request.Audio.Mode == AudioMode.SystemOutput) playbackLease = await playbackGate.AcquireAsync(cancellationToken);
            var handle = await helper.StartAsync(cancellationToken);
            rpc = await connect(handle, cancellationToken);
            var capabilities = await Vendor("GetCapabilities", new { }, cancellationToken);
            bool Has(string category, string id) => capabilities.GetProperty(category).EnumerateArray().Any(x => x.GetProperty("id").GetString() == id);
            if (!Has("sources", "jk_bound_capture") || !Has("services", "whip_custom") || !Has("encoders", "ffmpeg_opus"))
                throw new InvalidOperationException("Pinned native capture, WHIP or Opus component unavailable.");
            var audioKind = request.Audio.Mode switch { AudioMode.Application => "jk_process_audio", AudioMode.SystemOutput => "jk_system_audio", _ => null };
            if (audioKind is not null && !Has("sources", audioKind))
                throw new InvalidOperationException("The selected native audio component is unavailable; select audio off or use the complete updated package.");
            var settings = request.Settings;
            async Task ConfigureVideo()
            {
                await Request("SetVideoSettings", new { baseWidth = settings.Width, baseHeight = settings.Height, outputWidth = settings.Width, outputHeight = settings.Height, fpsNumerator = settings.Fps, fpsDenominator = 1 }, cancellationToken);
                var readback = await Request("GetVideoSettings", new { }, cancellationToken);
                if (readback.GetProperty("outputWidth").GetInt32() != settings.Width || readback.GetProperty("outputHeight").GetInt32() != settings.Height ||
                    readback.GetProperty("fpsNumerator").GetInt32() != settings.Fps || readback.GetProperty("fpsDenominator").GetInt32() != 1)
                    throw new InvalidDataException("Engine video readback does not match applied settings.");
            }
            await ConfigureVideo();
            generation++;
            var selected = request.Video.Process;
            await Request("CreateInput", new { sceneName = "JKiller", inputName = "JK Video", inputKind = "jk_bound_capture", inputSettings = new
            {
                native_handle = request.Video.NativeHandle.ToString(CultureInfo.InvariantCulture),
                capture_kind = request.Video.Kind == SourceKind.Window ? "window" : "monitor",
                pid = selected?.Pid ?? 0, creation_filetime = (selected?.CreationFileTime ?? 0).ToString(CultureInfo.InvariantCulture),
                image_path = selected?.ExecutablePath ?? "", selection_generation = generation
            }, sceneItemEnabled = true }, cancellationToken);
            string audioName = "";
            if (request.Audio.Application is { } application)
            {
                audioName = "JK Application Audio";
                await Request("CreateInput", new { sceneName = "JKiller", inputName = audioName, inputKind = "jk_process_audio", inputSettings = new
                {
                    audio_mode = "Application", pid = application.Pid, creation_filetime = application.CreationFileTime.ToString(CultureInfo.InvariantCulture), image_path = application.ExecutablePath, selection_generation = generation
                }, sceneItemEnabled = true }, cancellationToken);
            }
            else if (request.Audio.Mode == AudioMode.SystemOutput)
            {
                audioName = "JK System Audio";
                await Request("CreateInput", new { sceneName = "JKiller", inputName = audioName, inputKind = "jk_system_audio", inputSettings = new
                {
                    audio_mode = "SystemOutput", endpoint_id = request.Audio.RenderEndpointId, selection_generation = generation
                }, sceneItemEnabled = true }, cancellationToken);
            }
            async Task<JsonElement> PrepareEncoder(string encoder) => await Vendor("PreparePipeline", new { encoder, bitrateKbps = settings.EncoderKbps, generation }, cancellationToken);
            var encoded = Has("encoders", "obs_nvenc_h264_tex") ? await PrepareEncoder("obs_nvenc_h264_tex") : JsonSerializer.SerializeToElement(new { success = false });
            if (!encoded.GetProperty("success").GetBoolean() && allowCpuFallback && Has("encoders", "obs_x264"))
            {
                var reduced = StreamSettings.DefaultFor(Math.Min(settings.Height, 720), 30);
                settings = reduced with { BitrateMbps = Math.Min(settings.BitrateMbps, reduced.BitrateMbps) };
                await ConfigureVideo();
                encoded = await PrepareEncoder("obs_x264");
            }
            if (!encoded.GetProperty("success").GetBoolean()) throw new InvalidOperationException("NVENC could not initialize. CPU encoding requires explicitly selecting the fallback.");
            if (encoded.GetProperty("bitrateKbps").GetInt32() != settings.EncoderKbps || encoded.GetProperty("bFrames").GetInt32() != 0 || encoded.GetProperty("lookahead").GetBoolean())
                throw new InvalidDataException("Encoder readback does not match the low-latency configuration.");
            if (!encoded.TryGetProperty("repeatHeaders", out var headers) || headers.ValueKind != JsonValueKind.True)
                throw new InvalidDataException("H.264 setup header repetition is unavailable. Use the complete updated PlayKross package so browser viewers can decode the stream.");
            var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
            JsonElement captureState;
            while (!(captureState = await Vendor("GetSafetyStatus", new { }, cancellationToken)).GetProperty("videoReady").GetBoolean())
            {
                if (DateTimeOffset.UtcNow >= deadline) throw new InvalidOperationException("The original capture source is unavailable: " + captureState);
                await Task.Delay(100, cancellationToken);
            }
            var owner = handle.Controller;
            var items = await Request("GetSceneItemList", new { sceneName = "JKiller" }, cancellationToken);
            var item = items.GetProperty("sceneItems").EnumerateArray().Single(value => value.GetProperty("sourceName").GetString() == "JK Video");
            await Request("SetSceneItemTransform", new { sceneName = "JKiller", sceneItemId = item.GetProperty("sceneItemId").GetInt64(), sceneItemTransform = new
            {
                boundsType = "OBS_BOUNDS_SCALE_INNER", boundsWidth = settings.Width, boundsHeight = settings.Height,
                alignment = 5, boundsAlignment = 0, positionX = 0, positionY = 0, scaleX = 1, scaleY = 1, cropLeft = 0, cropTop = 0, cropRight = 0, cropBottom = 0
            } }, cancellationToken);
            var audioIdentity = request.Audio.Mode switch
            {
                AudioMode.Application => $"process:{request.Audio.Application!.Pid}:{request.Audio.Application.CreationFileTime}:{request.Audio.Application.ExecutablePath}",
                AudioMode.SystemOutput => "endpoint:" + request.Audio.RenderEndpointId,
                _ => ""
            };
            var arm = await Vendor("Arm", new { ownerPid = owner.Pid, ownerCreationFileTime = owner.CreationFileTime.ToString(CultureInfo.InvariantCulture), ownerImagePath = owner.ExecutablePath, generation, audioMode = request.Audio.Mode.ToString(), audioIdentity, audioSource = audioName, videoSource = "JK Video" }, cancellationToken);
            if (!arm.GetProperty("armed").GetBoolean()) throw new InvalidOperationException("Native capture safety guard refused the selected sources. " + (arm.TryGetProperty("error", out var error) ? error.GetString() : ""));
            if (request.Audio.Mode != AudioMode.Off)
            {
                var audioState = await Vendor("GetSafetyStatus", new { }, cancellationToken);
                if (!audioState.TryGetProperty("audioRunning", out var running) || running.ValueKind != JsonValueKind.True)
                    throw new InvalidOperationException("Selected audio is not initialized. " + (audioState.TryGetProperty("audioError", out var audioError) ? audioError.GetString() : "Native audio readiness is unavailable."));
                // Initialized quiet capture is ready; PCM counters establish activity only.
            }
            ThrowIfInvalidated(current, cancellationToken);
            prepared = new(settings, encoded.GetProperty("encoder").GetString()!, encoded.GetProperty("codec").GetString()!, encoded.GetProperty("hardwareInitialized").GetBoolean());
            heartbeatCancellation = new CancellationTokenSource();
            heartbeat = KeepAlive(heartbeatCancellation.Token);
            return prepared;
        }
        catch { await Cleanup(); throw; }
        finally { gate.Release(); }
    }

    public async Task StartAsync(Uri whipUri, string token, CancellationToken cancellationToken)
    {
        if ((whipUri.Scheme != "https" && !(whipUri.Scheme == "http" && whipUri.IsLoopback)) || !string.IsNullOrEmpty(whipUri.UserInfo) || string.IsNullOrEmpty(token))
            throw new ArgumentException("WHIP requires an authenticated HTTPS endpoint (or localhost HTTP).");
        var current = Interlocked.Read(ref epoch);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (prepared is null) throw new InvalidOperationException("Prepare a native source before publishing.");
            ThrowIfInvalidated(current, cancellationToken);
            var result = await Vendor("StartPipeline", new { url = whipUri.AbsoluteUri, token, generation }, cancellationToken);
            ThrowIfInvalidated(current, cancellationToken);
            if (!result.GetProperty("success").GetBoolean()) throw new InvalidOperationException("WHIP publication failed; inspect native diagnostics.");
            var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
            while (true)
            {
                var state = await Vendor("GetPipelineState", new { }, cancellationToken);
                ThrowIfInvalidated(current, cancellationToken);
                if (state.GetProperty("outputActive").GetBoolean() && state.GetProperty("sentMediaBytes").GetInt64() > 0 && state.GetProperty("sentFrames").GetInt64() > 0) break;
                if (!string.IsNullOrEmpty(state.GetProperty("outputError").GetString())) throw new InvalidOperationException("The media handshake failed; inspect native diagnostics.");
                if (DateTimeOffset.UtcNow >= deadline) throw new TimeoutException("The media connection did not send data within 20 seconds.");
                await Task.Delay(100, cancellationToken);
            }
            connected = true;
        }
        catch { await Cleanup(); throw; }
        finally { gate.Release(); }
    }
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref epoch);
        await gate.WaitAsync(CancellationToken.None);
        try { await Cleanup(); }
        finally { gate.Release(); }
    }
    public async Task<MediaDiagnostics> GetDiagnosticsAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            if (!connected || rpc is null) throw new InvalidOperationException("No active native publication to measure.");
            return await new ObsDiagnosticsReader(ct => Vendor("GetDiagnostics", new { }, ct)).ReadAsync(token);
        }
        finally { gate.Release(); }
    }
    private void ThrowIfInvalidated(long current, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (current != Interlocked.Read(ref epoch)) throw new OperationCanceledException("An earlier operation was superseded by Stop.");
    }
    private Task<JsonElement> Request(string type, object data, CancellationToken token) => (rpc ?? throw new InvalidOperationException("Owned helper disconnected.")).RequestAsync(type, JsonSerializer.SerializeToElement(data), token);
    private async Task<JsonElement> Vendor(string type, object data, CancellationToken token) =>
        (await Request("CallVendorRequest", new { vendorName = "jkiller", requestType = type, requestData = data }, token)).GetProperty("responseData").Clone();
    private async Task KeepAlive(CancellationToken token)
    {
        try
        {
            while (true)
            {
                await Task.Delay(500, token);
                var state = await Vendor("Heartbeat", new { }, token);
                if (!state.GetProperty("armed").GetBoolean())
                {
                    var code = state.TryGetProperty("stopReason", out var reason) && reason.ValueKind == JsonValueKind.String ? reason.GetString() : null;
                    throw new InvalidOperationException(code switch
                    {
                        "heartbeat_timeout" => "The native capture heartbeat timed out.",
                        "video_source_lost" => "The selected screen or window became unavailable.",
                        "controller_lost" => "The broadcaster controller became unavailable.",
                        "audio_graph_unsafe" or "audio_monitoring_enabled" => "Native audio routing verification failed. Capture stopped for safety.",
                        "unarmed_output" => "Native output was active without an armed capture guard.",
                        _ => "Native source or safety guard was lost."
                    });
                }
                if (connected && !(await Vendor("GetPipelineState", new { }, token)).GetProperty("outputActive").GetBoolean())
                    throw new InvalidOperationException("The media connection ended. Start again to request a fresh publication grant.");
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { Interlocked.Increment(ref epoch); SafetyFault?.Invoke(this, error); _ = StopAfterFault(); }
    }
    private async Task StopAfterFault()
    {
        try { await StopAsync(CancellationToken.None); }
        catch (Exception error) { SafetyFault?.Invoke(this, error); }
    }
    private async Task Cleanup()
    {
        connected = false;
        heartbeatCancellation?.Cancel();
        if (heartbeat is not null) await heartbeat;
        heartbeatCancellation?.Dispose(); heartbeatCancellation = null; heartbeat = null; prepared = null;
        try { if (rpc is not null) await Vendor("Disarm", new { }, CancellationToken.None); }
        catch (Exception) { /* Killing this owned helper remains the final safety boundary. */ }
        Exception? disposalError = null;
        var client = rpc; rpc = null;
        try { if (client is IAsyncDisposable disposable) await disposable.DisposeAsync(); }
        catch (Exception error) { disposalError = error; }
        try { await helper.StopAsync(CancellationToken.None); }
        catch (Exception error)
        {
            // Unknown helper lifetime: retain this operation's mute lease for a later retry.
            if (disposalError is not null) throw new AggregateException(disposalError, error);
            throw;
        }
        var lease = Interlocked.Exchange(ref playbackLease, null);
        try { if (lease is not null) await lease.DisposeAsync(); }
        catch (Exception error) { if (disposalError is not null) throw new AggregateException(disposalError, error); throw; }
        if (disposalError is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(disposalError).Throw();
    }
    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None);
}
