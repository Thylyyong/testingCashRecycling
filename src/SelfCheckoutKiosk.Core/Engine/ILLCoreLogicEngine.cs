using System;
using System.Threading;
using System.Threading.Tasks;
using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Domain.ValueObjects;

namespace SelfCheckoutKiosk.Core.Engine;

/// <summary>
/// Public surface of the deterministic core engine.
///
/// ViewModels interact with this interface rather than accessing
/// hardware abstractions directly.
///
/// LLCoreLogicEngine is the single subscriber to all HAL event
/// streams.
/// </summary>
public interface ILLCoreLogicEngine
{
    KioskState CurrentState
    {
        get;
    }

    event EventHandler<KioskStateChangedEventArgs>?
        OnStateChanged;

    event EventHandler<ProductAddedEventArgs>?
        OnProductAdded;

    event EventHandler<BalanceChangedEventArgs>?
        OnBalanceChanged;

    event EventHandler?
        LowFloatStateTriggered;

    event EventHandler?
        LowFloatStateCleared;

    event EventHandler<HardwareFaultEventArgs>?
        OnHardwareFault;

    /// <summary>
    /// Raised when a physically escrowed cash note is rejected for
    /// exceeding the overpayment tolerance. See
    /// <see cref="CashNoteRejectedEventArgs"/>.
    /// </summary>
    event EventHandler<CashNoteRejectedEventArgs>?
        OnCashNoteRejected;

    /// <summary>
    /// Validates the offline license, subscribes to HAL events,
    /// and connects the configured hardware devices.
    /// </summary>
    Task InitializeAsync(
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Classifies and dispatches one raw scanner value.
    /// </summary>
    Task<ScanResult> SubmitScanAsync(
        string rawScan,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Selects a payment method.
    ///
    /// KHQR uses this command directly.
    ///
    /// BeginCashPaymentAsync should be used for exact-cash
    /// processing because cash requires a transaction total and
    /// exchange rate.
    /// </summary>
    Task SelectPaymentMethodAsync(
        PaymentMethod method,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Starts an exact-cash payment session.
    ///
    /// The transaction total uses USD as the pricing currency.
    /// Accepted USD and KHR notes are normalized to KHR for exact
    /// comparison.
    ///
    /// A note that would cause overpayment is rejected.
    /// No physical change is calculated or dispensed.
    /// </summary>
    Task BeginCashPaymentAsync(
        Money transactionTotalUsd,
        decimal usdToKhrRate,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns the engine to Idle from a safe state.
    /// </summary>
    Task ResetToIdleAsync(
        CancellationToken cancellationToken = default
    );
}
