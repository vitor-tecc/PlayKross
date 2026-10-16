using JKiller.Contracts;
using JKiller.Core.Media;
using JKiller.Core.Rooms;
using JKiller.Core.Settings;
namespace JKiller.Core.Broadcast;
public enum BroadcastState { Stopped, Preparing, Starting, Broadcasting, Reconfiguring, Stopping, Faulted }
public sealed class BroadcastController : IAsyncDisposable
{
    private readonly IMediaEngine engine;
    private readonly IRoomClient room;
    private readonly Uri origin;
    private readonly TimeProvider clock;
    private readonly SemaphoreSlim gate = new(1);
    private readonly object lifetime = new();
    private CancellationTokenSource operation = new();
    private long generation;
    private bool disposed, faulted;
    private BroadcastRequest? request;
    private CancellationTokenSource? leaseCancellation;
    private Task? leaseTask;
    public BroadcastController(IMediaEngine engine, IRoomClient room, Uri origin, TimeProvider clock)
    {
        this.engine = engine; this.room = room; this.origin = origin; this.clock = clock;
        engine.SafetyFault += OnSafetyFault;
    }
    public BroadcastState State { get; private set; }
    public Guid? StreamId { get; private set; }
    public AppliedMediaSettings? Applied { get; private set; }
    public Exception? LastError { get; private set; }
    public event EventHandler? BroadcastStateChanged;
    private void SetState(BroadcastState state) { State = state; BroadcastStateChanged?.Invoke(this, EventArgs.Empty); }
    private (long Version, CancellationToken Token) Snapshot()
    {
        lock (lifetime) { ObjectDisposedException.ThrowIf(disposed, this); return (generation, operation.Token); }
    }
    private void Check(long version, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (lifetime) { if (version != generation || disposed) throw new OperationCanceledException(token); }
    }
    private void Invalidate(bool fault)
    {
        lock (lifetime)
        {
            generation++; faulted = fault; var previous = operation;
            operation = new(); previous.Cancel(); previous.Dispose();
        }
    }
    public async Task StartAsync(BroadcastRequest request, CancellationToken token)
    {
        var snapshot = Snapshot();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, snapshot.Token);
        await gate.WaitAsync(linked.Token);
        try
        {
            Check(snapshot.Version, linked.Token);
            if (State == BroadcastState.Broadcasting) return;
            faulted = false; LastError = null; this.request = request;
            SetState(BroadcastState.Preparing);
            await PublishAsync(request, snapshot.Version, linked.Token);
        }
        catch (Exception error) { LastError ??= error; await CleanupAsync(); throw; }
        finally { gate.Release(); }
    }
    private async Task PublishAsync(BroadcastRequest next, long version, CancellationToken token)
    {
        var validation = StreamSettings.Validate(next.Settings);
        if (!validation.IsValid) throw new ArgumentException(string.Join(" ", validation.Errors), nameof(next));
        var grant = await room.ReservePublishAsync(token);
        // Retain ownership even when an uncooperative room response arrives after cancellation.
        if (StreamId is { } previous && previous != grant.StreamId) throw new InvalidOperationException("The room changed publication ownership during restart.");
        StreamId = grant.StreamId; Check(version, token);
        await room.RenewPublishAsync(grant.StreamId, token); Check(version, token);
        if (leaseTask is null)
        {
            leaseCancellation = new();
            leaseTask = RenewLease(grant.StreamId, leaseCancellation.Token);
        }
        Applied = await engine.PrepareAsync(next, token); Check(version, token);
        if (grant.ExpiresAt <= clock.GetUtcNow().AddSeconds(5))
        {
            grant = await room.ReservePublishAsync(token); Check(version, token);
            if (grant.StreamId != StreamId) throw new InvalidOperationException("A renewed grant changed the stream identity.");
        }
        if (grant.ExpiresAt <= clock.GetUtcNow().AddSeconds(5)) throw new InvalidOperationException("The room supplied an expired publication grant.");
        var endpoint = new Uri(origin, grant.WhipUri);
        if (endpoint.GetLeftPart(UriPartial.Authority) != origin.GetLeftPart(UriPartial.Authority))
            throw new InvalidOperationException("The publication endpoint must belong to the joined server.");
        SetState(BroadcastState.Starting);
        await engine.StartAsync(endpoint, grant.Token, token); Check(version, token);
        request = next; SetState(BroadcastState.Broadcasting);
    }
    public async Task ChangeSettingsAsync(BroadcastSettings settings, CancellationToken token)
    {
        var snapshot = Snapshot();
        var validation = StreamSettings.Validate(settings);
        if (!validation.IsValid) throw new ArgumentException(string.Join(" ", validation.Errors), nameof(settings));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, snapshot.Token);
        await gate.WaitAsync(linked.Token);
        try
        {
            Check(snapshot.Version, linked.Token);
            if (State != BroadcastState.Broadcasting || request is null) throw new InvalidOperationException("Start a broadcast before changing its settings.");
            SetState(BroadcastState.Reconfiguring);
            await engine.StopAsync(linked.Token); Check(snapshot.Version, linked.Token);
            await PublishAsync(request with { Settings = settings }, snapshot.Version, linked.Token);
        }
        catch (Exception error) { LastError ??= error; await CleanupAsync(); throw; }
        finally { gate.Release(); }
    }
    public async Task StopAsync(CancellationToken token)
    {
        Invalidate(false);
        // Cancellation must not leave the owned publication running.
        await gate.WaitAsync(CancellationToken.None);
        try { SetState(BroadcastState.Stopping); await CleanupAsync(); }
        finally { gate.Release(); }
        token.ThrowIfCancellationRequested();
    }
    private async Task CleanupAsync()
    {
        SetState(faulted ? BroadcastState.Faulted : BroadcastState.Stopping);
        leaseCancellation?.Cancel();
        if (leaseTask is not null) await leaseTask;
        leaseCancellation?.Dispose(); leaseCancellation = null; leaseTask = null;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await engine.StopAsync(deadline.Token); }
        catch (Exception error) { LastError ??= error; faulted = true; }
        var owned = StreamId; StreamId = null; Applied = null; request = null;
        if (owned is { } id)
        {
            try { await room.StopPublishAsync(id, deadline.Token); }
            catch (Exception error) { LastError ??= error; faulted = true; }
        }
        SetState(faulted ? BroadcastState.Faulted : BroadcastState.Stopped);
    }
    private async Task RenewLease(Guid id, CancellationToken token)
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), clock, token);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                deadline.CancelAfter(TimeSpan.FromSeconds(15));
                await room.RenewPublishAsync(id, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { if (!token.IsCancellationRequested) OnSafetyFault(this, new InvalidOperationException("Publication lease could not be renewed. Broadcasting stopped.", error)); }
    }
    private void OnSafetyFault(object? sender, Exception error)
    {
        lock (lifetime) { if (disposed) return; LastError = error; }
        Invalidate(true); _ = HandleFaultAsync();
    }
    private async Task HandleFaultAsync()
    {
        await gate.WaitAsync();
        try { await CleanupAsync(); }
        catch (Exception error) { LastError ??= error; State = BroadcastState.Faulted; }
        finally { gate.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        lock (lifetime) { if (disposed) return; disposed = true; }
        engine.SafetyFault -= OnSafetyFault; Invalidate(false);
        await gate.WaitAsync();
        try { await CleanupAsync(); }
        finally { gate.Release(); }
    }
}
