using SelfCheckoutKiosk.Domain.Entities;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Domain.ValueObjects;

namespace SelfCheckoutKiosk.Core.Engine;

public sealed class KioskStateChangedEventArgs(KioskState previous, KioskState current) : EventArgs
{
    public KioskState Previous { get; } = previous;
    public KioskState Current { get; } = current;
}

public sealed class ProductAddedEventArgs(Product product, decimal runningTotalUsd) : EventArgs
{
    public Product Product { get; } = product;
    public decimal RunningTotalUsd { get; } = runningTotalUsd;
}

public sealed class BalanceChangedEventArgs(decimal totalUsd, decimal tenderedUsd, decimal remainingUsd) : EventArgs
{
    public decimal TotalUsd { get; } = totalUsd;
    public decimal TenderedUsd { get; } = tenderedUsd;
    public decimal RemainingUsd { get; } = remainingUsd;
}

/// <summary>
/// Raised when a physically escrowed cash note is rejected because
/// accepting it would overpay the transaction beyond tolerance.
///
/// The rejected note never enters the accepted running total — the
/// session stays open in ProcessingCash, and OnBalanceChanged is not
/// raised for this note. This is the only public signal that
/// distinguishes "note physically returned" from "no note yet inserted".
/// </summary>
public sealed class CashNoteRejectedEventArgs(Money note, Money remainingKhr) : EventArgs
{
    /// <summary>The note that was rejected and physically returned.</summary>
    public Money Note { get; } = note;

    /// <summary>Amount still owed, in KHR, unchanged by the rejected note.</summary>
    public Money RemainingKhr { get; } = remainingKhr;
}

/// <summary>Result of routing + handling a single raw scan.</summary>
public sealed record ScanResult(ScanCategory Category, bool Accepted, string? Message = null);
