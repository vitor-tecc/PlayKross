using JKiller.Windows.Viewing;
using JKiller.Windows.Presentation;
using JKiller.Contracts;
using Xunit;
namespace JKiller.Windows.Tests;
public sealed class WebViewPolicyTests
{
    [Fact]
    public void UntrustedNavigationCannotCallNativeActions()
    {
        var policy = new RoomWebViewPolicy(new("https://room.test"));
        foreach (var url in new[] { "https://evil.test", "https://room.test.evil.test", "https://room.test:444", "http://room.test", "file:///C:/file", "javascript:alert(1)", "https://user@room.test" })
        { Assert.False(policy.AllowsNavigation(new(url))); Assert.False(policy.CanInvokeNative(new(url))); }
        Assert.True(policy.AllowsNavigation(new("https://room.test/#invite=anything")));
        Assert.False(policy.CanInvokeNative(new("https://room.test")));
    }
    [Fact]
    public void PopupCannotInheritBridgeOrMicrophonePermission()
    { var policy = new RoomWebViewPolicy(new("http://localhost:5000")); Assert.False(policy.AllowsPopup); Assert.False(policy.AllowsMediaPermission); }
    [Fact]
    public void BroadcastersOwnPlaybackCanBeExplicitlySelectedForAudio()
    {
        foreach (var image in new[] { @"C:\Apps\JKiller.App.exe", @"C:\Apps\PlayKross.exe", @"C:\Apps\obs64.exe" })
            Assert.True(BroadcasterModel.AudioEligibilityFor(new("view", SourceKind.Window, 1, new(1234, 5678, image), "Room playback")).Allowed);
    }
}
