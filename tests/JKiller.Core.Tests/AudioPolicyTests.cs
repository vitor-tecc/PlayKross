using JKiller.Contracts;
using JKiller.Core.Capture;
using Xunit;
namespace JKiller.Core.Tests;

public sealed class AudioPolicyTests
{
    private static readonly ProcessIdentity Original = new(42, 1000, "C:\\Games\\game.exe");
    private static readonly SourceDescriptor Window = new("window:1:42:1000", SourceKind.Window, 1, Original, "Game 🎮");
    [Theory]
    [InlineData(ProcessClassification.Discord)]
    [InlineData(ProcessClassification.Browser)]
    [InlineData(ProcessClassification.SharedHost)]
    [InlineData(ProcessClassification.OwnProcessTree)]
    [InlineData(ProcessClassification.Unknown)]
    public void OnlyVerifiedApplicationAudioIsEligible(ProcessClassification classification)
    {
        var result = AudioPolicy.Evaluate(Window, classification);
        Assert.False(result.Allowed);
        Assert.NotEmpty(result.Reason);
    }
    [Fact]
    public void KnownApplicationWithoutEnforcedTreeBoundaryIsVideoOnly() => Assert.False(AudioPolicy.Evaluate(Window, ProcessClassification.VerifiedApplication).Allowed);
    [Fact]
    public void MonitorSelectionDoesNotEnableAudio() => Assert.False(AudioPolicy.Evaluate(new("monitor:1", SourceKind.Monitor, 1, null, "Display"), ProcessClassification.VerifiedApplication).Allowed);
    [Fact]
    public void DeniedProcessInspectionIsNotEligible() => Assert.False(AudioPolicy.Evaluate(Window with { Process = null }, ProcessClassification.VerifiedApplication).Allowed);
    [Fact]
    public void ClosedOrReusedProcessIsNotSameSelection()
    {
        Assert.False(AudioPolicy.IsSameProcess(Original, null));
        Assert.False(AudioPolicy.IsSameProcess(Original, Original with { CreationFileTime = 1001 }));
        Assert.False(AudioPolicy.IsSameProcess(Original, Original with { ExecutablePath = "C:\\Apps\\Discord.exe" }));
        Assert.True(AudioPolicy.IsSameProcess(Original, Original with { ExecutablePath = "c:\\games\\GAME.exe" }));
    }
}
