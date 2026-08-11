using System;
using SelfCheckoutKiosk.Core.Currency;
using Xunit;

namespace SelfCheckoutKiosk.Core.Tests;

public sealed class LowFloatMonitorTests
{
    [Fact]
    public void LowFloatThreshold_IsFifteen()
    {
        Assert.Equal(
            15,
            LowFloatMonitor.LowFloatThreshold
        );
    }

    [Fact]
    public void NewMonitor_StartsInNormalState()
    {
        var monitor =
            new LowFloatMonitor();

        Assert.False(
            monitor.IsLowFloat
        );
    }

    [Fact]
    public void UpdateCount_AtThreshold_DoesNotTriggerLowFloat()
    {
        var monitor =
            new LowFloatMonitor();

        var triggeredCount =
            0;

        monitor.LowFloatStateTriggered +=
            (_, _) =>
                triggeredCount++;

        monitor.UpdateCount(
            1000,
            LowFloatMonitor.LowFloatThreshold
        );

        Assert.False(
            monitor.IsLowFloat
        );

        Assert.Equal(
            0,
            triggeredCount
        );

        Assert.Equal(
            LowFloatMonitor.LowFloatThreshold,
            monitor.GetCount(
                1000
            )
        );
    }

    [Fact]
    public void UpdateCount_BelowThreshold_TriggersLowFloat()
    {
        var monitor =
            new LowFloatMonitor();

        object? eventSender =
            null;

        var triggeredCount =
            0;

        monitor.LowFloatStateTriggered +=
            (sender, _) =>
            {
                eventSender =
                    sender;

                triggeredCount++;
            };

        monitor.UpdateCount(
            1000,
            LowFloatMonitor.LowFloatThreshold - 1
        );

        Assert.True(
            monitor.IsLowFloat
        );

        Assert.Equal(
            1,
            triggeredCount
        );

        Assert.Same(
            monitor,
            eventSender
        );
    }

    [Fact]
    public void UpdateCount_WhileAlreadyLow_DoesNotTriggerRepeatedEvent()
    {
        var monitor =
            new LowFloatMonitor();

        var triggeredCount =
            0;

        monitor.LowFloatStateTriggered +=
            (_, _) =>
                triggeredCount++;

        monitor.UpdateCount(
            1000,
            14
        );

        monitor.UpdateCount(
            1000,
            10
        );

        monitor.UpdateCount(
            500,
            5
        );

        Assert.True(
            monitor.IsLowFloat
        );

        Assert.Equal(
            1,
            triggeredCount
        );
    }

    [Fact]
    public void UpdateCount_WhenAllDenominationsRecover_ClearsLowFloat()
    {
        var monitor =
            new LowFloatMonitor();

        var clearedCount =
            0;

        monitor.LowFloatStateCleared +=
            (_, _) =>
                clearedCount++;

        monitor.UpdateCount(
            1000,
            10
        );

        Assert.True(
            monitor.IsLowFloat
        );

        monitor.UpdateCount(
            1000,
            15
        );

        Assert.False(
            monitor.IsLowFloat
        );

        Assert.Equal(
            1,
            clearedCount
        );
    }

    [Fact]
    public void UpdateCount_WhenOneDenominationRemainsLow_DoesNotClearState()
    {
        var monitor =
            new LowFloatMonitor();

        var clearedCount =
            0;

        monitor.LowFloatStateCleared +=
            (_, _) =>
                clearedCount++;

        monitor.UpdateCount(
            1000,
            10
        );

        monitor.UpdateCount(
            500,
            5
        );

        monitor.UpdateCount(
            1000,
            15
        );

        Assert.True(
            monitor.IsLowFloat
        );

        Assert.Equal(
            0,
            clearedCount
        );

        monitor.UpdateCount(
            500,
            15
        );

        Assert.False(
            monitor.IsLowFloat
        );

        Assert.Equal(
            1,
            clearedCount
        );
    }

    [Fact]
    public void UpdateCount_AfterClear_CanTriggerLowFloatAgain()
    {
        var monitor =
            new LowFloatMonitor();

        var triggeredCount =
            0;

        var clearedCount =
            0;

        monitor.LowFloatStateTriggered +=
            (_, _) =>
                triggeredCount++;

        monitor.LowFloatStateCleared +=
            (_, _) =>
                clearedCount++;

        monitor.UpdateCount(
            1000,
            14
        );

        monitor.UpdateCount(
            1000,
            15
        );

        monitor.UpdateCount(
            1000,
            14
        );

        Assert.True(
            monitor.IsLowFloat
        );

        Assert.Equal(
            2,
            triggeredCount
        );

        Assert.Equal(
            1,
            clearedCount
        );
    }

    [Fact]
    public void UpdateCount_StoresLatestCountForEachDenomination()
    {
        var monitor =
            new LowFloatMonitor();

        monitor.UpdateCount(
            1000,
            20
        );

        monitor.UpdateCount(
            500,
            30
        );

        monitor.UpdateCount(
            1000,
            18
        );

        Assert.Equal(
            18,
            monitor.GetCount(
                1000
            )
        );

        Assert.Equal(
            30,
            monitor.GetCount(
                500
            )
        );
    }

    [Fact]
    public void GetCount_WhenDenominationIsNotTracked_ReturnsNull()
    {
        var monitor =
            new LowFloatMonitor();

        var count =
            monitor.GetCount(
                1000
            );

        Assert.Null(
            count
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void UpdateCount_WhenDenominationIsNotPositive_Throws(
        int denominationKhr)
    {
        var monitor =
            new LowFloatMonitor();

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                monitor.UpdateCount(
                    denominationKhr,
                    15
                )
        );
    }

    [Fact]
    public void UpdateCount_WhenCountIsNegative_Throws()
    {
        var monitor =
            new LowFloatMonitor();

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                monitor.UpdateCount(
                    1000,
                    -1
                )
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GetCount_WhenDenominationIsNotPositive_Throws(
        int denominationKhr)
    {
        var monitor =
            new LowFloatMonitor();

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                monitor.GetCount(
                    denominationKhr
                )
        );
    }

    [Fact]
    public void UpdateCount_ZeroNotes_TriggersLowFloat()
    {
        var monitor =
            new LowFloatMonitor();

        var triggeredCount =
            0;

        monitor.LowFloatStateTriggered +=
            (_, _) =>
                triggeredCount++;

        monitor.UpdateCount(
            1000,
            0
        );

        Assert.True(
            monitor.IsLowFloat
        );

        Assert.Equal(
            1,
            triggeredCount
        );

        Assert.Equal(
            0,
            monitor.GetCount(
                1000
            )
        );
    }
}
