using JKiller.Windows.Presentation;
using JKiller.Windows.Capture;
using JKiller.Windows.Obs;
using JKiller.Windows.Rooms;
using JKiller.Windows.Security;
using JKiller.Windows.Viewing;
namespace JKiller.App;
public sealed class MainWindowViewModel(string root, RoomHttpClient room, PlaybackAudioGate playbackAudioGate) : BroadcasterModel(
    fallback => new ObsMediaEngine(new OwnedObsProcess(root), fallback, playbackAudioGate), room, new WindowsSourceCatalog(), new SessionVault(Path.Combine(root, ".runtime/user-session")), new WindowsAudioCatalog());
