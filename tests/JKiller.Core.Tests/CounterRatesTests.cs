using JKiller.Core.Diagnostics;
using Xunit;
namespace JKiller.Core.Tests;
public sealed class CounterRatesTests
{
    [Fact] public void ResetDoesNotProduceNegativeRate() => Assert.Null(CounterRates.Calculate(500, 10, TimeSpan.FromSeconds(1)));
    [Fact] public void RateUsesMeasuredInterval() => Assert.Equal(60d, CounterRates.Calculate(0, 120, TimeSpan.FromSeconds(2)));
    [Theory] [InlineData(0)] [InlineData(-1)] public void InvalidIntervalHasNoRate(int milliseconds) => Assert.Null(CounterRates.Calculate(0, 1, TimeSpan.FromMilliseconds(milliseconds)));
    [Fact] public void NegativeCounterIsUnavailable() => Assert.Null(CounterRates.Calculate(-1, 100, TimeSpan.FromSeconds(1)));
}
