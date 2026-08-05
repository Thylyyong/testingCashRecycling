using System;
using System.Collections.Generic;

namespace SelfCheckoutKiosk.Core.Currency;

/// <summary>
/// Tracks live KHR note counts by denomination.
///
/// The monitor enters low-float state when any tracked denomination
/// contains fewer than <see cref="LowFloatThreshold"/> notes.
///
/// It leaves low-float state only after every tracked denomination
/// has recovered to the threshold or higher.
///
/// This class only tracks state and publishes events. It does not
/// communicate with cash-recycler hardware directly.
/// </summary>
public sealed class LowFloatMonitor
{
    /// <summary>
    /// Minimum safe note count for every tracked KHR denomination.
    ///
    /// A count below this value activates low-float state.
    /// </summary>
    public const int LowFloatThreshold =
        15;

    private readonly object
        _syncRoot =
            new();

    private readonly Dictionary<int, int>
        _countsByDenomination =
            new();

    private bool
        _isLowFloat;

    /// <summary>
    /// Raised once when the monitor changes from normal state
    /// to low-float state.
    /// </summary>
    public event EventHandler?
        LowFloatStateTriggered;

    /// <summary>
    /// Raised once when all tracked denominations recover and
    /// the monitor changes from low-float state to normal state.
    /// </summary>
    public event EventHandler?
        LowFloatStateCleared;

    /// <summary>
    /// Indicates whether any tracked denomination currently
    /// contains fewer than 15 notes.
    /// </summary>
    public bool IsLowFloat
    {
        get
        {
            lock (_syncRoot)
            {
                return _isLowFloat;
            }
        }
    }

    /// <summary>
    /// Updates the live count for one KHR denomination.
    /// </summary>
    /// <param name="denominationKhr">
    /// Positive whole-KHR note denomination, such as 100,
    /// 500, 1,000, or 5,000.
    /// </param>
    /// <param name="count">
    /// Current number of notes available for that denomination.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the denomination is not positive or the count
    /// is negative.
    /// </exception>
    public void UpdateCount(
        int denominationKhr,
        int count)
    {
        if (denominationKhr <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(denominationKhr),
                denominationKhr,
                "The KHR denomination must be greater than zero."
            );
        }

        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count),
                count,
                "The note count cannot be negative."
            );
        }

        var shouldRaiseTriggered =
            false;

        var shouldRaiseCleared =
            false;

        lock (_syncRoot)
        {
            _countsByDenomination[
                denominationKhr
            ] = count;

            var shouldBeLowFloat =
                HasLowDenomination();

            /*
             * Events describe state transitions, not every inventory
             * update.
             *
             * Normal -> Low:
             * raise LowFloatStateTriggered once.
             *
             * Low -> Normal:
             * raise LowFloatStateCleared once.
             */
            if (
                shouldBeLowFloat ==
                _isLowFloat
            )
            {
                return;
            }

            _isLowFloat =
                shouldBeLowFloat;

            shouldRaiseTriggered =
                shouldBeLowFloat;

            shouldRaiseCleared =
                !shouldBeLowFloat;
        }

        /*
         * Raise events outside the lock so subscribers cannot block
         * inventory updates or create a lock-related deadlock.
         */
        if (shouldRaiseTriggered)
        {
            LowFloatStateTriggered?.Invoke(
                this,
                EventArgs.Empty
            );
        }

        if (shouldRaiseCleared)
        {
            LowFloatStateCleared?.Invoke(
                this,
                EventArgs.Empty
            );
        }
    }

    /// <summary>
    /// Returns the last known count for a tracked denomination.
    /// </summary>
    /// <returns>
    /// The current count, or <see langword="null"/> when the
    /// denomination has not been tracked yet.
    /// </returns>
    public int? GetCount(
        int denominationKhr)
    {
        if (denominationKhr <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(denominationKhr),
                denominationKhr,
                "The KHR denomination must be greater than zero."
            );
        }

        lock (_syncRoot)
        {
            if (
                _countsByDenomination.TryGetValue(
                    denominationKhr,
                    out var count
                )
            )
            {
                return count;
            }

            return null;
        }
    }

    private bool HasLowDenomination()
    {
        foreach (
            var count in
            _countsByDenomination.Values
        )
        {
            if (
                count <
                LowFloatThreshold
            )
            {
                return true;
            }
        }

        return false;
    }
}