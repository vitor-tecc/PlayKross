using PlayKross.Windows.Presentation;
using PlayKross.Windows.Capture;
using PlayKross.Windows.Obs;
using PlayKross.Windows.Rooms;
using PlayKross.Windows.Security;
using PlayKross.Windows.Viewing;
namespace PlayKross.App;
public sealed class MainWindowViewModel(string root, RoomHttpClient room, PlaybackAudioGate playbackAudioGate) : BroadcasterModel(
    fallback => new ObsMediaEngine(new OwnedObsProcess(root), fallback, playbackAudioGate), room, new WindowsSourceCatalog(), new SessionVault(Path.Combine(root, ".runtime/user-session")), new WindowsAudioCatalog());
