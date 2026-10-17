using System.Security.Cryptography;
using System.Text;

namespace JKiller.Windows.Viewing;
public static class WebViewProfilePath
{
    public static string ForRoom(string localApplicationData, string instanceRoot, Uri origin)
    {
        if (string.IsNullOrWhiteSpace(localApplicationData) || !Path.IsPathFullyQualified(localApplicationData))
            throw new InvalidOperationException("Windows user application data is unavailable. Sign in with a writable user profile to open the viewer.");
        // Portable ZIPs can be extracted under deeply nested paths. Chromium adds
        // its own directory tree, so keep browser data outside the installation.
        // An installation+origin hash keeps concurrent portable instances isolated.
        var installation = Path.GetFullPath(instanceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
        var identity = installation + "\n" + origin.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..32];
        return Path.Combine(localApplicationData, "PlayKross", "WebView2", key);
    }
}
