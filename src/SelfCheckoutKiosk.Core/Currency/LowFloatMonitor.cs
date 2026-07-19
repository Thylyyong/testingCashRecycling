namespace SelfCheckoutKiosk.Core.Currency;

// TODO(Back-End): remove once the monitor raises these from real cassette reads.
#pragma warning disable CS0067 // Event is declared but never raised (stub)

/// <summary>
/// STUB — low-float safeguard (Blueprint §4). Tracks live per-denomination KHR
/// note counts; when any tracked denomination drops below its threshold it
/// raises <see cref="LowFloatStateTriggered"/>, and the engine reacts by
/// halting cash intake and showing the exact-cash / digital-only lock screen.
///
/// TODO(Back-End): make the threshold PER-DENOMINATION and configurable
/// (Blueprint §4 hard-codes 15 uniformly — that is a review finding to fix).
/// </summary>
public sealed class LowFloatMonitor
{
    public event EventHandler? LowFloatStateTriggered;
    public event EventHandler? LowFloatStateCleared;

    public void UpdateCount(int denominationKhr, int count)
        => throw new NotImplementedException("TODO(Back-End): implement per-denomination threshold logic.");
}
