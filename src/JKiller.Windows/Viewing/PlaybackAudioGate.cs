namespace JKiller.Windows.Viewing;

public interface IPlaybackAudioGate
{
    Task<IAsyncDisposable> AcquireAsync(CancellationToken token);
}

/// <summary>Serializes host mute changes without synchronously waiting on a viewer's UI dispatcher.</summary>
public sealed class PlaybackAudioGate : IPlaybackAudioGate
{
    private readonly object sync = new();
    private readonly List<Registration> registrations = [];
    private Task tail = Task.CompletedTask;
    private int leases;
    private volatile bool blocked;
    public bool IsBlocked => blocked;

    public IDisposable Register(Func<bool, CancellationToken, Task> apply)
    {
        ArgumentNullException.ThrowIfNull(apply);
        var registration = new Registration(this, apply);
        lock (sync)
        {
            registrations.Add(registration);
            // Register cannot await. Observe its initial update; acquisition retries every live
            // registration and therefore cannot mistake an earlier failed mute for success.
            _ = Observe(Enqueue(() => registration.ApplyAsync(blocked, CancellationToken.None)));
        }
        return registration;
    }

    public async Task<IAsyncDisposable> AcquireAsync(CancellationToken token)
    {
        await Enqueue(async () =>
        {
            token.ThrowIfCancellationRequested();
            leases++; blocked = true;
            try { await ApplyAsync(true, token); token.ThrowIfCancellationRequested(); }
            catch
            {
                leases--;
                if (leases == 0)
                {
                    // If rollback itself fails, remain conservatively blocked and surface it.
                    blocked = false;
                    try { await ApplyAsync(false, CancellationToken.None); }
                    catch { blocked = true; throw; }
                }
                throw;
            }
        });
        return new Lease(this);
    }

    private Task ReleaseAsync() => Enqueue(async () =>
    {
        if (--leases != 0) return;
        blocked = false;
        try { await ApplyAsync(false, CancellationToken.None); }
        catch { blocked = true; throw; }
    });

    private async Task ApplyAsync(bool value, CancellationToken token)
    {
        var applied = new HashSet<Registration>();
        while (true)
        {
            Registration[] viewers;
            lock (sync) viewers = registrations.Where(viewer => applied.Add(viewer)).ToArray();
            if (viewers.Length == 0) return;
            // Attempt every viewer even when one fails. A viewer may register while these
            // asynchronous mutes run; include it before declaring acquisition complete.
            await Task.WhenAll(viewers.Select(async viewer => await viewer.ApplyAsync(value, token)));
        }
    }

    private Task Enqueue(Func<Task> operation)
    {
        lock (sync)
        {
            var previous = tail;
            return tail = Run();
            async Task Run()
            {
                await Task.Yield();
                await Observe(previous);
                await operation();
            }
        }
    }

    private static async Task Observe(Task task) { try { await task; } catch { /* The initiating operation owns its failure. */ } }
    private sealed class Lease(PlaybackAudioGate owner) : IAsyncDisposable
    {
        private PlaybackAudioGate? current = owner;
        public ValueTask DisposeAsync() => Interlocked.Exchange(ref current, null) is { } gate ? new(gate.ReleaseAsync()) : ValueTask.CompletedTask;
    }
    private sealed class Registration(PlaybackAudioGate owner, Func<bool, CancellationToken, Task> apply) : IDisposable
    {
        private volatile bool disposed;
        public Task ApplyAsync(bool value, CancellationToken token) => disposed ? Task.CompletedTask : apply(value, token);
        public void Dispose() { lock (owner.sync) { disposed = true; owner.registrations.Remove(this); } }
    }
}
