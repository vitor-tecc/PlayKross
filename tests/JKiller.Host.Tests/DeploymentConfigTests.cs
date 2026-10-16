using Xunit;
namespace JKiller.Host.Tests;
public sealed class DeploymentConfigTests
{
    private static string ReadProfile()
    {
        for (var root = new DirectoryInfo(AppContext.BaseDirectory); root is not null; root = root.Parent)
            if (File.Exists(Path.Combine(root.FullName, "config/dependencies.json"))) return File.ReadAllText(Path.Combine(root.FullName, "config/mediamtx.public.yml"));
        throw new Exception("Repository root missing.");
    }
    [Fact]
    public void PublicProfileLeavesAdminPrivate()
    {
        var profile = ReadProfile(); Assert.Contains("apiAddress: 127.0.0.1:9997", profile); Assert.Contains("metricsAddress: 127.0.0.1:9998", profile);
        Assert.Contains("webrtcAddress: 127.0.0.1:8889", profile); Assert.Contains("authHTTPAddress: http://127.0.0.1:5001/internal/media-auth", profile);
        Assert.Contains("webrtcLocalUDPAddress: :8189", profile);
    }
    [Fact]
    public void PublicProfileHasNoAnonymousMediaAccess()
    {
        var profile = ReadProfile(); Assert.Contains("authMethod: http", profile); Assert.Contains("authInternalUsers: []", profile); Assert.Contains("authHTTPExclude: []", profile);
        Assert.DoesNotContain("\"*\"", profile); Assert.Contains("record: false", profile); Assert.Contains("writeQueueSize: 512", profile);
        foreach (var protocol in new[] { "rtsp", "rtmp", "hls", "srt", "moq", "playback", "pprof" }) Assert.Contains(protocol + ": false", profile);
    }
}
