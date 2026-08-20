using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Domain.ValueObjects;

namespace SelfCheckoutKiosk.Core.Currency;

/// <summary>Thrown when <see cref="DualCurrencyCalculator.CalculateChange"/> is
/// given a missing, zero, negative, or otherwise unusable USD→KHR rate. The
/// calculator never silently substitutes a default rate — a bad rate must
/// hard-stop the cash-change path, not under/over-dispense.</summary>
public sealed class InvalidExchangeRateException(decimal usdToKhrRate)
    : Exception($"USD to KHR exchange rate '{usdToKhrRate}' is invalid (missing, stale, zero, or negative). " +
        "Refusing to calculate change rather than risk mis-dispensing.")
{
    public decimal UsdToKhrRate { get; } = usdToKhrRate;
}

/// <summary>
/// Dual-currency change logic (Blueprint §3): USD is the ledger currency;
/// whole-dollar overpayment dispenses as USD notes, the sub-dollar remainder
/// converts to KHR at the kiosk's cached offline rate and is ALWAYS rounded
/// DOWN to the nearest dispensable KHR note — the kiosk must never promise
/// money it does not physically hold.
///
/// Sprint-0 policy: the discarded round-down remainder (&lt; 100 KHR) is not
/// tracked here — the caller (LLCoreLogicEngine) is responsible for logging it
/// to the transaction audit trail. Rate staleness (max age, per-txn rate
/// versioning) is the caller's concern too; this type is a pure function of
/// the rate it is given.
/// </summary>
public sealed class DualCurrencyCalculator
{
    /// <summary>USD note denominations the cash recycler can dispense, largest first.</summary>
    private static readonly int[] UsdDenominations = [20, 10, 5, 1];

    /// <summary>KHR note denominations the cash recycler can dispense, largest first.
    /// 100 KHR is the smallest circulating note — the effective rounding step.</summary>
    private static readonly int[] KhrDenominations = [100_000, 50_000, 20_000, 10_000, 5_000, 1_000, 500, 100];

    private const int KhrRoundingStep = 100;

    /// <summary>
    /// Converts a USD total to KHR using the Cambodian retail rounding rule:
    /// Any fractional remainder between 1 and 99 KHR (e.g. 50 KHR) is ALWAYS rounded
    /// UP to the nearest 100 KHR.
    /// Example: $1.01 * 4100 = 4141 KHR -> 4200 KHR.
    /// Example: $1.00 * 4100 = 4100 KHR -> 4100 KHR.
    /// </summary>
    public static decimal CalculateTotalKhr(decimal totalUsd, decimal usdToKhrRate)
    {
        if (totalUsd <= 0) return 0m;
        if (usdToKhrRate <= 0) throw new InvalidExchangeRateException(usdToKhrRate);

        decimal rawKhr = totalUsd * usdToKhrRate;
        return Math.Ceiling(rawKhr / KhrRoundingStep) * KhrRoundingStep;
    }

    /// <summary>
    /// Rounds any raw KHR amount UP to the nearest 100 KHR (Cambodian retail pricing rule).
    /// </summary>
    public static decimal RoundUpToNearest100Khr(decimal rawKhr)
    {
        if (rawKhr <= 0) return 0m;
        return Math.Ceiling(rawKhr / KhrRoundingStep) * KhrRoundingStep;
    }

    /// <summary>
    /// Step 1: remaining balance owed = max(0, total - tendered). If tendered
    /// hasn't covered the total yet there is no change to dispense.
    /// Step 2: overpayment = max(0, tendered - total) splits into a whole-dollar
    /// USD portion and a sub-dollar fractional portion.
    /// Step 3: the fractional USD remainder converts to KHR at
    /// <paramref name="usdToKhrRate"/> and rounds DOWN to the nearest
    /// dispensable KHR denomination before being broken into notes.
    /// </summary>
    public ChangeBreakdown CalculateChange(decimal totalUsd, decimal tenderedUsd, decimal usdToKhrRate)
    {
        if (totalUsd < 0) throw new ArgumentOutOfRangeException(nameof(totalUsd), "Total cannot be negative.");
        if (tenderedUsd < 0) throw new ArgumentOutOfRangeException(nameof(tenderedUsd), "Tendered amount cannot be negative.");
        if (usdToKhrRate <= 0) throw new InvalidExchangeRateException(usdToKhrRate);

        // Step 1: balance still owed (informational — zero here means change is due).
        decimal remainingOwedUsd = Math.Max(0m, totalUsd - tenderedUsd);
        if (remainingOwedUsd > 0)
            return ChangeBreakdown.Empty;

        // Step 2: split the overpayment.
        decimal overpaidUsd = tenderedUsd - totalUsd;
        if (overpaidUsd <= 0)
            return ChangeBreakdown.Empty;

        decimal wholeDollarsUsd = Math.Floor(overpaidUsd);
        decimal subDollarUsd = overpaidUsd - wholeDollarsUsd;

        var usdNotes = BreakdownIntoNotes((long)wholeDollarsUsd, UsdDenominations, CurrencyCode.Usd);

        // Step 3: convert the sub-dollar remainder to KHR, rounded DOWN to the
        // nearest dispensable note before splitting into denominations.
        decimal khrRaw = subDollarUsd * usdToKhrRate;
        long khrRoundedDown = (long)Math.Floor(khrRaw / KhrRoundingStep) * KhrRoundingStep;
        decimal discardedRemainder = khrRaw - khrRoundedDown;

        var khrNotes = BreakdownIntoNotes(khrRoundedDown, KhrDenominations, CurrencyCode.Khr);

        return new ChangeBreakdown(usdNotes, khrNotes, discardedRemainder);
    }

    private static IReadOnlyList<KeyValuePair<Money, int>> BreakdownIntoNotes(
        long amount, IReadOnlyList<int> denominationsDescending, CurrencyCode currency)
    {
        if (amount <= 0)
            return [];

        var notes = new List<KeyValuePair<Money, int>>();
        long remaining = amount;
        foreach (int denomination in denominationsDescending)
        {
            int count = (int)(remaining / denomination);
            if (count <= 0) continue;

            Money denominationMoney = currency == CurrencyCode.Usd ? Money.Usd(denomination) : Money.Khr(denomination);
            notes.Add(new KeyValuePair<Money, int>(denominationMoney, count));
            remaining -= (long)count * denomination;
        }

        // Denomination sets bottom out at 1 (USD) / 100 (KHR — the rounding
        // step), so a physically dispensable amount always fully decomposes.
        return notes;
    }
}
