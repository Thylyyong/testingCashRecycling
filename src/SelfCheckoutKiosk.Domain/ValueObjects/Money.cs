using SelfCheckoutKiosk.Domain.Enums;

namespace SelfCheckoutKiosk.Domain.ValueObjects;

/// <summary>Immutable currency-tagged amount. USD is the ledger currency.</summary>
public readonly record struct Money(decimal Amount, CurrencyCode Currency)
{
    public static Money Usd(decimal amount) => new(amount, CurrencyCode.Usd);
    public static Money Khr(decimal amount) => new(amount, CurrencyCode.Khr);
}

/// <summary>Per-denomination change breakdown to be dispensed (Blueprint §3).</summary>
public sealed record ChangeBreakdown(
    IReadOnlyList<KeyValuePair<Money, int>> UsdNotes,
    IReadOnlyList<KeyValuePair<Money, int>> KhrNotes)
{
    public static ChangeBreakdown Empty { get; } = new([], []);
}
