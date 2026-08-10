using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SelfCheckoutKiosk.Domain.Enums;

namespace SelfCheckoutKiosk.Domain.ValueObjects;

/// <summary>
/// Immutable currency-tagged amount.
///
/// USD is the kiosk ledger currency.
/// KHR is used for fractional-dollar change.
/// </summary>
public readonly record struct Money
{
    /// <summary>
    /// Creates a validated monetary value.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the amount is negative.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when the amount contains unsupported precision.
    /// </exception>
    public Money(
        decimal amount,
        CurrencyCode currency)
    {
        if (!Enum.IsDefined(currency))
        {
            throw new ArgumentOutOfRangeException(
                nameof(currency),
                currency,
                "Unsupported currency code."
            );
        }

        if (amount < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount,
                "Money amount cannot be negative."
            );
        }

        ValidatePrecision(
            amount,
            currency
        );

        Amount = amount;
        Currency = currency;
    }

    /// <summary>
    /// Numeric amount in the specified currency.
    /// </summary>
    public decimal Amount { get; }

    /// <summary>
    /// Currency associated with the amount.
    /// </summary>
    public CurrencyCode Currency { get; }

    /// <summary>
    /// Creates a USD amount.
    /// </summary>
    public static Money Usd(
        decimal amount)
    {
        return new Money(
            amount,
            CurrencyCode.Usd
        );
    }

    /// <summary>
    /// Creates a KHR amount.
    /// </summary>
    public static Money Khr(
        decimal amount)
    {
        return new Money(
            amount,
            CurrencyCode.Khr
        );
    }

    /// <summary>
    /// Adds two values that use the same currency.
    /// </summary>
    public Money Add(
        Money other)
    {
        EnsureSameCurrency(
            other
        );

        return new Money(
            Amount + other.Amount,
            Currency
        );
    }

    /// <summary>
    /// Subtracts one value from another using the same currency.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when currencies differ or the result would be negative.
    /// </exception>
    public Money Subtract(
        Money other)
    {
        EnsureSameCurrency(
            other
        );

        if (other.Amount > Amount)
        {
            throw new InvalidOperationException(
                "Money subtraction cannot produce a negative amount."
            );
        }

        return new Money(
            Amount - other.Amount,
            Currency
        );
    }

    /// <summary>
    /// Allows safe same-currency addition.
    /// </summary>
    public static Money operator +(
        Money left,
        Money right)
    {
        return left.Add(
            right
        );
    }

    /// <summary>
    /// Allows safe same-currency subtraction.
    /// </summary>
    public static Money operator -(
        Money left,
        Money right)
    {
        return left.Subtract(
            right
        );
    }

    /// <summary>
    /// Preserves positional-style deconstruction compatibility.
    /// </summary>
    public void Deconstruct(
        out decimal amount,
        out CurrencyCode currency)
    {
        amount = Amount;
        currency = Currency;
    }

    private void EnsureSameCurrency(
        Money other)
    {
        if (Currency != other.Currency)
        {
            throw new InvalidOperationException(
                $"Cannot combine {Currency} and {other.Currency} " +
                "without an explicit currency conversion."
            );
        }
    }

    private static void ValidatePrecision(
        decimal amount,
        CurrencyCode currency)
    {
        var decimalPlaces =
            currency switch
            {
                CurrencyCode.Usd => 2,
                CurrencyCode.Khr => 0,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(currency),
                    currency,
                    "Unsupported currency code."
                )
            };

        var normalized =
            decimal.Round(
                amount,
                decimalPlaces,
                MidpointRounding.ToZero
            );

        if (normalized != amount)
        {
            throw new ArgumentException(
                currency == CurrencyCode.Usd
                    ? "USD amounts support at most two decimal places."
                    : "KHR amounts must use whole riel values.",
                nameof(amount)
            );
        }
    }
}

/// <summary>
/// Immutable note-by-note change instructions for the cash recycler.
/// </summary>
public sealed record ChangeBreakdown
{
    /// <summary>
    /// Creates a validated change breakdown.
    /// </summary>
    public ChangeBreakdown(
        IReadOnlyList<KeyValuePair<Money, int>>
            usdNotes,
        IReadOnlyList<KeyValuePair<Money, int>>
            khrNotes)
    {
        ArgumentNullException.ThrowIfNull(
            usdNotes
        );

        ArgumentNullException.ThrowIfNull(
            khrNotes
        );

        UsdNotes =
            CopyAndValidate(
                usdNotes,
                CurrencyCode.Usd,
                nameof(usdNotes)
            );

        KhrNotes =
            CopyAndValidate(
                khrNotes,
                CurrencyCode.Khr,
                nameof(khrNotes)
            );
    }

    /// <summary>
    /// USD denomination and note-count pairs.
    ///
    /// Example:
    /// Money.Usd(1m) => 2 means two one-dollar notes.
    /// </summary>
    public IReadOnlyList<KeyValuePair<Money, int>>
        UsdNotes
    {
        get;
    }

    /// <summary>
    /// KHR denomination and note-count pairs.
    ///
    /// Example:
    /// Money.Khr(1000m) => 2 means two 1,000-KHR notes.
    /// </summary>
    public IReadOnlyList<KeyValuePair<Money, int>>
        KhrNotes
    {
        get;
    }

    /// <summary>
    /// Represents a result where no change must be dispensed.
    /// </summary>
    public static ChangeBreakdown Empty { get; } =
        new(
            Array.Empty<
                KeyValuePair<Money, int>
            >(),
            Array.Empty<
                KeyValuePair<Money, int>
            >()
        );

    private static IReadOnlyList<
        KeyValuePair<Money, int>>
        CopyAndValidate(
            IReadOnlyList<
                KeyValuePair<Money, int>>
                    notes,
            CurrencyCode expectedCurrency,
            string parameterName)
    {
        var copiedNotes =
            new List<
                KeyValuePair<Money, int>
            >(
                notes.Count
            );

        var usedDenominations =
            new HashSet<decimal>();

        foreach (var note in notes)
        {
            var denomination =
                note.Key;

            var count =
                note.Value;

            if (
                denomination.Currency !=
                expectedCurrency
            )
            {
                throw new ArgumentException(
                    $"The {parameterName} collection " +
                    $"must contain only {expectedCurrency} denominations.",
                    parameterName
                );
            }

            if (denomination.Amount <= 0m)
            {
                throw new ArgumentException(
                    "A note denomination must be greater than zero.",
                    parameterName
                );
            }

            if (count <= 0)
            {
                throw new ArgumentException(
                    "A denomination note count must be greater than zero.",
                    parameterName
                );
            }

            if (
                !usedDenominations.Add(
                    denomination.Amount
                )
            )
            {
                throw new ArgumentException(
                    $"Duplicate denomination detected: " +
                    $"{denomination.Amount} {expectedCurrency}.",
                    parameterName
                );
            }

            copiedNotes.Add(
                note
            );
        }

        return new ReadOnlyCollection<
            KeyValuePair<Money, int>
        >(
            copiedNotes
        );
    }
}