using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Core.Currency;
using SelfCheckoutKiosk.Core.Licensing;
using SelfCheckoutKiosk.Domain.Enums;

namespace SelfCheckoutKiosk.Core.Engine;

// TODO(Back-End): remove this suppression once the state machine actually
// raises these events. They are declared now so ViewModels can bind on Day 1.
#pragma warning disable CS0067 // Event is declared but never raised (stub)

/// <summary>
/// STUB — deterministic state machine (Blueprint §3). The constructor wires
/// its collaborators via DI (this is where Category 1 + Category 2 meet — the
/// engine receives interfaces, never concrete vendor adapters). Behaviour is
/// intentionally unimplemented for Sprint 0.
///
/// TODO(Back-End): implement the Idle -> Scanning -> AwaitingPayment ->
/// ProcessingCash -> DispensingChange -> TransactionComplete flow, plus the
/// parallel ExactCashOnlyLockout and Faulted branches. Subscribe to HAL
/// events here and nowhere else.
/// </summary>
public sealed class LLCoreLogicEngine : ILLCoreLogicEngine
{
    private readonly ICashRecycler _cashRecycler;
    private readonly IBarcodeScanner _scanner;
    private readonly IReceiptPrinter _printer;
    private readonly DualCurrencyCalculator _calculator;
    private readonly LowFloatMonitor _lowFloatMonitor;
    private readonly OfflineLicenseManager _licenseManager;

    public LLCoreLogicEngine(
        ICashRecycler cashRecycler,
        IBarcodeScanner scanner,
        IReceiptPrinter printer,
        DualCurrencyCalculator calculator,
        LowFloatMonitor lowFloatMonitor,
        OfflineLicenseManager licenseManager)
    {
        _cashRecycler = cashRecycler;
        _scanner = scanner;
        _printer = printer;
        _calculator = calculator;
        _lowFloatMonitor = lowFloatMonitor;
        _licenseManager = licenseManager;
    }

    public KioskState CurrentState { get; private set; } = KioskState.Idle;

    public event EventHandler<KioskStateChangedEventArgs>? OnStateChanged;
    public event EventHandler<ProductAddedEventArgs>? OnProductAdded;
    public event EventHandler<BalanceChangedEventArgs>? OnBalanceChanged;
    public event EventHandler? LowFloatStateTriggered;
    public event EventHandler? LowFloatStateCleared;
    public event EventHandler<HardwareFaultEventArgs>? OnHardwareFault;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => throw new NotImplementedException("TODO(Back-End): license-gate + connect HAL, then subscribe to events.");

    public Task<ScanResult> SubmitScanAsync(string rawScan, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("TODO(Back-End): route via RegexRouter and dispatch.");

    public Task SelectPaymentMethodAsync(PaymentMethod method, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("TODO(Back-End): transition to AwaitingPayment / arm cash.");

    public Task ResetToIdleAsync(CancellationToken cancellationToken = default)
        => throw new NotImplementedException("TODO(Back-End): tear down transaction, return to Idle.");
}
