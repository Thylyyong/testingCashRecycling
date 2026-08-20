using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Core.Currency;
using SelfCheckoutKiosk.Core.Engine;
using SelfCheckoutKiosk.Core.Licensing;
using SelfCheckoutKiosk.Domain.Entities;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Domain.ValueObjects;
using Xunit;

namespace SelfCheckoutKiosk.Core.Tests;

public sealed class LLCoreLogicEngineTests : IDisposable
{
    private const string TestEan13 = "4006381333931";

    private readonly string _logPath = Path.Combine(Path.GetTempPath(), $"engine-hwlog-{Guid.NewGuid():N}.tsv");
    private readonly FakeCashRecycler _cashRecycler = new();
    private readonly FakeBarcodeScanner _scanner = new();
    private readonly FakePrinter _printer = new();
    private readonly FakeProductCatalog _catalog = new();
    private readonly LLCoreLogicEngine _engine;

    public LLCoreLogicEngineTests()
    {
        _catalog.Add(new Product { Ean13 = TestEan13, Description = "Test Widget", UsdPrice = 9.5m, KhrPrice = 38_950m });

        _engine = new LLCoreLogicEngine(
            _cashRecycler,
            _scanner,
            _printer,
            new DualCurrencyCalculator(),
            new LowFloatMonitor(),
            new OfflineLicenseManager(),
            new HardwareAppendLog(_logPath),
            _catalog);
    }

    /// <summary>Builds and <see cref="ILLCoreLogicEngine.InitializeAsync"/>-wires
    /// an engine, so tests that rely on the engine subscribing to HAL events
    /// (cash escrow, hardware faults) exercise the real wiring rather than
    /// raising fake events into thin air. Needs a validly-signed license token
    /// since <see cref="OfflineLicenseManager.LoadAndValidateAsync"/> gates
    /// <see cref="ILLCoreLogicEngine.InitializeAsync"/> before any hardware
    /// connects (Blueprint §6 step 7) — see <see cref="TestLicenseTokenFactory"/>.</summary>
    private async Task<(LLCoreLogicEngine Engine, FakeCashRecycler CashRecycler)> CreateInitializedEngineAsync(
        LowFloatMonitor? lowFloatMonitor = null)
    {
        string tokenPath = Path.Combine(Path.GetTempPath(), $"engine-license-{Guid.NewGuid():N}.token");
        byte[] publicKey = TestLicenseTokenFactory.WriteValidToken(tokenPath);

        var cashRecycler = new FakeCashRecycler();
        var engine = new LLCoreLogicEngine(
            cashRecycler,
            new FakeBarcodeScanner(),
            new FakePrinter(),
            new DualCurrencyCalculator(),
            lowFloatMonitor ?? new LowFloatMonitor(),
            new OfflineLicenseManager(tokenPath, publicKey),
            new HardwareAppendLog(Path.Combine(Path.GetTempPath(), $"engine-hwlog-{Guid.NewGuid():N}.tsv")),
            _catalog);

        await engine.InitializeAsync();
        return (engine, cashRecycler);
    }

    [Fact]
    public void InitialState_IsIdle()
        => Assert.Equal(KioskState.Idle, _engine.CurrentState);

    [Fact]
    public async Task SubmitScanAsync_KnownProduct_TransitionsToScanningAndRaisesEvents()
    {
        ProductAddedEventArgs? productAdded = null;
        _engine.OnProductAdded += (_, e) => productAdded = e;

        var result = await _engine.SubmitScanAsync(TestEan13);

        Assert.True(result.Accepted);
        Assert.Equal(ScanCategory.Ean13Product, result.Category);
        Assert.Equal(KioskState.Scanning, _engine.CurrentState);
        Assert.NotNull(productAdded);
        Assert.Equal(9.5m, productAdded!.RunningTotalUsd);
    }

    [Fact]
    public async Task SubmitScanAsync_UnknownProduct_RejectsWithoutStateChange()
    {
        var result = await _engine.SubmitScanAsync("0000000000000");

        Assert.False(result.Accepted);
        Assert.Equal(KioskState.Idle, _engine.CurrentState);
    }

    [Fact]
    public async Task SelectPaymentMethodAsync_WithEmptyCart_Throws()
        => await Assert.ThrowsAsync<InvalidOperationException>(
            () => _engine.SelectPaymentMethodAsync(PaymentMethod.Cash));

    [Fact]
    public async Task SelectPaymentMethodAsync_Cash_ArmsAcceptanceAndTransitions()
    {
        await _engine.SubmitScanAsync(TestEan13);
        await _engine.SelectPaymentMethodAsync(PaymentMethod.Cash);

        Assert.Equal(KioskState.AwaitingPayment, _engine.CurrentState);
        Assert.True(_cashRecycler.AcceptanceArmed);
    }

    [Fact]
    public async Task SelectPaymentMethodAsync_CashWithLowFloat_EntersExactCashOnlyLockout()
    {
        var lowFloatMonitor = new LowFloatMonitor();
        lowFloatMonitor.UpdateCount(1000, 0); // trips low immediately

        var engine = new LLCoreLogicEngine(
            _cashRecycler, _scanner, _printer, new DualCurrencyCalculator(), lowFloatMonitor,
            new OfflineLicenseManager(), new HardwareAppendLog(_logPath + ".lockout"), _catalog);

        await engine.SubmitScanAsync(TestEan13);
        await engine.SelectPaymentMethodAsync(PaymentMethod.Cash);

        Assert.Equal(KioskState.ExactCashOnlyLockout, engine.CurrentState);
    }

    [Fact]
    public async Task CashTender_ExactAmount_CompletesTransactionWithNoChange()
    {
        (LLCoreLogicEngine engine, FakeCashRecycler cashRecycler) = await CreateInitializedEngineAsync();

        await engine.SubmitScanAsync(TestEan13); // $9.50 total
        await engine.SelectPaymentMethodAsync(PaymentMethod.Cash);

        cashRecycler.RaiseNoteInEscrow(Money.Usd(9.5m));
        await WaitForStateAsync(engine, KioskState.TransactionComplete);

        Assert.Equal(KioskState.TransactionComplete, engine.CurrentState);
        Assert.NotNull(engine.LastChangeBreakdown);
        Assert.Empty(engine.LastChangeBreakdown!.UsdNotes);
        Assert.Empty(engine.LastChangeBreakdown!.KhrNotes);
    }

    [Fact]
    public async Task CashTender_Overpayment_DispensesChangeAndCompletes()
    {
        (LLCoreLogicEngine engine, FakeCashRecycler cashRecycler) = await CreateInitializedEngineAsync();

        await engine.SubmitScanAsync(TestEan13); // $9.50 total
        await engine.SelectPaymentMethodAsync(PaymentMethod.Cash);

        cashRecycler.RaiseNoteInEscrow(Money.Usd(10m)); // $0.50 change due
        await WaitForStateAsync(engine, KioskState.TransactionComplete);

        Assert.Equal(KioskState.TransactionComplete, engine.CurrentState);
        Assert.NotNull(engine.LastChangeBreakdown);
        Assert.NotEmpty(engine.LastChangeBreakdown!.KhrNotes);
        Assert.True(cashRecycler.DispenseCallCount >= 1);
    }

    [Fact]
    public async Task CashTender_PartialPayment_StaysAwaitingPayment()
    {
        (LLCoreLogicEngine engine, FakeCashRecycler cashRecycler) = await CreateInitializedEngineAsync();

        await engine.SubmitScanAsync(TestEan13); // $9.50 total
        await engine.SelectPaymentMethodAsync(PaymentMethod.Cash);

        cashRecycler.RaiseNoteInEscrow(Money.Usd(5m));
        await Task.Delay(50);

        Assert.Equal(KioskState.AwaitingPayment, engine.CurrentState);
        Assert.Null(engine.LastChangeBreakdown);
    }

    [Fact]
    public async Task ResetToIdleAsync_ClearsTransactionAndChangeBreakdown()
    {
        (LLCoreLogicEngine engine, FakeCashRecycler cashRecycler) = await CreateInitializedEngineAsync();

        await engine.SubmitScanAsync(TestEan13);
        await engine.SelectPaymentMethodAsync(PaymentMethod.Cash);
        cashRecycler.RaiseNoteInEscrow(Money.Usd(9.5m));
        await WaitForStateAsync(engine, KioskState.TransactionComplete);

        await engine.ResetToIdleAsync();

        Assert.Equal(KioskState.Idle, engine.CurrentState);
        Assert.Null(engine.LastChangeBreakdown);
    }

    [Fact]
    public async Task HardwareFault_TransitionsToFaulted()
    {
        (LLCoreLogicEngine engine, FakeCashRecycler cashRecycler) = await CreateInitializedEngineAsync();

        cashRecycler.RaiseFault("simulated fault");

        Assert.Equal(KioskState.Faulted, engine.CurrentState);
    }

    [Fact]
    public async Task GetDiagnosticsSnapshotAsync_BeforeInitialize_ReportsDisconnectedAndUnknownLicense()
    {
        var snapshot = await _engine.GetDiagnosticsSnapshotAsync();

        Assert.False(snapshot.CashRecyclerConnected);
        Assert.False(snapshot.BarcodeScannerConnected);
        Assert.False(snapshot.ReceiptPrinterConnected);
        Assert.Equal("Unknown", snapshot.LicenseTier);
        Assert.Null(snapshot.LicenseExpiresAtUtc);
        Assert.Equal(0, snapshot.UnresolvedHardwareAppendLogEntries);
    }

    [Fact]
    public async Task OfflineLicenseManager_ValidatesGeneratedLicenseToken()
    {
        string tokenPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "license.token");
        if (File.Exists(tokenPath))
        {
            var mgr = new OfflineLicenseManager(
                tokenFilePath: tokenPath,
                publicKeySubjectPublicKeyInfo: DevLicenseKeys.PublicKeyBytes);

            await mgr.LoadAndValidateAsync();

            Assert.True(mgr.IsValidated);
            Assert.Equal(LicenseTier.Enterprise, mgr.Tier);
        }
    }

    public void Dispose()
    {
        foreach (string path in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(_logPath) + "*"))
        {
            try { File.Delete(path); } catch { /* best-effort cleanup */ }
        }
    }

    private static async Task WaitForStateAsync(ILLCoreLogicEngine engine, KioskState expected, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (engine.CurrentState != expected && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.Equal(expected, engine.CurrentState);
    }

    private sealed class FakeProductCatalog : IProductCatalog
    {
        private readonly Dictionary<string, Product> _products = [];
        public void Add(Product product) => _products[product.Ean13] = product;
        public Product? FindByEan13(string ean13) => _products.GetValueOrDefault(ean13);
    }

    private sealed class FakeBarcodeScanner : IBarcodeScanner
    {
        public bool IsConnected => true;
        public event EventHandler<BarcodeScannedEventArgs>? OnBarcodeScanned;
        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Raise(string raw) => OnBarcodeScanned?.Invoke(this, new BarcodeScannedEventArgs(raw));
    }

#pragma warning disable CS0067 // raised by the interface contract but unused by these tests
    private sealed class FakePrinter : IReceiptPrinter
    {
        public bool IsConnected => true;
        public event EventHandler<PrintJobStatusEventArgs>? OnJobStatusChanged;
        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> IsPaperPresentAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task PrintRawAsync(ReadOnlyMemory<byte> escPosPayload, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeCashRecycler : ICashRecycler
    {
        public bool AcceptanceArmed { get; private set; }
        public int DispenseCallCount { get; private set; }

        public event EventHandler<NoteInsertedEventArgs>? OnNoteInserted;
        public event EventHandler<NoteInEscrowEventArgs>? OnNoteInEscrow;
        public event EventHandler<HardwareFaultEventArgs>? OnFault;
        public event EventHandler<CashAcceptorStateChangedEventArgs>? OnAcceptorStateChanged;
        public event EventHandler<CashRecyclerJamEventArgs>? OnJam;
        public event EventHandler<CassetteInventoryChangedEventArgs>? OnCassetteInventoryChanged;

        public void RaiseNoteInEscrow(Money note) => OnNoteInEscrow?.Invoke(this, new NoteInEscrowEventArgs(note));
        public void RaiseFault(string message) => OnFault?.Invoke(this, new HardwareFaultEventArgs("CashRecycler", message));

        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ArmAcceptanceAsync(CancellationToken cancellationToken = default)
        {
            AcceptanceArmed = true;
            return Task.CompletedTask;
        }
        public Task DisarmAcceptanceAsync(CancellationToken cancellationToken = default)
        {
            AcceptanceArmed = false;
            return Task.CompletedTask;
        }
        public Task StopAcceptingCashAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<DispenseResult> DispenseAsync(ChangeBreakdown change, CancellationToken cancellationToken = default)
        {
            DispenseCallCount++;
            return Task.FromResult(new DispenseResult(true, change));
        }
        public Task RejectEscrowedNoteAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
#pragma warning restore CS0067
}
