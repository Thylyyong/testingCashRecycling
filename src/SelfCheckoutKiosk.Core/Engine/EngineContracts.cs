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

/// <summary>Result of routing + handling a single raw scan.</summary>
public sealed record ScanResult(ScanCategory Category, bool Accepted, string? Message = null);

// ---------------------------------------------------------------------------
// Cash payment decision events (raised by LLCoreLogicEngine — Systems scope)
// ---------------------------------------------------------------------------

/// <summary>
/// Raised after each note insertion while the running total is still below
/// the product price. The machine has committed the note to the vault and
/// is waiting for the customer to insert more cash (USD or KHR).
/// ViewModel: show "Please insert {RemainingUsd:F2} USD / {RemainingKhr:N0} KHR more".
/// </summary>
public sealed class CashPaymentPendingEventArgs(
    decimal totalUsd,
    decimal tenderedUsd,
    decimal remainingUsd,
    decimal remainingKhr) : EventArgs
{
    public decimal TotalUsd     { get; } = totalUsd;
    public decimal TenderedUsd  { get; } = tenderedUsd;
    /// <summary>Amount still owed, expressed in USD.</summary>
    public decimal RemainingUsd { get; } = remainingUsd;
    /// <summary>Amount still owed, expressed in KHR (for display convenience).</summary>
    public decimal RemainingKhr { get; } = remainingKhr;
}

/// <summary>
/// Raised when the accumulated payment equals or slightly exceeds the total,
/// and the overpayment is within the 500 KHR machine tolerance. The machine
/// has committed all escrowed cash to the vault. The merchant absorbs the
/// small overpayment (no change is dispensed).
/// ViewModel: show success screen and trigger receipt print.
/// </summary>
public sealed class CashPaymentConfirmedEventArgs(
    decimal totalUsd,
    decimal tenderedUsd,
    decimal overpaymentKhr) : EventArgs
{
    public decimal TotalUsd      { get; } = totalUsd;
    public decimal TenderedUsd   { get; } = tenderedUsd;
    /// <summary>
    /// Overpayment in KHR that the merchant absorbs (0–500 KHR).
    /// Shown on receipt for transparency.
    /// </summary>
    public decimal OverpaymentKhr { get; } = overpaymentKhr;
}

/// <summary>
/// Raised when the current escrowed note would push the running total more
/// than 500 KHR above the product price. The machine has returned that note
/// to the customer; previously committed notes remain in the vault.
/// ViewModel: show "Too much — please insert less" alert.
/// </summary>
public sealed class CashPaymentRejectedEventArgs(
    decimal totalUsd,
    decimal tenderedUsd,
    decimal overpaymentKhr) : EventArgs
{
    public decimal TotalUsd       { get; } = totalUsd;
    public decimal TenderedUsd    { get; } = tenderedUsd;
    /// <summary>
    /// How much over the limit the rejected note would have been (KHR).
    /// Shown in the UI alert so the customer knows how much less to insert.
    /// </summary>
    public decimal OverpaymentKhr { get; } = overpaymentKhr;
}
