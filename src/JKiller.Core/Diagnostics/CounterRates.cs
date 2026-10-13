namespace JKiller.Core.Diagnostics;
public static class CounterRates
{
    public static double? Calculate(long previous, long current, TimeSpan interval) =>
        previous < 0 || current < previous || interval <= TimeSpan.Zero ? null : (current - previous) / interval.TotalSeconds;
}
