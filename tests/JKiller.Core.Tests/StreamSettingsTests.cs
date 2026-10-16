using JKiller.Contracts;
using JKiller.Core.Settings;
using Xunit;
namespace JKiller.Core.Tests;

public sealed class StreamSettingsTests
{
    // Catches wrong dimensions/default rate and confusing megabits with bytes/kilobits.
    [Theory]
    [InlineData(480, 30, 854, 2)]
    [InlineData(480, 60, 854, 3)]
    [InlineData(720, 30, 1280, 4)]
    [InlineData(720, 60, 1280, 6)]
    [InlineData(1080, 30, 1920, 8)]
    [InlineData(1080, 60, 1920, 15)]
    public void PresetValuesAndMbpsConversion(int height, int fps, int width, int mbps)
    {
        var actual = StreamSettings.DefaultFor(height, fps);
        Assert.Equal(width, actual.Width);
        Assert.Equal(height, actual.Height);
        Assert.Equal(fps, actual.Fps);
        Assert.Equal(mbps, actual.BitrateMbps);
        Assert.Equal(mbps * 1000, actual.EncoderKbps);
        Assert.True(StreamSettings.Validate(actual).IsValid);
    }

    [Theory]
    [InlineData(1921, 1080, 60, "15")]
    [InlineData(1920, 1080, 59, "15")]
    [InlineData(1920, 1080, 60, "0.5")]
    [InlineData(1920, 1080, 60, "25.5")]
    [InlineData(1920, 1080, 60, "1.25")]
    public void RejectsUnsupportedOrNonFiniteSettings(int width, int height, int fps, string bitrate)
    {
        var result = StreamSettings.Validate(new(width, height, fps, decimal.Parse(bitrate, System.Globalization.CultureInfo.InvariantCulture)));
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("1.5")]
    [InlineData("25")]
    public void AcceptsSliderBoundaryAndHalfStep(string bitrate) => Assert.True(StreamSettings.Validate(new(1920, 1080, 60, decimal.Parse(bitrate, System.Globalization.CultureInfo.InvariantCulture))).IsValid);

    [Theory]
    [InlineData("{\"width\":1920,\"height\":1080,\"fps\":60,\"bitrateMbps\":\"NaN\"}")]
    [InlineData("{\"width\":1920,\"height\":1080,\"fps\":60,\"bitrateMbps\":1e99}")]
    [InlineData("{\"width\":1920,\"height\":1080,\"fps\":60,\"bitrateMbps\":1.25}")]
    [InlineData("null")]
    [InlineData("{broken}")]
    public void InvalidJsonCannotBecomeAppliedSettings(string json)
    {
        var result = StreamSettings.ParseJson(json);
        Assert.False(result.Validation.IsValid);
        Assert.Null(result.Settings);
    }

    [Fact]
    public void ValidJsonKeepsRequestedDecimalRate()
    {
        var result = StreamSettings.ParseJson("{\"width\":1920,\"height\":1080,\"fps\":60,\"bitrateMbps\":15.5}");
        Assert.True(result.Validation.IsValid);
        Assert.NotNull(result.Settings);
        Assert.Equal(15500, result.Settings!.EncoderKbps);
    }
}
