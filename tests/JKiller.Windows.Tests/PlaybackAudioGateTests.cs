using JKiller.Windows.Viewing;
using Xunit;

namespace JKiller.Windows.Tests;

public sealed class PlaybackAudioGateTests
{
    [Fact]
    public async Task AcquireWaitsForActualMuteAndDuplicateLeaseReleaseCannotUnmuteAnotherOwner()
    {
        var gate = new PlaybackAudioGate();
        var muted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var states = new List<bool>();
        using var registration = gate.Register(async (blocked, _) => { states.Add(blocked); if (blocked) { muted.TrySetResult(); await release.Task; } });
        var acquiring = gate.AcquireAsync(CancellationToken.None);
        await muted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(acquiring.IsCompleted);
        release.SetResult();
        var first = await acquiring; var second = await gate.AcquireAsync(CancellationToken.None);
        await first.DisposeAsync(); await first.DisposeAsync();
        Assert.True(gate.IsBlocked); Assert.True(states[^1]);
        await second.DisposeAsync(); Assert.False(gate.IsBlocked); Assert.False(states[^1]);
    }

    [Fact]
    public async Task LateOpenedViewerStartsBlocked()
    {
        var gate = new PlaybackAudioGate(); await using var lease = await gate.AcquireAsync(CancellationToken.None);
        var applied = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = gate.Register((blocked, _) => { applied.TrySetResult(blocked); return Task.CompletedTask; });
        Assert.True(await applied.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task FailedMuteRollsBackAndNextAcquisitionCanRetry()
    {
        var gate = new PlaybackAudioGate(); var fail = true; var states = new List<bool>();
        using var registration = gate.Register((blocked, _) => { states.Add(blocked); if (blocked && fail) throw new InvalidOperationException("native mute failed"); return Task.CompletedTask; });
        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.AcquireAsync(CancellationToken.None));
        Assert.False(gate.IsBlocked); Assert.False(states[^1]);
        fail = false; await using var lease = await gate.AcquireAsync(CancellationToken.None); Assert.True(gate.IsBlocked);
    }

    [Fact]
    public async Task ViewerRegisteredDuringAcquireMustFinishMutingBeforeAcquireReturns()
    {
        var gate = new PlaybackAudioGate();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var first = gate.Register(async (blocked, _) => { if (blocked) { entered.TrySetResult(); await proceed.Task; } });
        var acquiring = gate.AcquireAsync(CancellationToken.None); await entered.Task;
        var lateEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lateProceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var late = gate.Register(async (blocked, _) => { if (blocked) { lateEntered.TrySetResult(); await lateProceed.Task; } });
        proceed.SetResult(); await lateEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var returnedBeforeMute = acquiring.IsCompleted;
        lateProceed.SetResult(); await using var lease = await acquiring;
        Assert.False(returnedBeforeMute);
    }

    [Fact]
    public async Task CancelledAcquireDoesNotReleaseAnotherOwnersLease()
    {
        var gate = new PlaybackAudioGate(); await using var owner = await gate.AcquireAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        using var viewer = gate.Register((blocked, _) => { if (blocked) cancellation.Cancel(); return Task.CompletedTask; });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gate.AcquireAsync(cancellation.Token));
        Assert.True(gate.IsBlocked);
    }
}
