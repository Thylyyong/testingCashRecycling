using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Domain.Enums;

namespace SelfCheckoutKiosk.Core.Engine;

/// <summary>
/// Public surface of the deterministic core engine (Blueprint §3 & §6).
/// ViewModels bind to THIS — never to hardware types directly. The engine is
/// the single subscriber to all HAL event streams.
///
/// Front-End devs mock this interface for MVVM work on Day 1; the Back-End
/// dev fills in <see cref="LLCoreLogicEngine"/> behind it — the two proceed
/// in parallel without blocking each other.
/// </summary>
public interface ILLCoreLogicEngine
{
    KioskState CurrentState { get; }

    event EventHandler<KioskStateChangedEventArgs>? OnStateChanged;
    event EventHandler<ProductAddedEventArgs>?      OnProductAdded;
    event EventHandler<BalanceChangedEventArgs>?    OnBalanceChanged;
    event EventHandler?                             LowFloatStateTriggered;
    event EventHandler?                             LowFloatStateCleared;
    event EventHandler<HardwareFaultEventArgs>?     OnHardwareFault;

    // -----------------------------------------------------------------------
    // Cash payment decision events (Systems Team scope — wired in engine)
    // -----------------------------------------------------------------------
    /// <summary>
    /// Raised after a note is committed to the vault but the total is still
    /// not yet met. ViewModel shows "insert X more USD / Y more KHR".
    /// </summary>
    event EventHandler<CashPaymentPendingEventArgs>?   OnCashPaymentPending;

    /// <summary>
    /// Raised when accumulated cash equals the total (within 500 KHR tolerance).
    /// ViewModel transitions to success screen and triggers receipt print.
    /// </summary>
    event EventHandler<CashPaymentConfirmedEventArgs>? OnCashPaymentConfirmed;

    /// <summary>
    /// Raised when a note would overpay by more than 500 KHR; that note is
    /// physically returned to the customer. Previously committed notes remain.
    /// ViewModel shows "Too much — please insert less".
    /// </summary>
    event EventHandler<CashPaymentRejectedEventArgs>?  OnCashPaymentRejected;

    Task InitializeAsync(CancellationToken cancellationToken = default);

    // -----------------------------------------------------------------------
    // Cash payment command (Systems Team scope)
    // -----------------------------------------------------------------------
    /// <summary>
    /// Opens a cash payment session for the given product total: creates a
    /// <see cref="MixedPaymentAccumulator"/>, transitions to ProcessingCash,
    /// and arms the recycler. Call this when the customer selects Cash payment
    /// and the UI shows the "insert cash" screen.
    /// </summary>
    Task BeginCashPaymentAsync(decimal totalUsd, CancellationToken cancellationToken = default);

    // -----------------------------------------------------------------------
    // Backend Team stubs — implemented by Back-End (Blueprint §3)
    // -----------------------------------------------------------------------
    Task<ScanResult> SubmitScanAsync(string rawScan, CancellationToken cancellationToken = default);
    Task SelectPaymentMethodAsync(PaymentMethod method, CancellationToken cancellationToken = default);
    Task ResetToIdleAsync(CancellationToken cancellationToken = default);
}
