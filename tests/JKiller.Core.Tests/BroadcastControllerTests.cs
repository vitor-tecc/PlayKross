using JKiller.Contracts;
using JKiller.Core.Broadcast;
using JKiller.Core.Media;
using JKiller.Core.Rooms;
using Xunit;
namespace JKiller.Core.Tests;
public sealed class BroadcastControllerTests
{
    [Fact]
    public async Task PublicationMustHaveARenewedLeaseBeforeStartingMedia()
    {
        var media = new FakeMedia(); var room = new FakeRoom { FailRenewal = true };
        await using var controller = Controller(media, room);
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.StartAsync(Request, CancellationToken.None));
        Assert.Equal(0, media.Starts); Assert.Contains(room.Id, room.Stopped);
    }
    [Fact]
    public async Task LostRenewalStopsAnAlreadyRunningEncoder()
    {
        var media = new FakeMedia(); var room = new FakeRoom(); var clock = new TestClock();
        await using var controller = Controller(media, room, clock);
        await controller.StartAsync(Request, CancellationToken.None);
        room.FailRenewal = true; clock.FireTimer();
        await WaitUntil(() => controller.State == BroadcastState.Faulted);
        Assert.Equal(0, media.Live); Assert.Contains(room.Id, room.Stopped);
    }
    private static BroadcastRequest Request => new(new("fixture", SourceKind.Window, 42, new(123, 456, "fixture.exe"), "Fixture"), AudioSelection.Off, new(1920, 1080, 60, 15));
    private static BroadcastController Controller(FakeMedia media, FakeRoom room, TestClock? clock = null) => new(media, room, new("http://localhost:5000"), clock ?? new());
    [Fact]
    public async Task InvalidSettingEditDoesNotStopAnExistingBroadcast()
    {
        var media = new FakeMedia(); var room = new FakeRoom(); await using var controller = Controller(media, room);
        await controller.StartAsync(Request, CancellationToken.None);
        await Assert.ThrowsAsync<ArgumentException>(() => controller.ChangeSettingsAsync(new(1920, 1080, 120, 15), CancellationToken.None));
        Assert.Equal(1, media.Live); Assert.Empty(room.Stopped); Assert.Equal(BroadcastState.Broadcasting, controller.State);
    }
    [Fact]
    public async Task OnlyOneStartSurvivesConcurrentCommands()
    {
        var media = new FakeMedia(); var room = new FakeRoom(); await using var controller = Controller(media, room);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => controller.StartAsync(Request, CancellationToken.None)));
        Assert.Equal(1, media.Starts); Assert.Equal(1, media.Live); Assert.Equal(BroadcastState.Broadcasting, controller.State);
    }
    [Fact]
    public async Task SettingsRestartKeepsStreamIdentity()
    {
        var media = new FakeMedia(); var room = new FakeRoom(); await using var controller = Controller(media, room);
        await controller.StartAsync(Request, CancellationToken.None); var original = controller.StreamId;
        await controller.ChangeSettingsAsync(new(1280, 720, 30, 6), CancellationToken.None);
        Assert.Equal(original, controller.StreamId); Assert.Equal(1, media.MaximumLive); Assert.Empty(room.Stopped);
        Assert.Equal(720, controller.Applied!.Settings.Height);
    }
    [Fact]
    public async Task SourceRestartKeepsPublicationAndStopsOldMediaBeforePreparingNewSources()
    {
        var media = new FakeMedia(); var room = new FakeRoom(); await using var controller = Controller(media, room);
        await controller.StartAsync(Request, CancellationToken.None);
        var original = controller.StreamId;
        var next = new BroadcastRequest(new("display", SourceKind.Monitor, 7, null, "Display"), new(AudioMode.SystemOutput, RenderEndpointId: "speakers"), new(1280, 720, 30, 6));
        await controller.ChangeSourcesAsync(next, CancellationToken.None);
        Assert.Equal(next, media.PreparedRequest);
        Assert.Equal(original, controller.StreamId);
        Assert.Equal(1, media.MaximumLive);
        Assert.False(media.PreparedWhileLive);
        Assert.Empty(room.Stopped);
        Assert.Equal(BroadcastState.Broadcasting, controller.State);
    }
    [Fact]
    public async Task SourceLossMutesAndStops()
    {
        var media = new FakeMedia(); var room = new FakeRoom(); await using var controller = Controller(media, room);
        await controller.StartAsync(Request, CancellationToken.None); media.FailSource();
        await WaitUntil(() => media.Live == 0 && room.Stopped.Count == 1);
        Assert.Equal(BroadcastState.Faulted, controller.State);
    }
    [Fact]
    public async Task ClosingAppStopsOwnedPublication()
    {
        var media = new FakeMedia(); var room = new FakeRoom(); var controller = Controller(media, room);
        await controller.StartAsync(Request, CancellationToken.None); await controller.DisposeAsync();
        Assert.Equal(0, media.Live); Assert.Contains(room.Id, room.Stopped);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => controller.StartAsync(Request, CancellationToken.None));
    }
    [Fact]
    public async Task ExpiredGrantNeedsFreshHandshake()
    {
        var clock = new TestClock(); var media = new FakeMedia { OnPrepare = () => clock.Now = clock.Now.AddSeconds(61) }; var room = new FakeRoom { Clock = clock };
        await using var controller = Controller(media, room, clock);
        await controller.StartAsync(Request, CancellationToken.None);
        Assert.Equal(2, room.Reserves); Assert.Equal("grant-2", media.LastToken);
    }
    [Fact]
    public async Task CancellationWhilePreparingCannotPublishLateResponse()
    {
        var media = new FakeMedia { HoldPrepare = true }; var room = new FakeRoom(); await using var controller = Controller(media, room);
        using var canceled = new CancellationTokenSource();
        var start = controller.StartAsync(Request, canceled.Token);
        await media.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2)); canceled.Cancel(); media.Release.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        Assert.Equal(0, media.Starts); Assert.Contains(room.Id, room.Stopped);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisconnectWhileRestartingInvalidatesOlderGeneration(bool changeSources)
    {
        var media = new FakeMedia(); var room = new FakeRoom(); await using var controller = Controller(media, room);
        await controller.StartAsync(Request, CancellationToken.None);
        media.HoldPrepare = true;
        var change = changeSources
            ? controller.ChangeSourcesAsync(Request with { Video = new("new-display", SourceKind.Monitor, 7, null, "Display") }, CancellationToken.None)
            : controller.ChangeSettingsAsync(new(1280, 720, 30, 6), CancellationToken.None);
        await media.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2)); media.FailSource(); media.Release.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => change);
        await WaitUntil(() => media.Live == 0 && controller.State == BroadcastState.Faulted);
        Assert.Equal(1, media.Starts); Assert.Contains(room.Id, room.Stopped);
    }
    [Fact]
    public async Task StopInvalidatesStartThatIgnoresCancellation()
    {
        var media = new FakeMedia { HoldPrepare = true }; var room = new FakeRoom(); await using var controller = Controller(media, room);
        var start = controller.StartAsync(Request, CancellationToken.None); await media.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var stop = controller.StopAsync(CancellationToken.None); media.Release.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start); await stop;
        Assert.Equal(0, media.Live); Assert.Equal(0, media.Starts);
    }
    private static async Task WaitUntil(Func<bool> condition) { using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2)); while (!condition()) await Task.Delay(10, deadline.Token); }
    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        private TimerCallback? callback; private object? state;
        public override DateTimeOffset GetUtcNow() => Now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { this.callback = callback; this.state = state; return new ManualTimer(); }
        public void FireTimer() => callback!(state);
        private sealed class ManualTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
    private sealed class FakeRoom : IRoomClient
    {
        public bool FailRenewal;
        public Task RenewPublishAsync(Guid id, CancellationToken token) => FailRenewal ? Task.FromException(new InvalidOperationException("Lease lost.")) : Task.CompletedTask;
        public Guid Id = Guid.NewGuid(); public TestClock Clock = new(); public int Reserves; public List<Guid> Stopped = [];
        public Task<PublishReservation> ReservePublishAsync(CancellationToken token) { Reserves++; return Task.FromResult(new PublishReservation(Id, "generated", new("/media/generated/whip", UriKind.Relative), "grant-" + Reserves, Clock.Now.AddSeconds(60))); }
        public Task StopPublishAsync(Guid id, CancellationToken token) { Stopped.Add(id); return Task.CompletedTask; }
        public Task<SessionGrant> JoinAsync(Uri uri, string invite, string nickname, CancellationToken token) => throw new NotSupportedException();
        public Task<RoomSnapshot> RenameAsync(string nickname, CancellationToken token) => throw new NotSupportedException();
        public Task<PlaybackGrant> GetPlaybackAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class FakeMedia : IMediaEngine
    {
        public event EventHandler<Exception>? SafetyFault;
        public int Starts, Live, MaximumLive; public string? LastToken; public bool HoldPrepare; public Action? OnPrepare;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public BroadcastRequest? PreparedRequest; public bool PreparedWhileLive;
        public async Task<AppliedMediaSettings> PrepareAsync(BroadcastRequest request, CancellationToken token) { PreparedRequest = request; PreparedWhileLive |= Live != 0; OnPrepare?.Invoke(); if (HoldPrepare) { Entered.TrySetResult(); await Release.Task; } return new(request.Settings, "obs_nvenc_h264_tex", "h264", true); }
        public Task StartAsync(Uri uri, string token, CancellationToken cancellation) { Starts++; Live++; MaximumLive = Math.Max(MaximumLive, Live); LastToken = token; return Task.CompletedTask; }
        public Task StopAsync(CancellationToken token) { Live = 0; return Task.CompletedTask; }
        public void FailSource() => SafetyFault?.Invoke(this, new InvalidOperationException("Original source lost."));
    }
}
