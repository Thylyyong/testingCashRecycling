using SelfCheckoutKiosk.Core.Currency;
using Xunit;

namespace SelfCheckoutKiosk.Core.Tests;

/// <summary>
/// Unit tests for <see cref="LowFloatMonitor"/> (Blueprint §4).
///
/// AI NOTE — before editing this file:
///   1. Re-read Blueprint §4 (low-float safeguard) and the pending tracker.
///   2. Do NOT add business logic to LowFloatMonitor itself; it only tracks
///      counts and fires events. Engine reaction is tested in CompositionSeamTests.
/// </summary>
public sealed class LowFloatMonitorTests
{
    // -----------------------------------------------------------------------
    // Trigger tests
    // -----------------------------------------------------------------------

    [Fact]
    public void UpdateCount_ExactlyAtThreshold_DoesNotTrigger()
    {
        var monitor = new LowFloatMonitor();
        var triggered = false;
        monitor.LowFloatStateTriggered += (_, _) => triggered = true;

        monitor.UpdateCount(1000, LowFloatMonitor.LowFloatThreshold); // == 15

        Assert.False(triggered, "Trigger must NOT fire when count equals the threshold.");
    }

    [Fact]
    public void UpdateCount_OneBelowThreshold_TriggersEvent()
    {
        var monitor = new LowFloatMonitor();
        var triggered = false;
        monitor.LowFloatStateTriggered += (_, _) => triggered = true;

        monitor.UpdateCount(1000, LowFloatMonitor.LowFloatThreshold - 1); // 14

        Assert.True(triggered, "Trigger must fire the first time any denomination drops below threshold.");
    }

    [Fact]
    public void UpdateCount_SecondDenominationDrops_DoesNotFireTriggerAgain()
    {
        // Once already in LowFloat state, a second denomination going below
        // threshold must NOT re-fire the triggered event (no re-entrancy).
        var monitor = new LowFloatMonitor();
        var triggerCount = 0;
        monitor.LowFloatStateTriggered += (_, _) => triggerCount++;

        monitor.UpdateCount(1000, 10); // triggers once → LowFloat
        monitor.UpdateCount(5000, 5);  // still LowFloat — no second trigger

        Assert.Equal(1, triggerCount);
    }

    // -----------------------------------------------------------------------
    // Cleared tests
    // -----------------------------------------------------------------------

    [Fact]
    public void UpdateCount_RecoveryAfterTrigger_FiresClearedEvent()
    {
        var monitor = new LowFloatMonitor();
        var cleared = false;
        monitor.LowFloatStateCleared += (_, _) => cleared = true;

        monitor.UpdateCount(1000, 10); // go below threshold
        monitor.UpdateCount(1000, LowFloatMonitor.LowFloatThreshold); // recover to exactly 15

        Assert.True(cleared, "Cleared must fire when the denomination recovers to or above threshold.");
    }

    [Fact]
    public void UpdateCount_MultiDenomination_ClearsOnlyWhenAllRecover()
    {
        // Both denominations go below. Cleared must not fire until BOTH recover.
        var monitor = new LowFloatMonitor();
        var clearedCount = 0;
        monitor.LowFloatStateCleared += (_, _) => clearedCount++;

        monitor.UpdateCount(1000, 10);  // below → LowFloat
        monitor.UpdateCount(5000, 5);   // also below — no state change

        monitor.UpdateCount(1000, 20);  // 1000 recovers — 5000 still below → NOT cleared yet
        Assert.Equal(0, clearedCount);

        monitor.UpdateCount(5000, 20);  // 5000 recovers — all clear now
        Assert.Equal(1, clearedCount);
    }

    [Fact]
    public void UpdateCount_NeverBelowThreshold_NeverFiresClearedEvent()
    {
        var monitor = new LowFloatMonitor();
        var cleared = false;
        monitor.LowFloatStateCleared += (_, _) => cleared = true;

        // All updates keep denominations at or above threshold.
        monitor.UpdateCount(1000, 15);
        monitor.UpdateCount(1000, 20);
        monitor.UpdateCount(5000, 100);

        Assert.False(cleared, "Cleared must never fire if LowFloat was never entered.");
    }

    // -----------------------------------------------------------------------
    // GetAllCounts snapshot
    // -----------------------------------------------------------------------

    [Fact]
    public void GetAllCounts_ReturnsCurrentSnapshot()
    {
        var monitor = new LowFloatMonitor();
        monitor.UpdateCount(1000, 10);
        monitor.UpdateCount(5000, 30);

        var counts = monitor.GetAllCounts();

        Assert.Equal(10, counts[1000]);
        Assert.Equal(30, counts[5000]);
    }

    [Fact]
    public void GetAllCounts_SnapshotIsIsolated_MutationDoesNotAffectMonitor()
    {
        var monitor = new LowFloatMonitor();
        monitor.UpdateCount(1000, 20);

        var snapshot = (Dictionary<int, int>)monitor.GetAllCounts();
        snapshot[1000] = 999; // mutate the returned copy

        // Monitor's internal state must be unchanged.
        Assert.Equal(20, monitor.GetAllCounts()[1000]);
    }
}
