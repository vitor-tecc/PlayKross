using JKiller.Contracts;
using JKiller.Core.Media;
using JKiller.Core.Rooms;
using JKiller.Windows.Capture;
using JKiller.Windows.Presentation;
using Xunit;
namespace JKiller.Windows.Tests;
public sealed class BroadcasterModelTests
{
    [Fact]
    public async Task SwitchToWindowClearsSystemSelection()
    {
        await using var model = new BroadcasterModel(_ => new Media(), new Room(), new Catalog(), audioCatalog: new AudioCatalog());
        model.Video = new("monitor", SourceKind.Monitor, 1, null, "Display");
        await model.RefreshAsync(CancellationToken.None);
        model.SelectSystemAudio(model.AudioOutputs[0]);
        model.Video = new("window", SourceKind.Window, 2, new(42, 100, "Game.exe"), "Game");
        Assert.Equal(AudioSelection.Off, model.Audio);
        Assert.Contains("off", model.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("display", model.Status, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task MissingEndpointRemainsSelectedAndInvalidUntilReselection()
    {
        var outputs = new AudioCatalog();
        await using var model = new BroadcasterModel(_ => new Media(), new Room(), new Catalog(), audioCatalog: outputs);
        model.Video = new("monitor", SourceKind.Monitor, 1, null, "Display");
        await model.RefreshAsync(CancellationToken.None); model.SelectSystemAudio(model.AudioOutputs[0]);
        outputs.Outputs = [new("other", "Other", true)];
        await model.RefreshAsync(CancellationToken.None);
        Assert.Equal("render:selected", model.Audio.RenderEndpointId); Assert.False(model.AudioEligibility.Allowed);
        model.SelectSystemAudio(model.AudioOutputs[0]); Assert.True(model.AudioEligibility.Allowed);
    }
    [Fact]
    public async Task SettingsRestartKeepsOriginalEndpoint()
    {
        var media = new Media();
        await using var model = new BroadcasterModel(_ => media, new Room(), new Catalog(), audioCatalog: new AudioCatalog()) { RoomLink = "http://localhost:5000/#invite=test" };
        await model.JoinAsync(CancellationToken.None);
        model.Video = new("monitor", SourceKind.Monitor, 1, null, "Display");
        model.SelectSystemAudio(model.AudioOutputs[0]); await model.StartAsync(CancellationToken.None);
        Assert.Throws<InvalidOperationException>(() => model.Audio = AudioSelection.Off);
        model.Settings = new(1280, 720, 30, 4); await model.ApplySettingsAsync(CancellationToken.None);
        Assert.Equal(2, media.Prepares); Assert.Equal("render:selected", media.Request!.Audio.RenderEndpointId);
    }
    [Fact]
    public async Task JoinedIdleAppRenewsPresenceWithoutStartingCapture()
    {
        var room = new Room(); var media = new Media();
        await using var model = new BroadcasterModel(_ => media, room, new Catalog()) { RoomLink = "http://localhost:5000/#invite=test" };
        await model.JoinAsync(CancellationToken.None);
        Assert.Equal(1, room.Heartbeats); Assert.Equal(0, media.Prepares);
    }
    [Fact]
    public async Task ClosingNativeAppReleasesItsRoomMembershipOnce()
    {
        var room = new Room();
        var model = new BroadcasterModel(_ => new Media(), room, new Catalog()) { RoomLink = "http://localhost:5000/#invite=test" };
        await model.JoinAsync(CancellationToken.None);
        await model.DisposeAsync(); await model.DisposeAsync();
        Assert.Equal(1, room.Leaves);
    }
    [Fact]
    public async Task RejectedRoomLinkDoesNotCancelTheExistingPresenceLease()
    {
        var room = new Room();
        await using var model = new BroadcasterModel(_ => new Media(), room, new Catalog()) { RoomLink = "http://localhost:5000/#invite=test" };
        await model.JoinAsync(CancellationToken.None); model.RoomLink = "invalid";
        await Assert.ThrowsAsync<ArgumentException>(() => model.JoinAsync(CancellationToken.None));
        await Task.Delay(TimeSpan.FromSeconds(6));
        Assert.True(model.IsJoined); Assert.True(room.Heartbeats >= 2);
    }
    [Fact]
    public async Task ExpiredPresenceCannotPreventCloseCleanup()
    {
        var room = new Room();
        var model = new BroadcasterModel(_ => new Media(), room, new Catalog()) { RoomLink = "http://localhost:5000/#invite=test" };
        await model.JoinAsync(CancellationToken.None); room.ExpirePresence = true;
        await Task.Delay(TimeSpan.FromSeconds(6));
        await model.DisposeAsync(); Assert.Equal(1, room.Leaves);
    }
    [Fact]
    public async Task CaptureCannotStartBeforeJoiningAndExplicitVideoSelection()
    {
        var media = new Media(); var model = new BroadcasterModel(_ => media, new Room(), new Catalog());
        await Assert.ThrowsAsync<InvalidOperationException>(() => model.StartAsync(CancellationToken.None));
        model.RoomLink = "http://localhost:5000/#invite=test"; await model.JoinAsync(CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => model.StartAsync(CancellationToken.None));
        Assert.Equal(0, media.Prepares); await model.DisposeAsync();
    }
    [Fact]
    public async Task BrowserAudioIsRejectedEvenWhenScreenVideoIsAllowed()
    {
        var media = new Media(); var model = new BroadcasterModel(_ => media, new Room(), new Catalog()) { RoomLink = "http://localhost:5000/#invite=test" };
        await model.JoinAsync(CancellationToken.None);
        model.Video = new("monitor", SourceKind.Monitor, 1, null, "Display");
        model.SelectApplicationAudio(new("browser", SourceKind.Window, 2, new(3, 4, "C:/msedge.exe"), "Discord in browser"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => model.StartAsync(CancellationToken.None));
        Assert.Equal(0, media.Prepares); await model.DisposeAsync();
    }
    [Fact]
    public async Task VideoOnlySelectionHasNoAudioAndUsesExplicitCpuFallbackChoice()
    {
        var media = new Media(); bool? fallback = null;
        var model = new BroadcasterModel(chosen => { fallback = chosen; return media; }, new Room(), new Catalog()) { RoomLink = "http://localhost:5000/#invite=test", AllowCpuFallback = true };
        await model.JoinAsync(CancellationToken.None);
        model.Video = new("monitor", SourceKind.Monitor, 1, null, "Display");
        await model.StartAsync(CancellationToken.None);
        Assert.True(fallback); Assert.Null(media.Request!.Audio.Application); await model.DisposeAsync();
    }
    private sealed class Catalog : IWindowsSourceCatalog { public Task<IReadOnlyList<SourceDescriptor>> ListSourcesAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<SourceDescriptor>>([]); }
    private sealed class AudioCatalog : IWindowsAudioCatalog
    {
        public IReadOnlyList<AudioOutputDescriptor> Outputs = [new("render:selected", "Speakers", true)];
        public Task<IReadOnlyList<AudioOutputDescriptor>> ListOutputsAsync(CancellationToken token) => Task.FromResult(Outputs);
        public Task<AudioOutputDescriptor?> ResolveDefaultAsync(CancellationToken token) => Task.FromResult(Outputs.FirstOrDefault(x => x.IsDefault));
    }
    private sealed class Room : IRoomClient
    {
        private readonly Guid streamId = Guid.NewGuid();
        public int Heartbeats, Leaves;
        public bool ExpirePresence;
        public Task HeartbeatAsync(CancellationToken token) { Interlocked.Increment(ref Heartbeats); if (ExpirePresence) throw new InvalidOperationException("Session expired."); return Task.CompletedTask; }
        public Task LeaveAsync(CancellationToken token) { Interlocked.Increment(ref Leaves); return Task.CompletedTask; }
        public Task RenewPublishAsync(Guid id, CancellationToken token) => Task.CompletedTask;
        public Task<SessionGrant> JoinAsync(Uri uri, string invite, string nickname, CancellationToken token) => Task.FromResult(new SessionGrant(Guid.NewGuid(), Guid.NewGuid(), "session", DateTimeOffset.UtcNow.AddHours(1), new(Guid.NewGuid(), 1, [], [])));
        public Task<PublishReservation> ReservePublishAsync(CancellationToken token) => Task.FromResult(new PublishReservation(streamId, "test", new("/media/test/whip", UriKind.Relative), "media", DateTimeOffset.UtcNow.AddMinutes(1)));
        public Task StopPublishAsync(Guid id, CancellationToken token) => Task.CompletedTask;
        public Task<RoomSnapshot> RenameAsync(string nickname, CancellationToken token) => throw new NotSupportedException();
        public Task<PlaybackGrant> GetPlaybackAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Media : IMediaEngine
    {
        public event EventHandler<Exception>? SafetyFault { add { } remove { } }
        public int Prepares; public BroadcastRequest? Request;
        public Task<AppliedMediaSettings> PrepareAsync(BroadcastRequest request, CancellationToken token) { Prepares++; Request = request; return Task.FromResult(new AppliedMediaSettings(request.Settings, "test", "h264", false)); }
        public Task StartAsync(Uri uri, string credential, CancellationToken token) => Task.CompletedTask;
        public Task StopAsync(CancellationToken token) => Task.CompletedTask;
    }
}
