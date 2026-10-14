using JKiller.Contracts;
using JKiller.Core.Media;
using JKiller.Core.Settings;
using System.Globalization;
using System.Text.Json;

namespace JKiller.Windows.Obs;

public sealed class ObsMediaEngine(IOwnedObsProcess helper, Func<OwnedEngineHandle, CancellationToken, Task<IObsRpcClient>> connect, bool allowCpuFallback = false) : IMediaEngine, IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1);
    private IObsRpcClient? rpc;
    private AppliedMediaSettings? prepared;
    private long epoch;
    private long generation;
    private CancellationTokenSource? heartbeatCancellation;
    private Task? heartbeat;
    private volatile bool connected;
    public event EventHandler<Exception>? SafetyFault;

    public ObsMediaEngine(OwnedObsProcess helper, bool allowCpuFallback = false)
        : this(helper, async (handle, token) => await ObsRpcClient.ConnectAsync(handle.PipeName, handle.Identity, token), allowCpuFallback) { }

    public async Task<AppliedMediaSettings> PrepareAsync(BroadcastRequest request, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        var current = Interlocked.Read(ref epoch);
        try
        {
            if (prepared is not null || rpc is not null) throw new InvalidOperationException("Stop the current capture before preparing another selection.");
            var validation = StreamSettings.Validate(request.Settings);
            if (!validation.IsValid) throw new ArgumentException(string.Join(" ", validation.Errors), nameof(request));
            if (request.Audio.Application is not null)
                throw new InvalidOperationException("Application audio is disabled until an enforced process-tree boundary is available. Use video-only capture.");
            if (request.Video.NativeHandle == 0 || (request.Video.Kind == SourceKind.Window && request.Video.Process is null))
                throw new InvalidOperationException("Select an identifiable live display or window.");
            var handle = await helper.StartAsync(cancellationToken);
            rpc = await connect(handle, cancellationToken);
            var capabilities = await Vendor("GetCapabilities", new { }, cancellationToken);
            bool Has(string category, string id) => capabilities.GetProperty(category).EnumerateArray().Any(x => x.GetProperty("id").GetString() == id);
            if (!Has("sources", "jk_bound_capture") || !Has("services", "whip_custom") || !Has("encoders", "ffmpeg_opus"))
                throw new InvalidOperationException("Pinned native capture, WHIP or Opus component unavailable.");
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
                    pid = application.Pid, creation_filetime = application.CreationFileTime.ToString(CultureInfo.InvariantCulture), image_path = application.ExecutablePath, selection_generation = generation
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
            var arm = await Vendor("Arm", new { ownerPid = owner.Pid, ownerCreationFileTime = owner.CreationFileTime.ToString(CultureInfo.InvariantCulture), ownerImagePath = owner.ExecutablePath, generation, audioSource = audioName, videoSource = "JK Video" }, cancellationToken);
            if (!arm.GetProperty("armed").GetBoolean()) throw new InvalidOperationException("Native capture safety guard refused the selected sources.");
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
                if (!state.GetProperty("armed").GetBoolean()) throw new InvalidOperationException("Native source or safety guard was lost.");
                if (connected && !(await Vendor("GetPipelineState", new { }, token)).GetProperty("outputActive").GetBoolean())
                    throw new InvalidOperationException("The media connection ended. Start again to request a fresh publication grant.");
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { Interlocked.Increment(ref epoch); SafetyFault?.Invoke(this, error); _ = StopAsync(CancellationToken.None); }
    }
    private async Task Cleanup()
    {
        connected = false;
        heartbeatCancellation?.Cancel();
        if (heartbeat is not null) await heartbeat;
        heartbeatCancellation?.Dispose(); heartbeatCancellation = null; heartbeat = null; prepared = null;
        try { if (rpc is not null) await Vendor("Disarm", new { }, CancellationToken.None); }
        catch (Exception) { /* Killing this owned helper remains the final safety boundary. */ }
        finally
        {
            if (rpc is IAsyncDisposable disposable) await disposable.DisposeAsync();
            rpc = null; await helper.StopAsync(CancellationToken.None);
        }
    }
    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None);
}
