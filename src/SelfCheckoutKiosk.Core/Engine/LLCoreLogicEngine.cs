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
/// Cash payment machine rules (Systems Team scope):
///   1 USD = 4 100 KHR (DualCurrencyCalculator.DefaultUsdToKhrRate).
///   Under-payment  → note committed to vault, session kept open; customer inserts more
///                    (mixed USD + KHR accepted across multiple insertions).
///   Over ≤ 500 KHR → confirmed, merchant absorbs the small overpayment.
///   Over > 500 KHR → current note rejected (returned to customer); prior
///                    committed notes stay in the vault; session stays open.
///
/// State machine (Backend Team scope — remaining methods are stubs):
///   TODO(Backend): implement Idle → Scanning → AwaitingPayment →
///   ProcessingCash → DispensingChange → TransactionComplete, plus the
///   parallel ExactCashOnlyLockout branch. Subscribe to HAL events here only.
/// </summary>
public sealed class LLCoreLogicEngine : ILLCoreLogicEngine
{
    // -----------------------------------------------------------------------
    // Machine constant — overpayment tolerance
    // -----------------------------------------------------------------------
    /// <summary>
    /// Maximum overpayment (in KHR) the machine will absorb without rejecting
    /// the note. If overpayment &gt; this value the note is pushed back.
    /// Blueprint §3: 500 KHR (~$0.12 at 4 100 rate).
    /// </summary>
    private const decimal MaxAcceptableOverpaymentKhr = 500m;

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

    /// <summary>
    /// Active cash payment session. Non-null only while in ProcessingCash state.
    /// Created by BeginCashPaymentAsync, cleared by confirmation, fault, or reset.
    /// NOT thread-safe — all access is from the single HAL event thread (Blueprint §4).
    /// </summary>
    private MixedPaymentAccumulator? _paymentSession;

    // -----------------------------------------------------------------------
    // Public events (ILLCoreLogicEngine)
    // -----------------------------------------------------------------------
    public event EventHandler<KioskStateChangedEventArgs>?   OnStateChanged;
    public event EventHandler<ProductAddedEventArgs>?        OnProductAdded;    // Backend scope
    public event EventHandler<BalanceChangedEventArgs>?      OnBalanceChanged;  // Backend scope
    public event EventHandler?                               LowFloatStateTriggered;
    public event EventHandler?                               LowFloatStateCleared;
    public event EventHandler<HardwareFaultEventArgs>?       OnHardwareFault;

    // Cash payment decision events (Systems Team scope)
    public event EventHandler<CashPaymentPendingEventArgs>?   OnCashPaymentPending;
    public event EventHandler<CashPaymentConfirmedEventArgs>? OnCashPaymentConfirmed;
    public event EventHandler<CashPaymentRejectedEventArgs>?  OnCashPaymentRejected;

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
    // BeginCashPaymentAsync — Systems Team scope
    // -----------------------------------------------------------------------
    /// <summary>
    /// Arms the recycler for cash acceptance and opens a new payment session
    /// for the given product total. Transitions the engine to ProcessingCash.
    ///
    /// Call once per transaction, after the customer confirms they want to pay
    /// with cash (e.g. from the payment selection ViewModel).
    ///
    /// Multi-note / mixed-currency flow:
    ///   Each subsequent OnNoteInEscrow event is processed by
    ///   HandleNoteInEscrowInternal until the session is complete or faulted.
    /// </summary>
    /// <param name="totalUsd">Product total in USD (e.g. 0.75m for a $0.75 item).</param>
    public async Task BeginCashPaymentAsync(
        decimal totalUsd,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(totalUsd);

        // Create a fresh accumulator for this transaction.
        _paymentSession = new MixedPaymentAccumulator(totalUsd, _calculator);

        TransitionState(KioskState.ProcessingCash);

        // Arm the hardware — from this point on, notes enter escrow and fire events.
        await _cashRecycler.ArmAcceptanceAsync(cancellationToken).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------
    // Private event handlers — Systems Team scope (cash path)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Called on every note-in-escrow event, AFTER HardwareAppendLog has already
    /// written the IN_ESCROW audit record. The EscrowId is available via
    /// _hardwareAppendLog.LastEscrowId (single-note-at-a-time hardware constraint).
    ///
    /// Decision table (1 USD = 4 100 KHR, MaxAcceptableOverpaymentKhr = 500):
    ///
    ///   Accumulated &lt; Total (under-paid):
    ///     → CommitToVault (note stays in machine permanently)
    ///     → Raise OnCashPaymentPending (UI: "insert X more")
    ///     → Session stays open; recycler stays Armed for next note.
    ///
    ///   Accumulated ≥ Total AND overpayment ≤ 500 KHR:
    ///     → CommitToVault
    ///     → DisarmAcceptanceAsync (no more notes)
    ///     → Transition to TransactionComplete
    ///     → Raise OnCashPaymentConfirmed (UI: success + receipt)
    ///
    ///   Accumulated ≥ Total AND overpayment &gt; 500 KHR:
    ///     → Log.Reject + RejectEscrowedNoteAsync (push this note back)
    ///     → Previously committed notes remain in vault
    ///     → Raise OnCashPaymentRejected (UI: "too much — insert less")
    ///     → Session stays open at the prior accumulated total.
    /// </summary>
    private void HandleNoteInEscrowInternal(object? sender, NoteInEscrowEventArgs e)
    {
        // Audit record already written by HardwareAppendLog (first subscriber).
        var escrowId = _hardwareAppendLog.LastEscrowId;

        // Safety guard — reject if no active session (e.g. unexpected note).
        if (_paymentSession is null)
        {
            _hardwareAppendLog.Reject(escrowId);
            _ = _cashRecycler.RejectEscrowedNoteAsync();
            return;
        }

        // Speculatively accumulate the note to test whether it would overpay.
        _paymentSession.AccumulateNote(e.Note);

        var totalUsd    = _paymentSession.TotalUsd;
        var tenderedUsd = _paymentSession.AccumulatedUsdEquivalent;

        // Compute overpayment in KHR (negative means under-paid — clamp to 0).
        var overpaymentUsd = tenderedUsd - totalUsd;
        var overpaymentKhr = overpaymentUsd * DualCurrencyCalculator.DefaultUsdToKhrRate;

        // --- CASE A: Under-paid — commit note, wait for more ----------------
        if (overpaymentKhr < 0m)
        {
            _hardwareAppendLog.CommitToVault(escrowId);
            // Note physically stays in machine vault. No DisarmAcceptance —
            // the recycler remains Armed and ready for the next insertion.

            var remainingUsd = _paymentSession.RemainingUsd;
            var remainingKhr = remainingUsd * DualCurrencyCalculator.DefaultUsdToKhrRate;

            OnCashPaymentPending?.Invoke(this, new CashPaymentPendingEventArgs(
                totalUsd,
                tenderedUsd,
                remainingUsd,
                remainingKhr));

            return;
        }

        // --- CASE B: Overpayment within tolerance — confirm ------------------
        if (overpaymentKhr <= MaxAcceptableOverpaymentKhr)
        {
            _hardwareAppendLog.CommitToVault(escrowId);

            // Disarm the recycler — session is complete.
            _ = _cashRecycler.DisarmAcceptanceAsync();

            _paymentSession = null;
            TransitionState(KioskState.TransactionComplete);

            OnCashPaymentConfirmed?.Invoke(this, new CashPaymentConfirmedEventArgs(
                totalUsd,
                tenderedUsd,
                overpaymentKhr));

            return;
        }

        // --- CASE C: Overpayment exceeds tolerance — reject this note --------
        // Roll back the speculative accumulation so the session total is correct.
        _paymentSession.RollBackLastNote(e.Note);

        _hardwareAppendLog.Reject(escrowId);

        // Fire-and-forget is safe: RejectEscrowedNoteAsync is idempotent and the
        // physical note will be returned to the customer regardless.
        _ = _cashRecycler.RejectEscrowedNoteAsync();

        // Recompute tendered after rollback for accurate event data.
        var tenderedAfterRollback = _paymentSession.AccumulatedUsdEquivalent;

        OnCashPaymentRejected?.Invoke(this, new CashPaymentRejectedEventArgs(
            totalUsd,
            tenderedAfterRollback,
            overpaymentKhr));
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
