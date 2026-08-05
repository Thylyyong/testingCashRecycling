namespace SelfCheckoutKiosk.Core.Currency;

/// <summary>
/// Low-float safeguard (Blueprint §4). Tracks live per-denomination KHR note
/// counts updated from cassette-inventory reads and dispense events.
///
/// State machine (two states — Normal / LowFloat):
///   Normal  → LowFloat : fires <see cref="LowFloatStateTriggered"/> the first
///             time any tracked denomination drops below <see cref="LowFloatThreshold"/>.
///   LowFloat → Normal  : fires <see cref="LowFloatStateCleared"/> once ALL
///             previously-below-threshold denominations recover.
///   No re-entrancy: transitioning from LowFloat to LowFloat (another denomination
///   drops) does NOT fire a second Triggered event.
///
/// Thread safety: <see cref="UpdateCount"/> is called from the hardware event
/// thread; events are raised OUTSIDE the lock to avoid deadlocks with callers
/// that subscribe and call back into this class.
///
/// TODO(Lead): make LowFloatThreshold configurable per denomination (Blueprint §4
/// hard-codes 15 uniformly; that is a review finding for Sprint 1).
/// </summary>
public sealed class LowFloatMonitor
{
    // -----------------------------------------------------------------------
    // Blueprint §4: threshold = 15 per denomination (uniform for Sprint 0).
    // -----------------------------------------------------------------------
    public const int LowFloatThreshold = 15;

    // -----------------------------------------------------------------------
    // Events
    // -----------------------------------------------------------------------
    /// <summary>
    /// Raised when the first denomination drops below <see cref="LowFloatThreshold"/>.
    /// Engine reaction (Blueprint §4): call StopAcceptingCashAsync and show lock screen.
    /// </summary>
    public event EventHandler? LowFloatStateTriggered;

    /// <summary>
    /// Raised when ALL previously-below-threshold denominations recover to or
    /// above <see cref="LowFloatThreshold"/> (e.g. after a manual cassette reload).
    /// </summary>
    public event EventHandler? LowFloatStateCleared;

    // -----------------------------------------------------------------------
    // State (guarded by _lock)
    // -----------------------------------------------------------------------
    private readonly object _lock = new();
    private readonly Dictionary<int, int> _counts = new();
    private bool _isLowFloat;

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// <summary>
    /// Updates the count for one KHR denomination and evaluates whether the
    /// low-float state should be entered or exited.
    /// </summary>
    /// <param name="denominationKhr">KHR denomination value (e.g. 100, 500, 1000 …).</param>
    /// <param name="count">Current note count in the cassette for this denomination.</param>
    public void UpdateCount(int denominationKhr, int count)
    {
        bool shouldTrigger = false;
        bool shouldClear   = false;

        lock (_lock)
        {
            _counts[denominationKhr] = count;

            // Is ANY denomination currently below threshold?
            var anyBelow = false;
            foreach (var kvp in _counts)
            {
                if (kvp.Value < LowFloatThreshold)
                {
                    anyBelow = true;
                    break;
                }
            }

            if (anyBelow && !_isLowFloat)
            {
                _isLowFloat   = true;
                shouldTrigger = true;
            }
            else if (!anyBelow && _isLowFloat)
            {
                _isLowFloat = false;
                shouldClear = true;
            }
        }

        // Fire events OUTSIDE the lock to prevent deadlocks.
        if (shouldTrigger) LowFloatStateTriggered?.Invoke(this, EventArgs.Empty);
        if (shouldClear)   LowFloatStateCleared?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Returns a snapshot of all tracked denomination counts.</summary>
    public IReadOnlyDictionary<int, int> GetAllCounts()
    {
        lock (_lock)
        {
            return new Dictionary<int, int>(_counts);
        }
    }
}
