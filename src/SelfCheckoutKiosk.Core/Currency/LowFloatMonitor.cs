using System.Threading;

namespace SelfCheckoutKiosk.Core.Currency;

/// <summary>
/// Low-float safeguard (Blueprint §4). Tracks live per-denomination KHR note
/// counts reported by the cash recycler; when ANY tracked denomination drops
/// below its threshold the monitor is "low" and raises
/// <see cref="LowFloatStateTriggered"/> — the engine reacts by halting cash
/// intake / routing to the exact-cash-only lockout. When every denomination
/// recovers above its threshold it raises <see cref="LowFloatStateCleared"/>.
///
/// Thresholds are per-denomination and configurable (a Sprint-0 review finding
/// against the blueprint's uniform hard-coded "15") — pass
/// <paramref name="thresholdsByDenomination"/> to override the
/// <paramref name="defaultThreshold"/> for specific KHR denominations.
/// </summary>
public sealed class LowFloatMonitor
{
    private readonly int _defaultThreshold;
    private readonly Dictionary<int, int> _thresholdsByDenomination;
    private readonly Dictionary<int, int> _countsByDenomination = [];
    private readonly HashSet<int> _lowDenominations = [];
    private readonly Lock _gate = new();

    public LowFloatMonitor(int defaultThreshold = 15, IReadOnlyDictionary<int, int>? thresholdsByDenomination = null)
    {
        if (defaultThreshold < 0)
            throw new ArgumentOutOfRangeException(nameof(defaultThreshold), "Threshold cannot be negative.");

        _defaultThreshold = defaultThreshold;
        _thresholdsByDenomination = thresholdsByDenomination is null
            ? []
            : new Dictionary<int, int>(thresholdsByDenomination);
    }

    public event EventHandler? LowFloatStateTriggered;
    public event EventHandler? LowFloatStateCleared;

    /// <summary>True while at least one tracked denomination is below its threshold.</summary>
    public bool IsLow
    {
        get { lock (_gate) return _lowDenominations.Count > 0; }
    }

    public IReadOnlySet<int> LowDenominations
    {
        get { lock (_gate) return new HashSet<int>(_lowDenominations); }
    }

    /// <summary>Snapshot of the last-reported physical note count per tracked
    /// denomination — feeds <c>AdminDiagnosticsSnapshot.KhrCassetteCounts</c>.</summary>
    public IReadOnlyDictionary<int, int> CurrentCounts
    {
        get { lock (_gate) return new Dictionary<int, int>(_countsByDenomination); }
    }

    /// <summary>Reports the current physical note count for a denomination.
    /// Called by the engine every time the cash recycler's cassette state changes.</summary>
    public void UpdateCount(int denominationKhr, int count)
    {
        if (denominationKhr <= 0)
            throw new ArgumentOutOfRangeException(nameof(denominationKhr), "Denomination must be positive.");
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Count cannot be negative.");

        bool wasLow;
        bool isLow;
        lock (_gate)
        {
            wasLow = _lowDenominations.Count > 0;

            _countsByDenomination[denominationKhr] = count;
            int threshold = _thresholdsByDenomination.GetValueOrDefault(denominationKhr, _defaultThreshold);

            if (count < threshold)
                _lowDenominations.Add(denominationKhr);
            else
                _lowDenominations.Remove(denominationKhr);

            isLow = _lowDenominations.Count > 0;
        }

        if (!wasLow && isLow)
            LowFloatStateTriggered?.Invoke(this, EventArgs.Empty);
        else if (wasLow && !isLow)
            LowFloatStateCleared?.Invoke(this, EventArgs.Empty);
    }
}
