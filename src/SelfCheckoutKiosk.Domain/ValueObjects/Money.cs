using SelfCheckoutKiosk.Domain.Enums;

namespace SelfCheckoutKiosk.Domain.ValueObjects;

/// <summary>Immutable currency-tagged amount. USD is the ledger currency.</summary>
public readonly record struct Money(decimal Amount, CurrencyCode Currency)
{
    public static Money Usd(decimal amount) => new(amount, CurrencyCode.Usd);
    public static Money Khr(decimal amount) => new(amount, CurrencyCode.Khr);

    public Money Add(Money other)
    {
        if (Currency != other.Currency)
            throw new InvalidOperationException($"Cannot add money of different currencies: {Currency} and {other.Currency}");
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        if (Currency != other.Currency)
            throw new InvalidOperationException($"Cannot subtract money of different currencies: {Currency} and {other.Currency}");
        return new Money(Amount - other.Amount, Currency);
    }

    public static Money operator +(Money left, Money right) => left.Add(right);
    public static Money operator -(Money left, Money right) => left.Subtract(right);
}

/// <summary>Per-denomination change breakdown to be dispensed (Blueprint §3).</summary>
/// <param name="DiscardedKhrRemainder">The sub-100-KHR fraction discarded by
/// always-round-down conversion — never dispensed, but not silently dropped
/// either: the caller books it as a "kiosk float gain" audit entry.</param>
public sealed record ChangeBreakdown(
    IReadOnlyList<KeyValuePair<Money, int>> UsdNotes,
    IReadOnlyList<KeyValuePair<Money, int>> KhrNotes,
    decimal DiscardedKhrRemainder = 0m)
{
    public static ChangeBreakdown Empty { get; } = new([], []);
}
