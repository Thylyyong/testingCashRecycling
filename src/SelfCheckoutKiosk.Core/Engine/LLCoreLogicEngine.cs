using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Core.Currency;
using SelfCheckoutKiosk.Core.Licensing;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Domain.ValueObjects;

namespace SelfCheckoutKiosk.Core.Engine;

// Product lookup remains deferred until a product repository or
// catalog abstraction is integrated.
#pragma warning disable CS0067

/// <summary>
/// Deterministic application engine for the self-checkout kiosk.
///
/// The engine receives hardware abstractions rather than concrete
/// vendor implementations.
///
/// LLCoreLogicEngine is the single subscriber to HAL event streams.
/// </summary>
public sealed class LLCoreLogicEngine : ILLCoreLogicEngine
{
    private readonly ICashRecycler
        _cashRecycler;

    private readonly ICashEscrowController?
        _cashEscrowController;

    private readonly IBarcodeScanner
        _scanner;

    private readonly IReceiptPrinter
        _printer;

    private readonly DualCurrencyCalculator
        _calculator;

    private readonly CashAcceptancePolicy
        _cashAcceptancePolicy;

    private readonly LowFloatMonitor
        _lowFloatMonitor;

    private readonly IOfflineLicenseManager
        _licenseManager;

    private readonly HardwareAppendLog
        _hardwareAppendLog;

    /*
     * Serializes InitializeAsync calls.
     */
    private readonly SemaphoreSlim
        _initializationGate =
            new(
                initialCount: 1,
                maxCount: 1
            );

    /*
     * Serializes state-machine commands and hardware-event
     * reactions.
     */
    private readonly SemaphoreSlim
        _stateGate =
            new(
                initialCount: 1,
                maxCount: 1
            );

    /*
     * Preserves the order in which synchronous hardware and
     * low-float events reach the engine.
     */
    private readonly object
        _eventQueueSync =
            new();

    private Task
        _eventQueueTail =
            Task.CompletedTask;

    private bool
        _isInitialized;

    private bool
        _eventsSubscribed;

    /*
     * These flags support safe retry after partial initialization.
     */
    private bool
        _cashRecyclerConnected;

    private bool
        _scannerConnected;

    private bool
        _printerConnected;

    /*
     * Exact-cash session state.
     *
     * USD remains the pricing currency.
     * KHR is used as the normalized cash comparison unit.
     */
    private Money?
        _cashTransactionTotalUsd;

    private Money?
        _cashTargetKhr;

    private Money
        _acceptedCashKhr =
            Money.Khr(
                0m
            );

    private decimal?
        _usdToKhrRate;

    /*
     * Only one physical note may be awaiting escrow resolution at
     * one time.
     */
    private PendingCashEscrow?
        _pendingCashEscrow;

    /// <summary>
    /// Compatibility constructor used by the current application
    /// composition root and existing integration tests.
    ///
    /// Optional parameters preserve existing construction code while
    /// allowing tests to inject deterministic policy and audit
    /// dependencies.
    /// </summary>
    public LLCoreLogicEngine(
        ICashRecycler cashRecycler,
        IBarcodeScanner scanner,
        IReceiptPrinter printer,
        DualCurrencyCalculator calculator,
        LowFloatMonitor lowFloatMonitor,
        OfflineLicenseManager licenseManager,
        HardwareAppendLog? hardwareAppendLog = null,
        CashAcceptancePolicy? cashAcceptancePolicy = null)
        : this(
            cashRecycler,
            scanner,
            printer,
            calculator,
            lowFloatMonitor,
            (IOfflineLicenseManager)licenseManager,
            hardwareAppendLog,
            cashAcceptancePolicy
        )
    {
    }

    /// <summary>
    /// Testable constructor that depends on the offline-license
    /// abstraction.
    ///
    /// Internal visibility prevents dependency-injection systems
    /// outside Core from seeing two public constructors.
    /// </summary>
    internal LLCoreLogicEngine(
        ICashRecycler cashRecycler,
        IBarcodeScanner scanner,
        IReceiptPrinter printer,
        DualCurrencyCalculator calculator,
        LowFloatMonitor lowFloatMonitor,
        IOfflineLicenseManager licenseManager,
        HardwareAppendLog? hardwareAppendLog = null,
        CashAcceptancePolicy? cashAcceptancePolicy = null)
    {
        ArgumentNullException.ThrowIfNull(
            cashRecycler
        );

        ArgumentNullException.ThrowIfNull(
            scanner
        );

        ArgumentNullException.ThrowIfNull(
            printer
        );

        ArgumentNullException.ThrowIfNull(
            calculator
        );

        ArgumentNullException.ThrowIfNull(
            lowFloatMonitor
        );

        ArgumentNullException.ThrowIfNull(
            licenseManager
        );

        _cashRecycler =
            cashRecycler;

        /*
         * The escrow controller is an optional hardware capability.
         *
         * Exact-cash processing fails safely when the configured
         * device cannot provide physically confirmed escrow.
         */
        _cashEscrowController =
            cashRecycler as
                ICashEscrowController;

        _scanner =
            scanner;

        _printer =
            printer;

        _calculator =
            calculator;

        _cashAcceptancePolicy =
            cashAcceptancePolicy ??
            new CashAcceptancePolicy();

        _lowFloatMonitor =
            lowFloatMonitor;

        _licenseManager =
            licenseManager;

        _hardwareAppendLog =
            hardwareAppendLog ??
            CreateDefaultHardwareAppendLog();
    }

    public KioskState CurrentState
    {
        get;
        private set;
    } = KioskState.Idle;

    public event EventHandler<KioskStateChangedEventArgs>?
        OnStateChanged;

    public event EventHandler<ProductAddedEventArgs>?
        OnProductAdded;

    public event EventHandler<BalanceChangedEventArgs>?
        OnBalanceChanged;

    public event EventHandler?
        LowFloatStateTriggered;

    public event EventHandler?
        LowFloatStateCleared;

    public event EventHandler<HardwareFaultEventArgs>?
        OnHardwareFault;

    /// <summary>
    /// Creates the default production hardware audit log.
    /// </summary>
    private static HardwareAppendLog
        CreateDefaultHardwareAppendLog()
    {
        var localApplicationDataPath =
            Environment.GetFolderPath(
                Environment.SpecialFolder
                    .LocalApplicationData
            );

        if (
            string.IsNullOrWhiteSpace(
                localApplicationDataPath
            )
        )
        {
            throw new InvalidOperationException(
                "The local application-data directory could not " +
                "be resolved for the hardware audit log."
            );
        }

        var auditFilePath =
            Path.Combine(
                localApplicationDataPath,
                "SelfCheckoutKiosk",
                "Audit",
                "hardware-append.log"
            );

        return new HardwareAppendLog(
            auditFilePath
        );
    }

    /// <summary>
    /// Validates the license, attaches engine-owned event handlers,
    /// and connects the HAL devices.
    ///
    /// Repeated calls after successful initialization do nothing.
    /// </summary>
    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        await _initializationGate
            .WaitAsync(
                cancellationToken
            )
            .ConfigureAwait(false);

        try
        {
            if (_isInitialized)
            {
                return;
            }

            await _licenseManager
                .LoadAndValidateAsync(
                    cancellationToken
                )
                .ConfigureAwait(false);

            _licenseManager
                .EnforceFeatureAccess(
                    LicensedFeature.CashRecycler
                );

            SubscribeToEventsOnce();

            if (!_cashRecyclerConnected)
            {
                await _cashRecycler
                    .ConnectAsync(
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                _cashRecyclerConnected =
                    true;
            }

            if (!_scannerConnected)
            {
                await _scanner
                    .ConnectAsync(
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                _scannerConnected =
                    true;
            }

            if (!_printerConnected)
            {
                await _printer
                    .ConnectAsync(
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                _printerConnected =
                    true;
            }

            _isInitialized =
                true;
        }
        finally
        {
            _initializationGate
                .Release();
        }
    }

    /// <summary>
    /// Classifies and handles one raw scan.
    /// </summary>
    public async Task<ScanResult> SubmitScanAsync(
        string rawScan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            rawScan
        );

        cancellationToken
            .ThrowIfCancellationRequested();

        var normalizedScan =
            rawScan.Trim();

        var category =
            RegexRouter.Classify(
                normalizedScan
            );

        if (
            category ==
            ScanCategory.Unknown
        )
        {
            return new ScanResult(
                ScanCategory.Unknown,
                false,
                "Unsupported scan format."
            );
        }

        await _stateGate
            .WaitAsync(
                cancellationToken
            )
            .ConfigureAwait(false);

        try
        {
            if (
                CurrentState !=
                    KioskState.Idle &&
                CurrentState !=
                    KioskState.Scanning
            )
            {
                throw new InvalidOperationException(
                    $"Scans cannot be submitted while the kiosk is " +
                    $"in the {CurrentState} state."
                );
            }

            switch (category)
            {
                case ScanCategory.Ean13Product:
                    {
                        if (
                            CurrentState ==
                            KioskState.Idle
                        )
                        {
                            TransitionTo(
                                KioskState.Scanning
                            );
                        }

                        return new ScanResult(
                            ScanCategory.Ean13Product,
                            true,
                            "EAN-13 scan accepted for product routing."
                        );
                    }

                case ScanCategory.KhqrProfile:
                    {
                        return new ScanResult(
                            ScanCategory.KhqrProfile,
                            false,
                            "KHQR scan handling is not implemented yet."
                        );
                    }

                case ScanCategory.OfflineCoupon:
                    {
                        return new ScanResult(
                            ScanCategory.OfflineCoupon,
                            false,
                            "Offline coupon handling is not implemented yet."
                        );
                    }

                default:
                    {
                        return new ScanResult(
                            category,
                            false,
                            "Unsupported scan category."
                        );
                    }
            }
        }
        finally
        {
            _stateGate
                .Release();
        }
    }

    /// <summary>
    /// Selects a payment method.
    ///
    /// KHQR remains in AwaitingPayment.
    ///
    /// The cash path is retained for compatibility with current
    /// callers. Exact-cash processing should use
    /// BeginCashPaymentAsync so the engine receives a transaction
    /// total and exchange rate.
    /// </summary>
    public async Task SelectPaymentMethodAsync(
        PaymentMethod method,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(method))
        {
            throw new ArgumentOutOfRangeException(
                nameof(method),
                method,
                "The payment method is not supported."
            );
        }

        cancellationToken
            .ThrowIfCancellationRequested();

        await _stateGate
            .WaitAsync(
                cancellationToken
            )
            .ConfigureAwait(false);

        try
        {
            EnsurePaymentSelectionState();

            ClearCashSession();

            TransitionTo(
                KioskState.AwaitingPayment
            );

            if (
                method ==
                PaymentMethod.Cash
            )
            {
                _licenseManager
                    .EnforceFeatureAccess(
                        LicensedFeature.CashRecycler
                    );

                try
                {
                    await _cashRecycler
                        .ArmAcceptanceAsync(
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                    when (
                        cancellationToken
                            .IsCancellationRequested
                    )
                {
                    throw;
                }
                catch (Exception exception)
                {
                    TransitionTo(
                        KioskState.Faulted
                    );

                    OnHardwareFault?.Invoke(
                        this,
                        new HardwareFaultEventArgs(
                            "CashRecycler",
                            "Failed to arm cash acceptance: " +
                            exception.Message
                        )
                    );

                    throw;
                }

                TransitionTo(
                    KioskState.ProcessingCash
                );
            }
        }
        finally
        {
            _stateGate
                .Release();
        }
    }

    /// <summary>
    /// Starts one exact-cash USD/KHR payment session.
    ///
    /// The transaction total is expressed in USD. It is converted
    /// to a whole-KHR target using the supplied transaction exchange
    /// rate.
    ///
    /// Every accepted note is later normalized to KHR by
    /// CashAcceptancePolicy.
    /// </summary>
    public async Task BeginCashPaymentAsync(
        Money transactionTotalUsd,
        decimal usdToKhrRate,
        CancellationToken cancellationToken = default)
    {
        var targetKhr =
            CreateCashTargetKhr(
                transactionTotalUsd,
                usdToKhrRate
            );

        cancellationToken
            .ThrowIfCancellationRequested();

        await _stateGate
            .WaitAsync(
                cancellationToken
            )
            .ConfigureAwait(false);

        try
        {
            EnsurePaymentSelectionState();

            if (
                _cashEscrowController is
                null
            )
            {
                throw new InvalidOperationException(
                    "The configured cash device does not support " +
                    "software-controlled, physically confirmed escrow."
                );
            }

            if (
                _pendingCashEscrow is
                not null
            )
            {
                throw new InvalidOperationException(
                    "A cash payment cannot begin while another note " +
                    "is awaiting escrow resolution."
                );
            }

            _licenseManager
                .EnforceFeatureAccess(
                    LicensedFeature.CashRecycler
                );

            _cashTransactionTotalUsd =
                transactionTotalUsd;

            _cashTargetKhr =
                targetKhr;

            _acceptedCashKhr =
                Money.Khr(
                    0m
                );

            _usdToKhrRate =
                usdToKhrRate;

            TransitionTo(
                KioskState.AwaitingPayment
            );

            try
            {
                await _cashRecycler
                    .ArmAcceptanceAsync(
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (
                    cancellationToken
                        .IsCancellationRequested
                )
            {
                ClearCashSession();

                throw;
            }
            catch (Exception exception)
            {
                ClearCashSession();

                TransitionTo(
                    KioskState.Faulted
                );

                OnHardwareFault?.Invoke(
                    this,
                    new HardwareFaultEventArgs(
                        "CashRecycler",
                        "Failed to arm exact-cash acceptance: " +
                        exception.Message
                    )
                );

                throw;
            }

            TransitionTo(
                KioskState.ProcessingCash
            );

            PublishCashBalance();
        }
        finally
        {
            _stateGate
                .Release();
        }
    }

    /// <summary>
    /// Returns the engine to Idle from a safe state.
    /// </summary>
    public async Task ResetToIdleAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken
            .ThrowIfCancellationRequested();

        await _stateGate
            .WaitAsync(
                cancellationToken
            )
            .ConfigureAwait(false);

        try
        {
            if (
                _pendingCashEscrow is
                not null
            )
            {
                throw new InvalidOperationException(
                    "The kiosk cannot reset while a note is awaiting " +
                    "physical escrow resolution."
                );
            }

            if (
                CurrentState ==
                KioskState.Idle
            )
            {
                ClearCashSession();

                return;
            }

            if (
                CurrentState ==
                    KioskState.ProcessingCash ||
                CurrentState ==
                    KioskState.DispensingChange
            )
            {
                throw new InvalidOperationException(
                    $"The kiosk cannot reset while it is in the " +
                    $"{CurrentState} state."
                );
            }

            TransitionTo(
                KioskState.Idle
            );

            ClearCashSession();
        }
        finally
        {
            _stateGate
                .Release();
        }
    }

    /// <summary>
    /// Validates that payment selection is occurring from Scanning.
    /// </summary>
    private void EnsurePaymentSelectionState()
    {
        if (
            CurrentState !=
            KioskState.Scanning
        )
        {
            throw new InvalidOperationException(
                $"A payment method cannot be selected while " +
                $"the kiosk is in the {CurrentState} state."
            );
        }
    }

    /// <summary>
    /// Converts the USD transaction total into the exact whole-KHR
    /// cash target.
    /// </summary>
    private static Money CreateCashTargetKhr(
        Money transactionTotalUsd,
        decimal usdToKhrRate)
    {
        if (
            transactionTotalUsd.Currency !=
            CurrencyCode.Usd
        )
        {
            throw new ArgumentException(
                "The transaction total must use USD as its currency.",
                nameof(transactionTotalUsd)
            );
        }

        if (
            transactionTotalUsd.Amount <=
            0m
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(transactionTotalUsd),
                transactionTotalUsd.Amount,
                "The transaction total must be greater than zero."
            );
        }

        if (usdToKhrRate <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(usdToKhrRate),
                usdToKhrRate,
                "The USD-to-KHR exchange rate must be greater than zero."
            );
        }

        var targetAmountKhr =
            transactionTotalUsd.Amount *
            usdToKhrRate;

        if (
            decimal.Truncate(
                targetAmountKhr
            ) !=
            targetAmountKhr
        )
        {
            throw new ArgumentException(
                "The transaction total and exchange rate produce a " +
                "fractional KHR cash target. Exact-cash processing " +
                "requires a whole-KHR target.",
                nameof(usdToKhrRate)
            );
        }

        return Money.Khr(
            targetAmountKhr
        );
    }

    /// <summary>
    /// Applies one validated deterministic state transition.
    /// </summary>
    private void TransitionTo(
        KioskState nextState)
    {
        var previousState =
            CurrentState;

        if (
            previousState ==
            nextState
        )
        {
            return;
        }

        if (
            !IsTransitionAllowed(
                previousState,
                nextState
            )
        )
        {
            throw new InvalidOperationException(
                "Invalid kiosk-state transition: " +
                $"{previousState} -> {nextState}."
            );
        }

        CurrentState =
            nextState;

        OnStateChanged?.Invoke(
            this,
            new KioskStateChangedEventArgs(
                previousState,
                nextState
            )
        );
    }

    /// <summary>
    /// Defines the allowed deterministic state transitions.
    /// </summary>
    private static bool IsTransitionAllowed(
        KioskState previousState,
        KioskState nextState)
    {
        if (
            nextState ==
            KioskState.Faulted
        )
        {
            return previousState !=
                   KioskState.Faulted;
        }

        if (
            nextState ==
            KioskState.ExactCashOnlyLockout
        )
        {
            return
                previousState !=
                    KioskState.Faulted &&
                previousState !=
                    KioskState.ExactCashOnlyLockout;
        }

        return (
            previousState,
            nextState
        ) switch
        {
            (
                KioskState.Idle,
                KioskState.Scanning
            ) =>
                true,

            (
                KioskState.Scanning,
                KioskState.AwaitingPayment
            ) =>
                true,

            (
                KioskState.Scanning,
                KioskState.Idle
            ) =>
                true,

            (
                KioskState.AwaitingPayment,
                KioskState.ProcessingCash
            ) =>
                true,

            (
                KioskState.AwaitingPayment,
                KioskState.Idle
            ) =>
                true,

            (
                KioskState.ProcessingCash,
                KioskState.DispensingChange
            ) =>
                true,

            (
                KioskState.ProcessingCash,
                KioskState.TransactionComplete
            ) =>
                true,

            (
                KioskState.DispensingChange,
                KioskState.TransactionComplete
            ) =>
                true,

            (
                KioskState.TransactionComplete,
                KioskState.Idle
            ) =>
                true,

            (
                KioskState.ExactCashOnlyLockout,
                KioskState.Idle
            ) =>
                true,

            (
                KioskState.Faulted,
                KioskState.Idle
            ) =>
                true,

            _ =>
                false
        };
    }

    /// <summary>
    /// Attaches all engine-owned event handlers exactly once.
    /// </summary>
    private void SubscribeToEventsOnce()
    {
        if (_eventsSubscribed)
        {
            return;
        }

        _cashRecycler.OnNoteInEscrow +=
            HandleNoteInEscrow;

        _cashRecycler.OnFault +=
            HandleCashRecyclerFault;

        if (
            _cashEscrowController is
            not null
        )
        {
            _cashEscrowController.OnEscrowResolved +=
                HandleEscrowResolved;
        }

        _scanner.OnBarcodeScanned +=
            HandleBarcodeScanned;

        _printer.OnJobStatusChanged +=
            HandlePrintJobStatusChanged;

        _lowFloatMonitor.LowFloatStateTriggered +=
            HandleLowFloatStateTriggered;

        _lowFloatMonitor.LowFloatStateCleared +=
            HandleLowFloatStateCleared;

        _eventsSubscribed =
            true;
    }

    /// <summary>
    /// Handles one physical note entering escrow.
    ///
    /// The durable ESCROW append is synchronous and is the first
    /// processing operation performed for the note.
    /// </summary>
    private void HandleNoteInEscrow(
        object? sender,
        NoteInEscrowEventArgs eventArgs)
    {
        _ = sender;

        ArgumentNullException.ThrowIfNull(
            eventArgs
        );

        Guid auditId;

        try
        {
            auditId =
                _hardwareAppendLog
                    .AppendEscrow(
                        eventArgs.Note
                    );
        }
        catch (Exception exception)
        {
            EnqueueEventWork(
                () =>
                    ProcessEscrowAuditFailureAsync(
                        exception
                    )
            );

            return;
        }

        EnqueueEventWork(
            () =>
                ProcessEscrowedNoteAsync(
                    auditId,
                    eventArgs.Note
                )
        );
    }

    /// <summary>
    /// Receives confirmed physical escrow outcomes.
    /// </summary>
    private void HandleEscrowResolved(
        object? sender,
        CashEscrowResolvedEventArgs eventArgs)
    {
        _ = sender;

        ArgumentNullException.ThrowIfNull(
            eventArgs
        );

        EnqueueEventWork(
            () =>
                ProcessEscrowResolutionAsync(
                    eventArgs
                )
        );
    }

    /// <summary>
    /// Evaluates one durably audited escrowed note and sends the
    /// appropriate commit or reject command.
    /// </summary>
    private async Task ProcessEscrowedNoteAsync(
        Guid auditId,
        Money note)
    {
        await _stateGate
            .WaitAsync(
                CancellationToken.None
            )
            .ConfigureAwait(false);

        try
        {
            if (
                CurrentState !=
                KioskState.ProcessingCash
            )
            {
                await EnterCashFaultAsync(
                        "A note entered escrow while the kiosk was " +
                        $"in the {CurrentState} state."
                    )
                    .ConfigureAwait(false);

                return;
            }

            if (
                _cashTransactionTotalUsd is
                    null ||
                _cashTargetKhr is
                    null ||
                _usdToKhrRate is
                    null
            )
            {
                await EnterCashFaultAsync(
                        "A note entered escrow without an active " +
                        "exact-cash payment session."
                    )
                    .ConfigureAwait(false);

                return;
            }

            if (
                _cashEscrowController is
                null
            )
            {
                await EnterCashFaultAsync(
                        "The configured cash device cannot provide " +
                        "confirmed escrow control."
                    )
                    .ConfigureAwait(false);

                return;
            }

            if (
                _pendingCashEscrow is
                not null
            )
            {
                await EnterCashFaultAsync(
                        "A second note entered escrow before the " +
                        "previous note was physically resolved."
                    )
                    .ConfigureAwait(false);

                return;
            }

            CashAcceptanceResult result;

            try
            {
                result =
                    _cashAcceptancePolicy
                        .Evaluate(
                            _cashTargetKhr.Value,
                            _acceptedCashKhr,
                            note,
                            _usdToKhrRate.Value
                        );
            }
            catch (Exception exception)
            {
                await EnterCashFaultAsync(
                        "Cash acceptance policy evaluation failed: " +
                        exception.Message
                    )
                    .ConfigureAwait(false);

                return;
            }

            /*
             * Save correlation before sending the hardware command.
             *
             * The adapter may raise OnEscrowResolved synchronously
             * while executing the command.
             */
            _pendingCashEscrow =
                new PendingCashEscrow(
                    auditId,
                    note,
                    result
                );

            try
            {
                if (
                    result.Decision ==
                    CashAcceptanceDecision
                        .RejectOverpayment
                )
                {
                    await _cashRecycler
                        .RejectEscrowedNoteAsync(
                            CancellationToken.None
                        )
                        .ConfigureAwait(false);
                }
                else
                {
                    await _cashEscrowController
                        .CommitEscrowedNoteAsync(
                            CancellationToken.None
                        )
                        .ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                /*
                 * Keep the pending escrow correlation.
                 *
                 * A late physical resolution may still arrive and
                 * must be written to the audit log.
                 */
                await EnterCashFaultAsync(
                        "The escrow command failed: " +
                        exception.Message
                    )
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            _stateGate
                .Release();
        }
    }

    /// <summary>
    /// Applies one physically confirmed escrow resolution.
    ///
    /// Business balance changes occur only after physical
    /// confirmation.
    /// </summary>
    private async Task ProcessEscrowResolutionAsync(
        CashEscrowResolvedEventArgs eventArgs)
    {
        await _stateGate
            .WaitAsync(
                CancellationToken.None
            )
            .ConfigureAwait(false);

        try
        {
            var pending =
                _pendingCashEscrow;

            if (pending is null)
            {
                await EnterCashFaultAsync(
                        "The cash device reported an escrow resolution " +
                        "when no note was awaiting confirmation."
                    )
                    .ConfigureAwait(false);

                return;
            }

            if (
                pending.Note !=
                eventArgs.Note
            )
            {
                await EnterCashFaultAsync(
                        "The confirmed escrow note does not match the " +
                        "note currently awaiting resolution."
                    )
                    .ConfigureAwait(false);

                return;
            }

            /*
             * Record physical truth before applying business state.
             */
            try
            {
                switch (eventArgs.Resolution)
                {
                    case CashEscrowResolution.CommittedToVault:
                        {
                            _hardwareAppendLog
                                .AppendCommittedToVault(
                                    pending.AuditId,
                                    pending.Note
                                );

                            break;
                        }

                    case CashEscrowResolution.Rejected:
                        {
                            _hardwareAppendLog
                                .AppendRejected(
                                    pending.AuditId,
                                    pending.Note
                                );

                            break;
                        }

                    default:
                        {
                            throw new ArgumentOutOfRangeException(
                                nameof(eventArgs),
                                eventArgs.Resolution,
                                "Unsupported escrow resolution."
                            );
                        }
                }
            }
            catch (Exception exception)
            {
                await EnterCashFaultAsync(
                        "Failed to durably record the escrow " +
                        $"resolution: {exception.Message}"
                    )
                    .ConfigureAwait(false);

                return;
            }

            var expectedResolution =
                pending.Result.Decision ==
                CashAcceptanceDecision
                    .RejectOverpayment
                    ? CashEscrowResolution.Rejected
                    : CashEscrowResolution
                        .CommittedToVault;

            if (
                eventArgs.Resolution !=
                expectedResolution
            )
            {
                _pendingCashEscrow =
                    null;

                await EnterCashFaultAsync(
                        "The physical escrow outcome did not match " +
                        "the engine's cash acceptance decision."
                    )
                    .ConfigureAwait(false);

                return;
            }

            if (
                CurrentState !=
                KioskState.ProcessingCash
            )
            {
                _pendingCashEscrow =
                    null;

                await EnterCashFaultAsync(
                        "The escrow resolution completed while the " +
                        $"kiosk was in the {CurrentState} state."
                    )
                    .ConfigureAwait(false);

                return;
            }

            if (
                eventArgs.Resolution ==
                CashEscrowResolution.Rejected
            )
            {
                /*
                 * The rejected note never enters the accepted
                 * running total.
                 */
                _pendingCashEscrow =
                    null;

                PublishCashBalance();

                return;
            }

            /*
             * Only confirmed vault commitment changes the accepted
             * running total.
             */
            _acceptedCashKhr =
                pending.Result
                    .AcceptedPaidKhr;

            var paymentComplete =
                pending.Result
                    .IsPaymentComplete;

            _pendingCashEscrow =
                null;

            PublishCashBalance();

            if (!paymentComplete)
            {
                return;
            }

            try
            {
                await _cashRecycler
                    .DisarmAcceptanceAsync(
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                await EnterCashFaultAsync(
                        "Exact payment was confirmed, but cash " +
                        "acceptance could not be disarmed: " +
                        exception.Message
                    )
                    .ConfigureAwait(false);

                return;
            }

            /*
             * Acceptor-only configuration:
             *
             * No change is owed, so DispensingChange is skipped.
             */
            TransitionTo(
                KioskState.TransactionComplete
            );
        }
        finally
        {
            _stateGate
                .Release();
        }
    }

    /// <summary>
    /// Publishes the current exact-cash balance using the existing
    /// USD-based engine event contract.
    /// </summary>
    private void PublishCashBalance()
    {
        if (
            _cashTransactionTotalUsd is
                null ||
            _cashTargetKhr is
                null ||
            _usdToKhrRate is
                null
        )
        {
            return;
        }

        var rate =
            _usdToKhrRate.Value;

        var remainingKhr =
            Math.Max(
                0m,
                _cashTargetKhr.Value.Amount -
                _acceptedCashKhr.Amount
            );

        var tenderedUsd =
            _acceptedCashKhr.Amount /
            rate;

        var remainingUsd =
            remainingKhr /
            rate;

        OnBalanceChanged?.Invoke(
            this,
            new BalanceChangedEventArgs(
                _cashTransactionTotalUsd
                    .Value
                    .Amount,
                tenderedUsd,
                remainingUsd
            )
        );
    }

    /// <summary>
    /// Clears all transaction-specific exact-cash state.
    /// </summary>
    private void ClearCashSession()
    {
        _cashTransactionTotalUsd =
            null;

        _cashTargetKhr =
            null;

        _acceptedCashKhr =
            Money.Khr(
                0m
            );

        _usdToKhrRate =
            null;

        _pendingCashEscrow =
            null;
    }

    private void HandleCashRecyclerFault(
        object? sender,
        HardwareFaultEventArgs eventArgs)
    {
        _ = sender;

        ArgumentNullException.ThrowIfNull(
            eventArgs
        );

        EnqueueEventWork(
            () =>
                ProcessHardwareFaultAsync(
                    eventArgs
                )
        );
    }

    private void HandleBarcodeScanned(
        object? sender,
        BarcodeScannedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
    }

    private void HandlePrintJobStatusChanged(
        object? sender,
        PrintJobStatusEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
    }

    private void HandleLowFloatStateTriggered(
        object? sender,
        EventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;

        EnqueueEventWork(
            ProcessLowFloatStateTriggeredAsync
        );
    }

    private void HandleLowFloatStateCleared(
        object? sender,
        EventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;

        EnqueueEventWork(
            ProcessLowFloatStateClearedAsync
        );
    }

    /// <summary>
    /// Adds asynchronous hardware-event work to one serialized
    /// processing queue.
    /// </summary>
    private void EnqueueEventWork(
        Func<Task> work)
    {
        ArgumentNullException.ThrowIfNull(
            work
        );

        Task queuedTask;

        lock (_eventQueueSync)
        {
            queuedTask =
                RunQueuedEventWorkAsync(
                    _eventQueueTail,
                    work
                );

            _eventQueueTail =
                queuedTask;
        }

        _ = queuedTask.ContinueWith(
            static completedTask =>
            {
                _ =
                    completedTask.Exception;
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously |
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default
        );
    }

    /// <summary>
    /// Waits for the previous queued event before processing the
    /// next event.
    /// </summary>
    private static async Task RunQueuedEventWorkAsync(
        Task previousWork,
        Func<Task> currentWork)
    {
        try
        {
            await previousWork
                .ConfigureAwait(false);
        }
        catch
        {
            /*
             * Continue processing later hardware events even when a
             * previous queued operation failed.
             */
        }

        await currentWork()
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Stops further cash intake and faults the engine when an
    /// escrow record cannot be durably written.
    /// </summary>
    private async Task ProcessEscrowAuditFailureAsync(
        Exception auditException)
    {
        ArgumentNullException.ThrowIfNull(
            auditException
        );

        await _stateGate
            .WaitAsync(
                CancellationToken.None
            )
            .ConfigureAwait(false);

        try
        {
            await EnterCashFaultAsync(
                    "Failed to durably record an escrowed note: " +
                    auditException.Message,
                    device: "HardwareAppendLog"
                )
                .ConfigureAwait(false);
        }
        finally
        {
            _stateGate
                .Release();
        }
    }

    /// <summary>
    /// Stops cash acceptance and enters Faulted.
    ///
    /// The caller must already hold _stateGate.
    /// </summary>
    private async Task EnterCashFaultAsync(
        string message,
        string device = "CashRecycler")
    {
        var failureMessage =
            message;

        try
        {
            await _cashRecycler
                .StopAcceptingCashAsync(
                    CancellationToken.None
                )
                .ConfigureAwait(false);
        }
        catch (Exception stopException)
        {
            failureMessage +=
                " Cash acceptance could not be stopped: " +
                stopException.Message;
        }

        TransitionTo(
            KioskState.Faulted
        );

        OnHardwareFault?.Invoke(
            this,
            new HardwareFaultEventArgs(
                device,
                failureMessage
            )
        );
    }

    /// <summary>
    /// Handles the normal-to-low transition.
    /// </summary>
    private async Task ProcessLowFloatStateTriggeredAsync()
    {
        await _stateGate
            .WaitAsync(
                CancellationToken.None
            )
            .ConfigureAwait(false);

        try
        {
            if (
                CurrentState ==
                KioskState.Faulted
            )
            {
                return;
            }

            if (
                CurrentState ==
                KioskState.ExactCashOnlyLockout
            )
            {
                return;
            }

            try
            {
                await _cashRecycler
                    .StopAcceptingCashAsync(
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                TransitionTo(
                    KioskState.Faulted
                );

                OnHardwareFault?.Invoke(
                    this,
                    new HardwareFaultEventArgs(
                        "CashRecycler",
                        "Failed to stop cash acceptance during " +
                        $"low-float lockout: {exception.Message}"
                    )
                );

                return;
            }

            TransitionTo(
                KioskState.ExactCashOnlyLockout
            );

            LowFloatStateTriggered?.Invoke(
                this,
                EventArgs.Empty
            );
        }
        finally
        {
            _stateGate
                .Release();
        }
    }

    /// <summary>
    /// Republishes low-float recovery.
    /// </summary>
    private async Task ProcessLowFloatStateClearedAsync()
    {
        await _stateGate
            .WaitAsync(
                CancellationToken.None
            )
            .ConfigureAwait(false);

        try
        {
            LowFloatStateCleared?.Invoke(
                this,
                EventArgs.Empty
            );
        }
        finally
        {
            _stateGate
                .Release();
        }
    }

    /// <summary>
    /// Moves the engine to Faulted and forwards the original
    /// hardware-fault information.
    /// </summary>
    private async Task ProcessHardwareFaultAsync(
        HardwareFaultEventArgs eventArgs)
    {
        await _stateGate
            .WaitAsync(
                CancellationToken.None
            )
            .ConfigureAwait(false);

        try
        {
            TransitionTo(
                KioskState.Faulted
            );

            OnHardwareFault?.Invoke(
                this,
                eventArgs
            );
        }
        finally
        {
            _stateGate
                .Release();
        }
    }

    /// <summary>
    /// Correlates one durable ESCROW record with its policy decision
    /// until the physical device confirms the outcome.
    /// </summary>
    private sealed record PendingCashEscrow(
        Guid AuditId,
        Money Note,
        CashAcceptanceResult Result);
}

#pragma warning restore CS0067