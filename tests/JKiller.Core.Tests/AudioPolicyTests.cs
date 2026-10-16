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
    public void ExplicitKnownApplicationCanUseExperimentalTreeAudio()
    {
        var result = AudioPolicy.Evaluate(Window, ProcessClassification.VerifiedApplication);
        Assert.True(result.Allowed); Assert.Contains("child", result.Reason);
    }
    [Fact]
    public void MonitorSelectionDoesNotEnableAudio() => Assert.False(AudioPolicy.Evaluate(new("monitor:1", SourceKind.Monitor, 1, null, "Display"), ProcessClassification.VerifiedApplication).Allowed);
    [Fact]
    public void DeniedProcessInspectionIsNotEligible() => Assert.False(AudioPolicy.Evaluate(Window with { Process = null }, ProcessClassification.VerifiedApplication).Allowed);
    [Theory]
    [InlineData("C:/Steam/steamapps/common/Geometry Dash/GeometryDash.exe", true)]
    [InlineData("C:/Other/GeometryDash.exe", true)]
    [InlineData("C:/Discord.exe", false)]
    [InlineData("C:/msedgewebview2.exe", false)]
    [InlineData("C:/PlayKross.exe", false)]
    [InlineData("C:/Game.exe", true)]
    public void SupportedAudioProfileIsIndependentOfUi(string image, bool allowed) =>
        Assert.Equal(allowed, AudioPolicy.EvaluateApplication(Original with { ExecutablePath = image }).Allowed);

    public static TheoryData<AudioSelection> ContradictorySelections => new()
    {
        new(AudioMode.Off, Original),
        new(AudioMode.Off, RenderEndpointId: "speakers"),
        new(AudioMode.Application),
        new(AudioMode.Application, Original with { Pid = 0 }),
        new(AudioMode.Application, Original with { CreationFileTime = 0 }),
        new(AudioMode.Application, Original with { ExecutablePath = "" }),
        new(AudioMode.Application, Original with { ExecutablePath = "   " }),
        new(AudioMode.Application, Original, "speakers"),
        new(AudioMode.SystemOutput),
        new(AudioMode.SystemOutput, RenderEndpointId: ""),
        new(AudioMode.SystemOutput, RenderEndpointId: "   "),
        new(AudioMode.SystemOutput, RenderEndpointId: "default"),
        new(AudioMode.SystemOutput, RenderEndpointId: " DEFAULT "),
        new(AudioMode.SystemOutput, Original, "speakers"),
        new((AudioMode)999)
    };

    [Theory]
    [MemberData(nameof(ContradictorySelections))]
    public void SelectionRejectsContradictoryFields(AudioSelection audio)
    {
        var result = AudioPolicy.EvaluateSelection(audio, SourceKind.Monitor);
        Assert.False(result.Allowed);
        Assert.NotEmpty(result.Reason);
    }

    [Fact]
    public void SystemOutputRequiresMonitor()
    {
        var audio = new AudioSelection(AudioMode.SystemOutput, RenderEndpointId: "{render-speakers}");
        Assert.False(AudioPolicy.EvaluateSelection(audio, SourceKind.Window).Allowed);
        var result = AudioPolicy.EvaluateSelection(audio, SourceKind.Monitor);
        Assert.True(result.Allowed);
        Assert.Contains("Discord", result.Reason);
        Assert.Contains("playback", result.Reason);
    }

    [Theory]
    [InlineData(SourceKind.Window)]
    [InlineData(SourceKind.Monitor)]
    public void StandaloneAppIsEligible(SourceKind videoKind)
    {
        var result = AudioPolicy.EvaluateSelection(new(AudioMode.Application, Original), videoKind);
        Assert.True(result.Allowed);
        Assert.Contains("child", result.Reason);
    }

    [Theory]
    [InlineData("C:/Apps/DISCORDcanary.exe")]
    [InlineData("C:/Apps/chrome.exe")]
    [InlineData("C:\\Apps\\MSEDGE.exe")]
    [InlineData("C:/Apps/msedgewebview2.exe")]
    [InlineData("C:/Apps/firefox.exe")]
    [InlineData("C:/Apps/brave.exe")]
    [InlineData("C:/Apps/opera.exe")]
    [InlineData("C:/Apps/chromium.exe")]
    [InlineData("C:/Apps/steamwebhelper.exe")]
    [InlineData("C:/Windows/explorer.exe")]
    [InlineData("C:/Windows/rundll32.exe")]
    [InlineData("C:/Windows/dllhost.exe")]
    [InlineData("C:/Apps/PlayKross.exe")]
    [InlineData("C:/Apps/JKiller.App.exe")]
    [InlineData("C:/Apps/obs64.exe")]
    public void ExcludedNativeHostsRemainIneligible(string image) =>
        Assert.False(AudioPolicy.EvaluateSelection(new(AudioMode.Application, Original with { ExecutablePath = image }), SourceKind.Window).Allowed);

    [Fact]
    public void OwnerCannotSelectItselfUnderAnUnrecognizedImageName() =>
        Assert.False(AudioPolicy.EvaluateSelection(new(AudioMode.Application, Original), SourceKind.Window, Original.Pid).Allowed);

    [Fact]
    public void OffAndCompatibilityConstructorDoNotImplicitlySelectSystemAudio()
    {
        Assert.True(AudioPolicy.EvaluateSelection(AudioSelection.Off, SourceKind.Monitor).Allowed);
        Assert.Equal(AudioMode.Off, new AudioSelection((ProcessIdentity?)null).Mode);
        Assert.Equal(AudioMode.Application, new AudioSelection(Original).Mode);
        Assert.Null(new AudioSelection(Original).RenderEndpointId);
    }
    [Fact]
    public void ClosedOrReusedProcessIsNotSameSelection()
    {
        Assert.False(AudioPolicy.IsSameProcess(Original, null));
        Assert.False(AudioPolicy.IsSameProcess(Original, Original with { CreationFileTime = 1001 }));
        Assert.False(AudioPolicy.IsSameProcess(Original, Original with { ExecutablePath = "C:\\Apps\\Discord.exe" }));
        Assert.True(AudioPolicy.IsSameProcess(Original, Original with { ExecutablePath = "c:\\games\\GAME.exe" }));
    }
}
