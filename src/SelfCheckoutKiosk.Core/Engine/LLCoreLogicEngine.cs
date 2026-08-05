using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Core.Currency;
using SelfCheckoutKiosk.Core.Licensing;
using SelfCheckoutKiosk.Domain.Enums;

namespace SelfCheckoutKiosk.Core.Engine;

// OnProductAdded and OnBalanceChanged are declared now so ViewModels can bind
// on Day 1 without waiting for the full state machine. The Backend Team removes
// this suppression when those events are raised in the completed state machine.
#pragma warning disable CS0067 // Event is declared but never raised (stub — Backend scope)

/// <summary>
/// Deterministic state machine and single HAL event subscriber (Blueprint §3 + §4).
/// The constructor receives interfaces only — never concrete vendor adapter types.
/// This is where Category 1 (Backend) and Category 2 (Systems) formally meet.
///
/// InitializeAsync (Systems Team scope — this file):
///   Wires HardwareAppendLog → cash recycler events (FIRST, per Blueprint §4),
///   wires low-float and fault handlers, then connects the cash recycler.
///
///   TODO(Lead/Backend): add EnforceFeatureAccess(LicensedFeature.CashRecycler)
///   as the very first step inside InitializeAsync once OfflineLicenseManager
///   is implemented. The call site is marked below with a clear comment.
///
/// State machine (Backend Team scope — remaining methods are stubs):
///   TODO(Backend): implement Idle → Scanning → AwaitingPayment →
///   ProcessingCash → DispensingChange → TransactionComplete, plus the
///   parallel ExactCashOnlyLockout branch. Subscribe to HAL events here only.
/// </summary>
public sealed class LLCoreLogicEngine : ILLCoreLogicEngine
{
    // -----------------------------------------------------------------------
    // Injected collaborators (interfaces only — never vendor concrete types)
    // -----------------------------------------------------------------------
    private readonly ICashRecycler          _cashRecycler;
    private readonly IBarcodeScanner        _scanner;
    private readonly IReceiptPrinter        _printer;
    private readonly DualCurrencyCalculator _calculator;
    private readonly LowFloatMonitor        _lowFloatMonitor;
    private readonly OfflineLicenseManager  _licenseManager;
    private readonly HardwareAppendLog      _hardwareAppendLog;

    public LLCoreLogicEngine(
        ICashRecycler          cashRecycler,
        IBarcodeScanner        scanner,
        IReceiptPrinter        printer,
        DualCurrencyCalculator calculator,
        LowFloatMonitor        lowFloatMonitor,
        OfflineLicenseManager  licenseManager,
        HardwareAppendLog      hardwareAppendLog)
    {
        _cashRecycler      = cashRecycler;
        _scanner           = scanner;
        _printer           = printer;
        _calculator        = calculator;
        _lowFloatMonitor   = lowFloatMonitor;
        _licenseManager    = licenseManager;
        _hardwareAppendLog = hardwareAppendLog;
    }

    // -----------------------------------------------------------------------
    // State
    // -----------------------------------------------------------------------
    public KioskState CurrentState { get; private set; } = KioskState.Idle;

    // -----------------------------------------------------------------------
    // Public events (ILLCoreLogicEngine)
    // -----------------------------------------------------------------------
    public event EventHandler<KioskStateChangedEventArgs>? OnStateChanged;
    public event EventHandler<ProductAddedEventArgs>?      OnProductAdded;    // Backend scope
    public event EventHandler<BalanceChangedEventArgs>?    OnBalanceChanged;  // Backend scope
    public event EventHandler?                             LowFloatStateTriggered;
    public event EventHandler?                             LowFloatStateCleared;
    public event EventHandler<HardwareFaultEventArgs>?     OnHardwareFault;

    // -----------------------------------------------------------------------
    // InitializeAsync — Systems Team scope
    // -----------------------------------------------------------------------
    /// <summary>
    /// Wires all hardware event subscriptions and connects the cash recycler.
    /// Must be called exactly once, after the DI host is built (Blueprint §6, step 7).
    ///
    /// Subscription order for ICashRecycler.OnNoteInEscrow (Blueprint §4):
    ///   1. _hardwareAppendLog.HandleNoteInEscrow  — audit record written FIRST.
    ///   2. HandleNoteInEscrowInternal             — engine processing runs second.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // ===================================================================
        // TODO(Lead / Backend): Uncomment the line below once OfflineLicenseManager
        // is implemented. This MUST be the very first call — ConnectAsync must
        // never run without the license gate having cleared (Blueprint §4, §6).
        //
        //   _licenseManager.EnforceFeatureAccess(LicensedFeature.CashRecycler);
        //
        // Failure must be a hard stop, not a degraded mode (Blueprint §3).
        // ===================================================================

        // --- Wire cash-recycler events (order matters — audit log first) ---
        _cashRecycler.OnNoteInEscrow += _hardwareAppendLog.HandleNoteInEscrow; // FIRST (Blueprint §4)
        _cashRecycler.OnNoteInEscrow += HandleNoteInEscrowInternal;            // engine second
        _cashRecycler.OnFault        += HandleCashRecyclerFault;

        // --- Wire low-float monitor events ----------------------------------
        _lowFloatMonitor.LowFloatStateTriggered += HandleLowFloatTriggered;
        _lowFloatMonitor.LowFloatStateCleared   += HandleLowFloatCleared;

        // --- Connect the cash recycler hardware -----------------------------
        // License gate (above TODO) must clear before this line is reached.
        await _cashRecycler.ConnectAsync(cancellationToken).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------
    // Private event handlers — Systems Team scope (cash path)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Called on every note-in-escrow event, AFTER HardwareAppendLog has already
    /// written the audit record. The EscrowId is available via
    /// _hardwareAppendLog.LastEscrowId (single-note-at-a-time hardware constraint).
    ///
    /// TODO(Backend): implement ProcessingCash state transition, balance update,
    /// and ultimately CommitToVault / Reject call via _hardwareAppendLog.
    /// </summary>
    private void HandleNoteInEscrowInternal(object? sender, NoteInEscrowEventArgs e)
    {
        // Audit record already written by HardwareAppendLog (first subscriber).
        // TODO(Backend): transition to ProcessingCash, update balance, raise OnBalanceChanged.
    }

    /// <summary>
    /// Reacts to the low-float safeguard trigger (Blueprint §4):
    /// immediately stops cash acceptance and transitions to ExactCashOnlyLockout.
    /// </summary>
    private void HandleLowFloatTriggered(object? sender, EventArgs e)
    {
        // Hard stop — fire-and-forget is acceptable here because StopAcceptingCashAsync
        // is idempotent and the device state change is physical (not data-at-risk).
        // TODO(Backend): use ExactCashOnlyLockout state branch when it is added to KioskState.
        _ = _cashRecycler.StopAcceptingCashAsync();
        TransitionState(KioskState.Faulted); // placeholder until ExactCashOnlyLockout branch exists
        LowFloatStateTriggered?.Invoke(this, EventArgs.Empty);
    }

    private void HandleLowFloatCleared(object? sender, EventArgs e)
        => LowFloatStateCleared?.Invoke(this, EventArgs.Empty);

    private void HandleCashRecyclerFault(object? sender, HardwareFaultEventArgs e)
    {
        TransitionState(KioskState.Faulted);
        OnHardwareFault?.Invoke(this, e);
    }

    // -----------------------------------------------------------------------
    // State helper
    // -----------------------------------------------------------------------
    private void TransitionState(KioskState newState)
    {
        var previous = CurrentState;
        if (previous == newState) return;
        CurrentState = newState;
        OnStateChanged?.Invoke(this, new KioskStateChangedEventArgs(previous, newState));
    }

    // -----------------------------------------------------------------------
    // Backend Team stubs (DO NOT implement here — see Blueprint §3)
    // -----------------------------------------------------------------------
    public Task<ScanResult> SubmitScanAsync(string rawScan, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("TODO(Backend): route via RegexRouter and dispatch.");

    public Task SelectPaymentMethodAsync(PaymentMethod method, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("TODO(Backend): transition to AwaitingPayment / arm cash.");

    public Task ResetToIdleAsync(CancellationToken cancellationToken = default)
        => throw new NotImplementedException("TODO(Backend): tear down transaction, return to Idle.");
}

