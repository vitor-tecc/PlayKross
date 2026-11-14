using PlayKross.Contracts;
using PlayKross.Windows.Obs;
using Xunit;

namespace PlayKross.Windows.Tests;

public sealed class OwnedObsTests
{
    [Fact]
    public void NeverAttachesToPersonalObs()
    {
        var root = Path.Combine(Path.GetTempPath(), "playkross-test-root");
        var helper = new OwnedObsProcess(root);
        var launch = helper.BuildLaunch("playkross-test-pipe", new ProcessIdentity(123, 456, @"C:\JK App\PlayKross.exe"));
        Assert.Equal(Path.Combine(root, ".runtime", "obs", "bin", "64bit", "obs64.exe"), launch.FileName);
        Assert.Contains("--portable", launch.ArgumentList);
        Assert.Contains("--multi", launch.ArgumentList);
        Assert.DoesNotContain("--startstreaming", launch.ArgumentList);
        Assert.False(launch.UseShellExecute);
        Assert.True(launch.CreateNoWindow);
        Assert.Equal("123", launch.Environment["PLAYKROSS_OWNER_PID"]);
        Assert.Equal("456", launch.Environment["PLAYKROSS_OWNER_CREATION"]);
        Assert.Equal("playkross-test-pipe", launch.Environment["PLAYKROSS_CONTROL_PIPE"]);
        Assert.False(launch.Environment.ContainsKey("OBS_PLUGINS_PATH"));
        Assert.False(launch.Environment.ContainsKey("OBS_PLUGINS_DATA_PATH"));
    }

    [Fact]
    public async Task StopBeforeStartDoesNotSearchForOrKillOtherProcesses()
    {
        await using var helper = new OwnedObsProcess(Path.Combine(Path.GetTempPath(), "not-installed"));
        await helper.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void LaunchArgumentsCannotBecomeShellCommands()
    {
        var helper = new OwnedObsProcess(Path.Combine(Path.GetTempPath(), "jk root"));
        var owner = new ProcessIdentity(123, 456, @"C:\App & literal\owner.exe");
        var info = helper.BuildLaunch("safe-pipe", owner);
        Assert.Equal(owner.ExecutablePath, info.Environment["PLAYKROSS_OWNER_IMAGE"]);
        Assert.Empty(info.Arguments);
    }
}
