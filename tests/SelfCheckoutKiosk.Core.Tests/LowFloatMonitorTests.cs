using SelfCheckoutKiosk.Core.Currency;
using Xunit;

namespace SelfCheckoutKiosk.Core.Tests;

public sealed class LowFloatMonitorTests
{
    [Fact]
    public void UpdateCount_DropsBelowDefaultThreshold_RaisesTriggered()
    {
        var monitor = new LowFloatMonitor(defaultThreshold: 15);
        int triggeredCount = 0;
        monitor.LowFloatStateTriggered += (_, _) => triggeredCount++;

        monitor.UpdateCount(1000, 20);
        Assert.False(monitor.IsLow);
        Assert.Equal(0, triggeredCount);

        monitor.UpdateCount(1000, 10);
        Assert.True(monitor.IsLow);
        Assert.Equal(1, triggeredCount);
        Assert.Contains(1000, monitor.LowDenominations);
    }

    [Fact]
    public void UpdateCount_RecoversAboveThreshold_RaisesCleared()
    {
        var monitor = new LowFloatMonitor(defaultThreshold: 15);
        int clearedCount = 0;
        monitor.LowFloatStateCleared += (_, _) => clearedCount++;

        monitor.UpdateCount(1000, 10); // trips low
        monitor.UpdateCount(1000, 16); // recovers
        Assert.False(monitor.IsLow);
        Assert.Equal(1, clearedCount);
    }

    [Fact]
    public void UpdateCount_PerDenominationOverride_UsesCustomThreshold()
    {
        var monitor = new LowFloatMonitor(defaultThreshold: 15,
            thresholdsByDenomination: new Dictionary<int, int> { [100_000] = 3 });

        monitor.UpdateCount(100_000, 5); // above custom threshold (3), below default (15)
        Assert.False(monitor.IsLow);

        monitor.UpdateCount(100_000, 2);
        Assert.True(monitor.IsLow);
    }

    [Fact]
    public void IsLow_StaysTrueUntilEveryTrackedDenominationRecovers()
    {
        var monitor = new LowFloatMonitor(defaultThreshold: 15);

        monitor.UpdateCount(1000, 10);
        monitor.UpdateCount(500, 10);
        Assert.True(monitor.IsLow);

        monitor.UpdateCount(1000, 20); // one recovers, other still low
        Assert.True(monitor.IsLow);

        monitor.UpdateCount(500, 20); // both recovered
        Assert.False(monitor.IsLow);
    }

    [Fact]
    public void CurrentCounts_ReflectsLastReportedCounts()
    {
        var monitor = new LowFloatMonitor();
        monitor.UpdateCount(1000, 42);
        monitor.UpdateCount(500, 7);

        var counts = monitor.CurrentCounts;
        Assert.Equal(42, counts[1000]);
        Assert.Equal(7, counts[500]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void UpdateCount_NonPositiveDenomination_Throws(int denomination)
    {
        var monitor = new LowFloatMonitor();
        Assert.Throws<ArgumentOutOfRangeException>(() => monitor.UpdateCount(denomination, 10));
    }

    [Fact]
    public void UpdateCount_NegativeCount_Throws()
    {
        var monitor = new LowFloatMonitor();
        Assert.Throws<ArgumentOutOfRangeException>(() => monitor.UpdateCount(1000, -1));
    }

    [Fact]
    public void Constructor_NegativeDefaultThreshold_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new LowFloatMonitor(defaultThreshold: -1));
}
