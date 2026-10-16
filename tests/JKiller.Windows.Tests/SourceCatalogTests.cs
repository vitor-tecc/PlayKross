using JKiller.Contracts;
using JKiller.Windows.Capture;
using Xunit;
namespace JKiller.Windows.Tests;

public sealed class SourceCatalogTests
{
    [Fact]
    public async Task DuplicateUnicodeTitlesDoNotBecomeIdentity()
    {
        var catalog = new WindowsSourceCatalog(() => [new(1, 42, "ゲーム 🎮", true), new(2, 43, "ゲーム 🎮", true)], () => [], pid => new(pid, 1000, "C:\\game.exe"));
        var sources = await catalog.ListSourcesAsync(default);
        Assert.Equal(2, sources.Count);
        Assert.NotEqual(sources[0].Id, sources[1].Id);
        Assert.Equal("ゲーム 🎮", sources[0].DisplayName);
    }
    [Fact]
    public async Task HiddenAndDisappearingWindowsCannotAcquireAudioIdentity()
    {
        var catalog = new WindowsSourceCatalog(() => [new(1, 42, "Hidden", false), new(2, 43, "Gone", true)], () => [new(3, "Display")], _ => null);
        var sources = await catalog.ListSourcesAsync(default);
        Assert.Equal(2, sources.Count);
        Assert.DoesNotContain(sources, item => item.DisplayName == "Hidden");
        Assert.All(sources, item => Assert.Null(item.Process));
    }
    [Fact]
    public void RealProcessIdentityMatchesThisProcessLifetime()
    {
        var identity = ProcessIdentityReader.Read(Environment.ProcessId);
        Assert.NotNull(identity);
        Assert.Equal(Environment.ProcessId, identity.Pid);
        Assert.True(identity.CreationFileTime > 0);
        Assert.True(File.Exists(identity.ExecutablePath));
        Assert.Equal(identity, ProcessIdentityReader.Read(Environment.ProcessId));
        Assert.Null(ProcessIdentityReader.Read(int.MaxValue));
    }
}
