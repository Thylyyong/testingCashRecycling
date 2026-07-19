using SelfCheckoutKiosk.Domain.ValueObjects;

namespace SelfCheckoutKiosk.Core.Currency;

/// <summary>
/// STUB — dual-currency change logic (Blueprint §3): USD ledger, whole-dollar
/// overpayment dispensed as USD notes, sub-dollar remainder converted to KHR
/// at the cached rate and ALWAYS rounded DOWN to a dispensable denomination.
///
/// TODO(Back-End): implement and unit-test exhaustively. Sprint-0 policy calls
/// to settle here: (a) where the round-down remainder goes + its audit entry;
/// (b) cached-rate staleness handling (max age, stale behaviour, per-txn rate
/// versioning). No live rate exists on an offline kiosk.
/// </summary>
public sealed class DualCurrencyCalculator
{
    public ChangeBreakdown CalculateChange(decimal totalUsd, decimal tenderedUsd, decimal usdToKhrRate)
        => throw new NotImplementedException("TODO(Back-End): implement per Blueprint §3.");
}
