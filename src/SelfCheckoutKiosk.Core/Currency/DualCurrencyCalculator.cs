using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Domain.ValueObjects;

namespace SelfCheckoutKiosk.Core.Currency;

/// <summary>
/// Dual-currency change calculator (Blueprint §3).
///
/// RATE POLICY — no public API (Blueprint §4 offline-first):
///   1 USD = 4 100 KHR (hardcoded operational rate).
///   The rate parameter on each method allows per-transaction override
///   if the Lead later configures a cached rate from the sync worker.
///
/// CHANGE ALGORITHM (Blueprint §3):
///   1. Whole-dollar overpayment → dispensed as USD notes, largest first.
///   2. Sub-dollar remainder × rate → floored to the nearest 100 KHR
///      (smallest dispensable KHR denomination) then dispensed as KHR notes.
///   Any remainder below 100 KHR is absorbed — never returned (Blueprint §3).
///
/// MIXED PAYMENT:
///   Use <see cref="MixedPaymentAccumulator"/> to accumulate USD + KHR notes
///   across multiple note insertions before calling CalculateChange.
/// </summary>
public sealed class DualCurrencyCalculator
{
    // -----------------------------------------------------------------------
    // Rate — hardcoded, no public API (Blueprint §4)
    // -----------------------------------------------------------------------
    /// <summary>
    /// Operational exchange rate: 1 USD = 4 100 KHR.
    /// Hardcoded — no live API, offline-first kiosk (Blueprint §4).
    /// Lead may configure a per-transaction override via the sync worker cache.
    /// </summary>
    public const decimal DefaultUsdToKhrRate = 4_100m;

    // -----------------------------------------------------------------------
    // Dispensable denominations (descending — greedy largest-first)
    // -----------------------------------------------------------------------
    /// <summary>USD note denominations the recycler can dispense (whole dollars).</summary>
    public static readonly IReadOnlyList<int> UsdDenominations = [100, 50, 20, 10, 5, 1];

    /// <summary>KHR note denominations the recycler can dispense.</summary>
    public static readonly IReadOnlyList<int> KhrDenominations =
        [100_000, 50_000, 10_000, 5_000, 2_000, 1_000, 500, 100];

    // -----------------------------------------------------------------------
    // Conversion helpers
    // -----------------------------------------------------------------------
    /// <summary>
    /// Converts any Money note to its USD equivalent at the given rate.
    /// USD notes are returned at face value; KHR notes are divided by the rate.
    /// </summary>
    public static decimal ToUsdEquivalent(
        Money note,
        decimal usdToKhrRate = DefaultUsdToKhrRate)
        => note.Currency == CurrencyCode.Usd
            ? note.Amount
            : note.Amount / usdToKhrRate;

    // -----------------------------------------------------------------------
    // Change calculation
    // -----------------------------------------------------------------------
    /// <summary>
    /// Calculates the change breakdown after a completed payment.
    /// </summary>
    /// <param name="totalUsd">Transaction total in USD.</param>
    /// <param name="tenderedUsd">Total tendered expressed in USD equivalent.</param>
    /// <param name="usdToKhrRate">
    ///   Exchange rate for this transaction. Defaults to <see cref="DefaultUsdToKhrRate"/>.
    /// </param>
    /// <exception cref="ArgumentException">
    ///   Thrown when <paramref name="tenderedUsd"/> is less than <paramref name="totalUsd"/>.
    /// </exception>
    public ChangeBreakdown CalculateChange(
        decimal totalUsd,
        decimal tenderedUsd,
        decimal usdToKhrRate = DefaultUsdToKhrRate)
    {
        if (tenderedUsd < totalUsd)
            throw new ArgumentException(
                $"Tendered {tenderedUsd:F2} USD is less than total {totalUsd:F2} USD.",
                nameof(tenderedUsd));

        var overpaymentUsd = tenderedUsd - totalUsd;

        // Step 1 — whole-dollar change in USD
        var wholeUsd = Math.Floor(overpaymentUsd);

        // Step 2 — sub-dollar remainder → KHR, always round down to nearest 100 KHR
        var remainderUsd  = overpaymentUsd - wholeUsd;
        var rawKhr        = remainderUsd * usdToKhrRate;
        var dispensableKhr = FloorToNearest100((int)rawKhr);   // Blueprint §3: always round down

        var usdNotes = BuildNotes((int)wholeUsd, UsdDenominations, CurrencyCode.Usd);
        var khrNotes = BuildNotes(dispensableKhr, KhrDenominations, CurrencyCode.Khr);

        return new ChangeBreakdown(usdNotes, khrNotes);
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------
    /// <summary>
    /// Floors <paramref name="amount"/> to the nearest multiple of 100
    /// (smallest dispensable KHR denomination — Blueprint §3).
    /// </summary>
    private static int FloorToNearest100(int amount) => (amount / 100) * 100;

    /// <summary>
    /// Greedy denomination breakdown. Returns largest-first note counts
    /// until <paramref name="amount"/> is exhausted or denominations run out.
    /// </summary>
    private static List<KeyValuePair<Money, int>> BuildNotes(
        int amount,
        IReadOnlyList<int> denominations,
        CurrencyCode currency)
    {
        var result    = new List<KeyValuePair<Money, int>>();
        var remaining = amount;

        foreach (var denom in denominations)
        {
            if (remaining <= 0) break;
            var count = remaining / denom;
            if (count > 0)
            {
                result.Add(KeyValuePair.Create(new Money(denom, currency), count));
                remaining -= count * denom;
            }
        }

        return result;
    }
}
