using JKiller.Windows.Capture;
using Xunit;
namespace JKiller.Windows.Tests;

public sealed class AudioCatalogTests
{
    private static readonly AudioEndpointObservation[] Endpoints =
    [
        new("{speakers}", "Speakers 🔊", AudioEndpointFlow.Render, AudioEndpointState.Active),
        new("{headset}", "Headset", AudioEndpointFlow.Render, AudioEndpointState.Active),
        new("{microphone}", "Microphone", AudioEndpointFlow.Capture, AudioEndpointState.Active),
        new("{disabled}", "Disabled", AudioEndpointFlow.Render, AudioEndpointState.Disabled),
        new("{unplugged}", "Unplugged", AudioEndpointFlow.Render, AudioEndpointState.Unplugged),
        new("{missing}", "Missing", AudioEndpointFlow.Render, AudioEndpointState.NotPresent)
    ];

    [Fact]
    public async Task OnlyActiveRenderEndpointsHaveConcreteIdsAndFriendlyNames()
    {
        IWindowsAudioCatalog catalog = new WindowsAudioCatalog(_ => new(Endpoints, "{headset}"));
        var outputs = await catalog.ListOutputsAsync(default);
        Assert.Equal(new[] { "{speakers}", "{headset}" }, outputs.Select(output => output.Id));
        Assert.Equal("Speakers 🔊", outputs[0].DisplayName);
        Assert.False(outputs[0].IsDefault);
        Assert.True(outputs[1].IsDefault);
        var resolved = await catalog.ResolveDefaultAsync(default);
        Assert.Equal(outputs[1], resolved);
        Assert.NotEqual("default", resolved!.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{gone}")]
    [InlineData("{disabled}")]
    [InlineData("{microphone}")]
    public async Task MissingOrIneligibleDefaultDoesNotChooseAnotherDevice(string? defaultId)
    {
        var catalog = new WindowsAudioCatalog(_ => new(Endpoints, defaultId));
        Assert.Null(await catalog.ResolveDefaultAsync(default));
        Assert.All(await catalog.ListOutputsAsync(default), output => Assert.False(output.IsDefault));
    }

    [Fact]
    public async Task DefaultSwitchKeepsPreviouslySelectedEndpointIdStable()
    {
        var defaultId = "{speakers}";
        var catalog = new WindowsAudioCatalog(_ => new(Endpoints, defaultId));
        var selected = (await catalog.ResolveDefaultAsync(default))!;
        defaultId = "{headset}";
        Assert.Equal("{headset}", (await catalog.ResolveDefaultAsync(default))!.Id);
        var outputs = await catalog.ListOutputsAsync(default);
        Assert.Equal("{speakers}", selected.Id);
        Assert.Contains(outputs, output => output.Id == selected.Id && !output.IsDefault);
    }

    [Fact]
    public async Task MissingFriendlyNameFallsBackToConcreteEndpointId()
    {
        var catalog = new WindowsAudioCatalog(_ => new(
            [new("{speakers}", null, AudioEndpointFlow.Render, AudioEndpointState.Active)], "{speakers}"));
        Assert.Equal("{speakers}", (await catalog.ResolveDefaultAsync(default))!.DisplayName);
    }

    [Fact]
    public async Task CancellationBeforeObservationDoesNotTouchDeviceProvider()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var catalog = new WindowsAudioCatalog(_ => throw new InvalidOperationException("Provider must not run."));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => catalog.ListOutputsAsync(cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => catalog.ResolveDefaultAsync(cancelled.Token));
    }

    [Fact]
    public async Task CancellationDuringObservationDoesNotPublishPartialDeviceList()
    {
        using var cancelled = new CancellationTokenSource();
        var catalog = new WindowsAudioCatalog(token =>
        {
            Assert.Equal(cancelled.Token, token);
            cancelled.Cancel();
            return new(Endpoints, "{speakers}");
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => catalog.ListOutputsAsync(cancelled.Token));
    }
}
