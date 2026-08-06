using SelfCheckoutKiosk.Domain.ValueObjects;

namespace SelfCheckoutKiosk.Core.Currency;

/// <summary>
/// Accumulates USD and KHR note insertions toward a single transaction total
/// (Blueprint §3: mixed-currency payment flow).
///
/// Use case — customer pays a $2.00 product with $1 USD + 4 100 KHR:
/// <code>
///   var session = new MixedPaymentAccumulator(totalUsd: 2.00m, calculator);
///   session.AccumulateNote(Money.Usd(1.00m));   // remaining = $1.00
///   session.AccumulateNote(Money.Khr(4_100m));  // remaining = $0.00
///   if (session.IsComplete)
///       var change = session.CalculateChange();  // ChangeBreakdown.Empty
/// </code>
///
/// Thread safety: NOT thread-safe — call from the engine's event handler only
/// (single-threaded cash processing path, Blueprint §4).
/// </summary>
public sealed class MixedPaymentAccumulator
{
    // -----------------------------------------------------------------------
    // Dependencies
    // -----------------------------------------------------------------------
    private readonly DualCurrencyCalculator _calculator;
    private readonly decimal               _totalUsd;
    private readonly decimal               _usdToKhrRate;

    // -----------------------------------------------------------------------
    // Running state
    // -----------------------------------------------------------------------
    private decimal _accumulatedUsdEquivalent;

    // -----------------------------------------------------------------------
    // Construction
    // -----------------------------------------------------------------------
    /// <summary>
    /// Creates a new accumulator for one transaction.
    /// </summary>
    /// <param name="totalUsd">Amount due in USD (e.g. 2.00m for a $2 product).</param>
    /// <param name="calculator">Shared <see cref="DualCurrencyCalculator"/> instance.</param>
    /// <param name="usdToKhrRate">
    ///   Rate for this session. Defaults to
    ///   <see cref="DualCurrencyCalculator.DefaultUsdToKhrRate"/> (4 100).
    /// </param>
    public MixedPaymentAccumulator(
        decimal                totalUsd,
        DualCurrencyCalculator calculator,
        decimal                usdToKhrRate = DualCurrencyCalculator.DefaultUsdToKhrRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(totalUsd);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(usdToKhrRate);

        _totalUsd     = totalUsd;
        _calculator   = calculator;
        _usdToKhrRate = usdToKhrRate;
    }

    // -----------------------------------------------------------------------
    // State queries
    // -----------------------------------------------------------------------
    /// <summary>Transaction total in USD.</summary>
    public decimal TotalUsd => _totalUsd;

    /// <summary>Running total of all notes tendered, expressed in USD equivalent.</summary>
    public decimal AccumulatedUsdEquivalent => _accumulatedUsdEquivalent;

    /// <summary>Remaining amount the customer still needs to tender, in USD.</summary>
    public decimal RemainingUsd
        => Math.Max(0m, _totalUsd - _accumulatedUsdEquivalent);

    /// <summary>
    /// True when the accumulated payment covers the total.
    /// Call <see cref="CalculateChange"/> immediately after this becomes true.
    /// </summary>
    public bool IsComplete => _accumulatedUsdEquivalent >= _totalUsd;

    // -----------------------------------------------------------------------
    // Payment accumulation
    // -----------------------------------------------------------------------
    /// <summary>
    /// Adds one note (USD or KHR) to the running payment total.
    /// The note is converted to its USD equivalent using the session rate.
    /// Call once per <see cref="Core.Abstractions.ICashRecycler.OnNoteInEscrow"/> event.
    /// </summary>
    /// <param name="note">The inserted note (USD or KHR denomination).</param>
    public void AccumulateNote(Money note)
    {
        _accumulatedUsdEquivalent +=
            DualCurrencyCalculator.ToUsdEquivalent(note, _usdToKhrRate);
    }

    // -----------------------------------------------------------------------
    // Change
    // -----------------------------------------------------------------------
    /// <summary>
    /// Calculates the change breakdown after payment is complete.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///   Thrown when called before <see cref="IsComplete"/> is true.
    /// </exception>
    public ChangeBreakdown CalculateChange()
    {
        if (!IsComplete)
            throw new InvalidOperationException(
                $"Payment not complete — remaining: {RemainingUsd:F2} USD. " +
                "AccumulateNote() must be called until IsComplete is true.");

        return _calculator.CalculateChange(_totalUsd, _accumulatedUsdEquivalent, _usdToKhrRate);
    }

    // -----------------------------------------------------------------------
    // Rollback (used by engine when a note must be rejected)
    // -----------------------------------------------------------------------
    /// <summary>
    /// Reverses the effect of a previous <see cref="AccumulateNote"/> call for
    /// the given note. Used by <c>LLCoreLogicEngine</c> when a speculatively
    /// accumulated note is found to cause an over-500 KHR overpayment and must
    /// be physically returned to the customer.
    ///
    /// Only call this immediately after the AccumulateNote that you want to
    /// undo, while the note is still in escrow (before it is committed or rejected).
    /// </summary>
    /// <param name="note">The note to remove from the running total.</param>
    public void RollBackLastNote(Money note)
    {
        _accumulatedUsdEquivalent -=
            DualCurrencyCalculator.ToUsdEquivalent(note, _usdToKhrRate);

        // Clamp to zero to guard against floating-point edge cases.
        if (_accumulatedUsdEquivalent < 0m)
            _accumulatedUsdEquivalent = 0m;
    }

    // -----------------------------------------------------------------------
    // Reset (reuse across transactions if engine recycles the instance)
    // -----------------------------------------------------------------------
    /// <summary>Resets the accumulator for a new transaction with a different total.</summary>
    public void Reset(decimal newTotalUsd)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(newTotalUsd);
        _accumulatedUsdEquivalent = 0m;
    }
}

