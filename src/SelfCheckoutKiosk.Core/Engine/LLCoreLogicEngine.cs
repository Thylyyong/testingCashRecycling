using System.Threading;
using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Core.Currency;
using SelfCheckoutKiosk.Core.Licensing;
using SelfCheckoutKiosk.Domain.Entities;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Domain.ValueObjects;

namespace SelfCheckoutKiosk.Core.Engine;

/// <summary>
/// Deterministic state machine (Blueprint §3). The constructor wires its
/// collaborators via DI — Category 1 (Core) and Category 2 (HAL) meet here
/// through interfaces only, never concrete vendor adapters. This is the
/// SINGLE subscriber to every hardware event stream; nothing else in the
/// process may subscribe to <see cref="ICashRecycler"/> / <see cref="IBarcodeScanner"/>
/// events directly.
///
/// Flow: Idle -> Scanning -> AwaitingPayment -> [ProcessingCash]* ->
/// DispensingChange -> TransactionComplete -> Idle, with the parallel
/// ExactCashOnlyLockout branch (low float forces exact-cash-or-digital) and
/// Faulted (any hardware fault, terminal until <see cref="ResetToIdleAsync"/>).
/// </summary>
public sealed class LLCoreLogicEngine : ILLCoreLogicEngine
{
    /// <summary>Sprint-0 offline exchange rate. TODO(Back-End): source from a
    /// cached, versioned, max-age-checked rate rather than a constant.</summary>
    private const decimal UsdToKhrRateConst = 4100m;

    private readonly ICashRecycler _cashRecycler;
    private readonly IBarcodeScanner _scanner;
    private readonly IReceiptPrinter _printer;
    private readonly DualCurrencyCalculator _calculator;
    private readonly LowFloatMonitor _lowFloatMonitor;
    private readonly OfflineLicenseManager _licenseManager;
    private readonly HardwareAppendLog _hardwareAppendLog;
    private readonly IProductCatalog _productCatalog;
    private readonly Lock _gate = new();

    private Transaction _transaction = new();
    private KioskState _stateBeforeLockout = KioskState.AwaitingPayment;
    private bool _scannerConnected;
    private bool _cashRecyclerConnected;
    private bool _printerConnected;

    public LLCoreLogicEngine(
        ICashRecycler cashRecycler,
        IBarcodeScanner scanner,
        IReceiptPrinter printer,
        DualCurrencyCalculator calculator,
        LowFloatMonitor lowFloatMonitor,
        OfflineLicenseManager licenseManager,
        HardwareAppendLog hardwareAppendLog,
        IProductCatalog? productCatalog = null)
    {
        _cashRecycler = cashRecycler;
        _scanner = scanner;
        _printer = printer;
        _calculator = calculator;
        _lowFloatMonitor = lowFloatMonitor;
        _licenseManager = licenseManager;
        _hardwareAppendLog = hardwareAppendLog;
        // TODO(Back-End): composition root should inject the EF-Core-backed
        // catalog once KioskDbContext syncs product data; this empty fallback
        // makes every EAN-13 scan resolve to "not found" until then.
        _productCatalog = productCatalog ?? EmptyProductCatalog.Instance;
    }

    public KioskState CurrentState { get; private set; } = KioskState.Idle;

    public decimal UsdToKhrRate => UsdToKhrRateConst;

    public ChangeBreakdown? LastChangeBreakdown { get; private set; }

    public event EventHandler<KioskStateChangedEventArgs>? OnStateChanged;
    public event EventHandler<ProductAddedEventArgs>? OnProductAdded;
    public event EventHandler<BalanceChangedEventArgs>? OnBalanceChanged;
    public event EventHandler<NoteProcessedEventArgs>? OnNoteProcessed;
    public event EventHandler? LowFloatStateTriggered;
    public event EventHandler? LowFloatStateCleared;
    public event EventHandler<HardwareFaultEventArgs>? OnHardwareFault;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // License gate BEFORE any hardware is touched — failure is a hard stop.
        await _licenseManager.LoadAndValidateAsync(cancellationToken).ConfigureAwait(false);
        _licenseManager.EnforceFeatureAccess(LicensedFeature.CashRecycler);

        await _scanner.ConnectAsync(cancellationToken).ConfigureAwait(false);
        _scannerConnected = true;
        await _cashRecycler.ConnectAsync(cancellationToken).ConfigureAwait(false);
        _cashRecyclerConnected = true;
        await _printer.ConnectAsync(cancellationToken).ConfigureAwait(false);
        _printerConnected = true;

        // Single-subscriber wiring — the engine is the only place these HAL
        // event streams are observed FOR BUSINESS LOGIC. HardwareAppendLog
        // also taps OnNoteInEscrow directly (see its own doc comment) —
        // that's a passive durability recorder, not a second dispatcher.
        _scanner.OnBarcodeScanned += HandleBarcodeScanned;
        _cashRecycler.OnNoteInEscrow += HandleNoteInEscrow;
        _cashRecycler.OnFault += HandleHardwareFault;
        _cashRecycler.OnCassetteInventoryChanged += HandleCassetteInventoryChanged;
        _lowFloatMonitor.LowFloatStateTriggered += HandleLowFloatTriggered;
        _lowFloatMonitor.LowFloatStateCleared += HandleLowFloatCleared;
        _hardwareAppendLog.Attach(_cashRecycler);

        TransitionTo(KioskState.Idle);
    }

    public Task<ScanResult> SubmitScanAsync(string rawScan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rawScan);

        lock (_gate)
        {
            if (CurrentState is not (KioskState.Idle or KioskState.Scanning))
                return Task.FromResult(new ScanResult(ScanCategory.Unknown, false,
                    $"Scans are not accepted in state '{CurrentState}'."));

            ScanCategory category = RegexRouter.Classify(rawScan);
            return Task.FromResult(category switch
            {
                ScanCategory.Ean13Product => HandleProductScan(rawScan),
                ScanCategory.OfflineCoupon => new ScanResult(category, false,
                    "Coupon redemption pending catalog/coupon-service integration."),
                ScanCategory.KhqrProfile => new ScanResult(category, false,
                    "KHQR profile recognized, but no payment is awaiting confirmation yet."),
                _ => new ScanResult(ScanCategory.Unknown, false, "Unrecognized scan."),
            });
        }
    }

    public Task SelectPaymentMethodAsync(PaymentMethod method, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (CurrentState != KioskState.Scanning)
                throw new InvalidOperationException(
                    $"Cannot select a payment method from state '{CurrentState}'; scan at least one item first.");
            if (_transaction.LineItems.Count == 0)
                throw new InvalidOperationException("Cannot select a payment method with an empty cart.");

            _transaction.PaymentMethod = method;

            if (method == PaymentMethod.Cash && _lowFloatMonitor.IsLow)
            {
                // Low float: still accept cash, but only exact amounts — the
                // kiosk cannot promise change it doesn't hold.
                _stateBeforeLockout = KioskState.AwaitingPayment;
                TransitionTo(KioskState.ExactCashOnlyLockout);
            }
            else
            {
                TransitionTo(KioskState.AwaitingPayment);
            }
        }

        return method == PaymentMethod.Cash
            ? _cashRecycler.ArmAcceptanceAsync(cancellationToken)
            : Task.CompletedTask;
    }

    public async Task ResetToIdleAsync(CancellationToken cancellationToken = default)
    {
        await _cashRecycler.DisarmAcceptanceAsync(cancellationToken).ConfigureAwait(false);

        lock (_gate)
        {
            _transaction = new Transaction();
            LastChangeBreakdown = null;
            TransitionTo(KioskState.Idle);
        }
    }

    public Task<AdminDiagnosticsSnapshot> GetDiagnosticsSnapshotAsync(CancellationToken cancellationToken = default)
    {
        string licenseTier = _licenseManager.IsValidated ? _licenseManager.Tier.ToString() : "Unknown";

        var snapshot = new AdminDiagnosticsSnapshot(
            CashRecyclerConnected: _cashRecyclerConnected,
            BarcodeScannerConnected: _scannerConnected,
            ReceiptPrinterConnected: _printerConnected,
            KhrCassetteCounts: _lowFloatMonitor.CurrentCounts,
            UnresolvedHardwareAppendLogEntries: _hardwareAppendLog.UnresolvedEntriesAtStartup,
            LicenseTier: licenseTier,
            // TODO(Lead): expiry isn't exposed by OfflineLicenseManager yet —
            // surface it once that type grows a public accessor (Core/Licensing
            // is Lead-owned; not modified here).
            LicenseExpiresAtUtc: null,
            // TODO(Back-End): source from TailscaleSyncWorker once it exposes a
            // last-successful-sync timestamp the Core layer can read.
            LastSyncTimestampUtc: null);

        return Task.FromResult(snapshot);
    }

    // ---- scan handling -----------------------------------------------------

    private ScanResult HandleProductScan(string rawScan)
    {
        Product? product = _productCatalog.FindByEan13(rawScan);
        if (product is null)
            return new ScanResult(ScanCategory.Ean13Product, false, $"No product found for '{rawScan}'.");

        _transaction.LineItems.Add(new LineItem
        {
            Ean13 = product.Ean13,
            Description = product.Description,
            UnitPriceUsd = product.UsdPrice,
            Quantity = 1,
        });
        _transaction.TotalUsd += product.UsdPrice;

        if (CurrentState == KioskState.Idle)
            TransitionTo(KioskState.Scanning);

        OnProductAdded?.Invoke(this, new ProductAddedEventArgs(product, _transaction.TotalUsd));
        OnBalanceChanged?.Invoke(this, new BalanceChangedEventArgs(_transaction.TotalUsd, _transaction.TenderedUsd,
            Math.Max(0m, _transaction.TotalUsd - _transaction.TenderedUsd)));

        return new ScanResult(ScanCategory.Ean13Product, true);
    }

    // ---- hardware event handlers (single-subscriber seam) ------------------

    private async void HandleBarcodeScanned(object? sender, BarcodeScannedEventArgs e)
    {
        try
        {
            await SubmitScanAsync(e.RawBarcode).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            OnHardwareFault?.Invoke(this, new HardwareFaultEventArgs("BarcodeScanner", ex.Message));
        }
    }

    private async void HandleNoteInEscrow(object? sender, NoteInEscrowEventArgs e)
    {
        try
        {
            await OnNoteAcceptedAsync(e.Note).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            OnHardwareFault?.Invoke(this, new HardwareFaultEventArgs("CashRecycler", ex.Message));
        }
    }

    private async Task OnNoteAcceptedAsync(Money note)
    {
        bool exactCashOnly;
        decimal remainingBeforeNote;

        // Normalize note amount to USD for balance comparison and ledger recording
        decimal noteAmountUsd = note.Currency == CurrencyCode.Usd
            ? note.Amount
            : (UsdToKhrRate > 0 ? note.Amount / UsdToKhrRate : note.Amount);

        lock (_gate)
        {
            if (CurrentState is not (KioskState.AwaitingPayment or KioskState.ExactCashOnlyLockout))
                return; // stray escrow event outside a cash-tender window; ignore.

            exactCashOnly = CurrentState == KioskState.ExactCashOnlyLockout;
            remainingBeforeNote = Math.Max(0m, _transaction.TotalUsd - _transaction.TenderedUsd);
        }

        if (exactCashOnly && noteAmountUsd > remainingBeforeNote)
        {
            // Would require change we cannot dispense — reject the note.
            await _cashRecycler.RejectEscrowedNoteAsync().ConfigureAwait(false);
            _hardwareAppendLog.LogRejected(note, "exceeds remaining balance under exact-cash-only lockout");
            OnNoteProcessed?.Invoke(this, new NoteProcessedEventArgs(note, accepted: false,
                _transaction.TenderedUsd, remainingBeforeNote));
            return;
        }

        decimal remainingAfterNote;
        lock (_gate)
        {
            TransitionTo(KioskState.ProcessingCash);
            _transaction.TenderedUsd += noteAmountUsd;
            _hardwareAppendLog.LogCommittedToVault(note);

            decimal remaining = Math.Max(0m, _transaction.TotalUsd - _transaction.TenderedUsd);
            remainingAfterNote = remaining;
            OnBalanceChanged?.Invoke(this, new BalanceChangedEventArgs(_transaction.TotalUsd, _transaction.TenderedUsd, remaining));
        }

        OnNoteProcessed?.Invoke(this, new NoteProcessedEventArgs(note, accepted: true,
            _transaction.TenderedUsd, remainingAfterNote));

        lock (_gate)
        {
            if (remainingAfterNote > 0)
            {
                TransitionTo(exactCashOnly ? KioskState.ExactCashOnlyLockout : KioskState.AwaitingPayment);
                return;
            }
        }

        await CompleteCashTenderAsync().ConfigureAwait(false);
    }

    private async Task CompleteCashTenderAsync()
    {
        ChangeBreakdown change;
        lock (_gate)
        {
            TransitionTo(KioskState.DispensingChange);
            change = _calculator.CalculateChange(_transaction.TotalUsd, _transaction.TenderedUsd, UsdToKhrRate);
            LastChangeBreakdown = change;
        }

        _hardwareAppendLog.LogFloatGain(change.DiscardedKhrRemainder);

        await _cashRecycler.DisarmAcceptanceAsync().ConfigureAwait(false);

        if (change.UsdNotes.Count > 0 || change.KhrNotes.Count > 0)
        {
            DispenseResult result = await _cashRecycler.DispenseAsync(change).ConfigureAwait(false);
            if (!result.Success)
            {
                OnHardwareFault?.Invoke(this, new HardwareFaultEventArgs("CashRecycler", "Change dispense failed."));
                lock (_gate) TransitionTo(KioskState.Faulted);
                return;
            }
        }

        lock (_gate)
        {
            _transaction.SyncStatus = SyncStatus.Pending; // committed locally; Tailscale sync worker picks it up.
            TransitionTo(KioskState.TransactionComplete);
        }
    }

    private void HandleCassetteInventoryChanged(object? sender, CassetteInventoryChangedEventArgs e)
    {
        foreach ((int denominationKhr, int count) in e.CountsByKhrDenomination)
            _lowFloatMonitor.UpdateCount(denominationKhr, count);
    }

    private void HandleHardwareFault(object? sender, HardwareFaultEventArgs e)
    {
        lock (_gate) TransitionTo(KioskState.Faulted);
        OnHardwareFault?.Invoke(this, e);
    }

    private void HandleLowFloatTriggered(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (CurrentState == KioskState.AwaitingPayment && _transaction.PaymentMethod == PaymentMethod.Cash)
            {
                _stateBeforeLockout = KioskState.AwaitingPayment;
                TransitionTo(KioskState.ExactCashOnlyLockout);
            }
        }
        LowFloatStateTriggered?.Invoke(this, EventArgs.Empty);
    }

    private void HandleLowFloatCleared(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (CurrentState == KioskState.ExactCashOnlyLockout)
                TransitionTo(_stateBeforeLockout);
        }
        LowFloatStateCleared?.Invoke(this, EventArgs.Empty);
    }

    // ---- state transition helper -------------------------------------------

    private void TransitionTo(KioskState next)
    {
        if (next == CurrentState) return;
        KioskState previous = CurrentState;
        CurrentState = next;
        OnStateChanged?.Invoke(this, new KioskStateChangedEventArgs(previous, next));
    }

    private sealed class EmptyProductCatalog : IProductCatalog
    {
        public static readonly EmptyProductCatalog Instance = new();
        public Product? FindByEan13(string ean13) => null;
    }
}
