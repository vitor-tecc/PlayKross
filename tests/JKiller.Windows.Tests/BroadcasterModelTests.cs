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
        model.Audio = new("browser", SourceKind.Window, 2, new(3, 4, "C:/msedge.exe"), "Discord in browser");
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
    private sealed class Room : IRoomClient
    {
        public Task<SessionGrant> JoinAsync(Uri uri, string invite, string nickname, CancellationToken token) => Task.FromResult(new SessionGrant(Guid.NewGuid(), Guid.NewGuid(), "session", DateTimeOffset.UtcNow.AddHours(1), new(Guid.NewGuid(), 1, [], [])));
        public Task<PublishReservation> ReservePublishAsync(CancellationToken token) => Task.FromResult(new PublishReservation(Guid.NewGuid(), "test", new("/media/test/whip", UriKind.Relative), "media", DateTimeOffset.UtcNow.AddMinutes(1)));
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
