using JKiller.Windows.Viewing;
using Xunit;

namespace JKiller.Windows.Tests;
public sealed class WebViewProfilePathTests
{
    [Fact]
    public void LongPortableExtractionDoesNotLengthenBrowserDataPath()
    {
        var local = Path.Combine(Path.GetTempPath(), "profile-location-test");
        var instance = Path.Combine(@"D:\portable", new string('a', 110), new string('b', 110));
        var profile = WebViewProfilePath.ForRoom(local, instance, new Uri("https://room.test"));
        Assert.StartsWith(Path.Combine(local, "PlayKross", "WebView2") + Path.DirectorySeparatorChar, profile);
        Assert.True(profile.Length + @"\EBWebView\Default\Network\Cookies".Length < 240);
        Assert.DoesNotContain(new string('a', 110), profile);
        Assert.DoesNotContain("room.test", profile);
    }

    [Fact]
    public void ProfilesAreStableAndSeparateAcrossOriginsAndInstallations()
    {
        var local = Path.GetTempPath(); var origin = new Uri("https://room.test");
        var first = WebViewProfilePath.ForRoom(local, @"D:\portable\a", origin);
        Assert.Equal(first, WebViewProfilePath.ForRoom(local, @"D:\portable\a", origin));
        Assert.NotEqual(first, WebViewProfilePath.ForRoom(local, @"D:\portable\b", origin));
        Assert.NotEqual(first, WebViewProfilePath.ForRoom(local, @"D:\portable\a", new Uri("https://other.test")));
        Assert.NotEqual(first, WebViewProfilePath.ForRoom(local, @"D:\portable\a", new Uri("https://room.test:8443")));
    }
}
