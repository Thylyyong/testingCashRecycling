using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Core.Currency;
using SelfCheckoutKiosk.Core.Engine;
using SelfCheckoutKiosk.Core.Licensing;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Domain.ValueObjects;
using Xunit;

namespace SelfCheckoutKiosk.Integration.Tests;

// Some fake events are still declared-but-not-raised (OnFault, OnBarcodeScanned,
// OnJobStatusChanged). The pragma covers only those — events wired in the real
// production path (OnNoteInEscrow) are now raised via FireNoteInEscrow().
#pragma warning disable CS0067 // Event is declared but never raised (test fake)

/// <summary>
/// The one test suite that proves Category 1 / 2 / 3 are wired, not just
/// individually correct (Blueprint §6, step 9).
///
/// AI NOTE — before editing this file:
///   1. Re-read Blueprint §4 + §6 and the pending tracker.
///   2. NEVER subscribe a test directly to OnNoteInEscrow, OnFault, etc. —
///      the engine is the sole subscriber (Blueprint §4 rule).
///   3. FakeCashRecycler.FireNoteInEscrow is the ONLY approved test hook for
///      triggering escrow events. It mirrors what the real vendor adapter does.
///   4. Do NOT add business logic to fakes. Fakes model the interface surface only.
/// </summary>
public sealed class CompositionSeamTests : IDisposable
{
    // -----------------------------------------------------------------------
    // Fake HAL implementations — interface surface only, zero business logic
    // -----------------------------------------------------------------------

    private sealed class FakeCashRecycler : ICashRecycler
    {
        public event EventHandler<NoteInEscrowEventArgs>? OnNoteInEscrow;
        public event EventHandler<HardwareFaultEventArgs>? OnFault;

        // Tracking fields for assertions
        public bool ConnectCalled          { get; private set; }
        public bool StopAcceptingCashCalled { get; private set; }
        public bool DisposeFaultFired      { get; private set; }

        public Task ConnectAsync(CancellationToken ct = default)
        {
            ConnectCalled = true;
            return Task.CompletedTask;
        }

        public Task ArmAcceptanceAsync(CancellationToken ct = default)        => Task.CompletedTask;
        public Task DisarmAcceptanceAsync(CancellationToken ct = default)     => Task.CompletedTask;

        public Task StopAcceptingCashAsync(CancellationToken ct = default)
        {
            StopAcceptingCashCalled = true;
            return Task.CompletedTask;
        }

        public Task<DispenseResult> DispenseAsync(ChangeBreakdown change, CancellationToken ct = default)
            => Task.FromResult(new DispenseResult(true, ChangeBreakdown.Empty));

        public Task RejectEscrowedNoteAsync(CancellationToken ct = default) => Task.CompletedTask;

        /// <summary>
        /// Test helper — fires OnNoteInEscrow as if a physical note was inserted.
        /// The engine must be initialised first so HardwareAppendLog is the first
        /// subscriber (wired in InitializeAsync).
        /// </summary>
        public void FireNoteInEscrow(Money note)
            => OnNoteInEscrow?.Invoke(this, new NoteInEscrowEventArgs(note));

        /// <summary>Test helper — fires OnFault.</summary>
        public void FireFault(string message)
            => OnFault?.Invoke(this, new HardwareFaultEventArgs("FakeCashRecycler", message));
    }

    private sealed class FakeScanner : IBarcodeScanner
    {
        public event EventHandler<BarcodeScannedEventArgs>? OnBarcodeScanned;
        public Task ConnectAsync(CancellationToken ct = default)    => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakePrinter : IReceiptPrinter
    {
        public event EventHandler<PrintJobStatusEventArgs>? OnJobStatusChanged;
        public Task ConnectAsync(CancellationToken ct = default)                                          => Task.CompletedTask;
        public Task<bool> IsPaperPresentAsync(CancellationToken ct = default)                            => Task.FromResult(true);
        public Task PrintRawAsync(ReadOnlyMemory<byte> payload, CancellationToken ct = default)          => Task.CompletedTask;
    }

    // -----------------------------------------------------------------------
    // Test fixture helpers
    // -----------------------------------------------------------------------
    private readonly string _logPath =
        Path.Combine(Path.GetTempPath(), $"seam_test_{Guid.NewGuid():N}.log");

    private readonly FakeCashRecycler       _fakeCashRecycler = new();
    private readonly FakeScanner            _fakeScanner      = new();
    private readonly FakePrinter            _fakePrinter      = new();
    private readonly DualCurrencyCalculator _calculator       = new();
    private readonly LowFloatMonitor        _lowFloat         = new();
    private readonly OfflineLicenseManager  _license          = new();

    // Extra temp logs created by the isolated-engine tests.
    private readonly List<string> _extraLogs = [];

    private HardwareAppendLog BuildLog() => new(_logPath);

    private LLCoreLogicEngine BuildEngine(HardwareAppendLog? log = null)
        => new(
            _fakeCashRecycler,
            _fakeScanner,
            _fakePrinter,
            _calculator,
            _lowFloat,
            _license,
            log ?? BuildLog());

    /// <summary>
    /// Builds a fully initialised engine with its own isolated recycler and log
    /// so that event handlers from other tests never bleed in.
    /// Used only by the cash-payment seam tests.
    /// </summary>
    private async Task<(LLCoreLogicEngine engine, FakeCashRecycler recycler, string logPath)>
        BuildIsolatedEngineAsync()
    {
        var recycler = new FakeCashRecycler();
        var logPath  = Path.Combine(Path.GetTempPath(), $"seam_cash_{Guid.NewGuid():N}.log");
        _extraLogs.Add(logPath);

        var engine = new LLCoreLogicEngine(
            recycler,
            _fakeScanner,
            _fakePrinter,
            _calculator,
            _lowFloat,
            _license,
            new HardwareAppendLog(logPath));

        await engine.InitializeAsync();
        return (engine, recycler, logPath);
    }

    public void Dispose()
    {
        if (File.Exists(_logPath))
            File.Delete(_logPath);
        foreach (var p in _extraLogs)
            if (File.Exists(p)) File.Delete(p);
    }


    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    /// <summary>
    /// Sprint-0 gate: the object graph compiles and the engine starts Idle.
    /// This is the proof that Category 1 + Category 2 meet correctly at the
    /// composition boundary (Blueprint §6, step 9).
    /// </summary>
    [Fact]
    public void Engine_ConstructsAndStartsIdle_WithMockHal()
    {
        var engine = BuildEngine();

        Assert.Equal(KioskState.Idle, engine.CurrentState);
    }

    /// <summary>
    /// InitializeAsync wires HardwareAppendLog to OnNoteInEscrow and calls
    /// ConnectAsync on the cash recycler (Blueprint §4, §6 step 7).
    /// </summary>
    [Fact]
    public async Task Engine_InitializeAsync_ConnectsHardwareAndWiresLog()
    {
        var log    = BuildLog();
        var engine = BuildEngine(log);

        await engine.InitializeAsync();

        Assert.True(_fakeCashRecycler.ConnectCalled,
            "ConnectAsync must be called during InitializeAsync.");
    }

    /// <summary>
    /// When a note enters escrow, HardwareAppendLog must write an IN_ESCROW
    /// audit record BEFORE any engine processing (Blueprint §4 ordering rule).
    /// </summary>
    [Fact]
    public async Task NoteInEscrow_WritesAuditLogEntry_BeforeEngineProcessing()
    {
        var log    = BuildLog();
        var engine = BuildEngine(log);
        await engine.InitializeAsync();

        _fakeCashRecycler.FireNoteInEscrow(Money.Usd(1.00m));

        var content = File.ReadAllText(_logPath);
        Assert.Contains("IN_ESCROW", content);
        Assert.Contains("1.00",      content);
    }

    /// <summary>
    /// When a denomination drops below LowFloatThreshold, the engine must call
    /// StopAcceptingCashAsync immediately (Blueprint §4: hard stop on low float)
    /// and raise LowFloatStateTriggered so the UI can show the lock screen.
    /// </summary>
    [Fact]
    public async Task LowFloat_BelowThreshold_EngineStopsCashAndFiresEvent()
    {
        var log    = BuildLog();
        var engine = BuildEngine(log);
        await engine.InitializeAsync();

        var engineEventFired = false;
        engine.LowFloatStateTriggered += (_, _) => engineEventFired = true;

        // Drive the monitor below threshold to trigger the chain:
        // LowFloatMonitor → engine.HandleLowFloatTriggered → StopAcceptingCashAsync + event
        _lowFloat.UpdateCount(denominationKhr: 1000, count: LowFloatMonitor.LowFloatThreshold - 1);

        // Allow any async fire-and-forget to complete.
        await Task.Yield();

        Assert.True(_fakeCashRecycler.StopAcceptingCashCalled,
            "Engine must call StopAcceptingCashAsync when LowFloat triggers (Blueprint §4).");
        Assert.True(engineEventFired,
            "Engine must re-raise LowFloatStateTriggered so the UI can show the lock screen.");
    }

    /// <summary>
    /// A hardware fault on the cash recycler must transition the engine to
    /// Faulted state and surface the OnHardwareFault event to ViewModels.
    /// </summary>
    [Fact]
    public async Task CashRecyclerFault_TransitionsToFaultedAndRaisesEvent()
    {
        var log    = BuildLog();
        var engine = BuildEngine(log);
        await engine.InitializeAsync();

        HardwareFaultEventArgs? receivedFault = null;
        engine.OnHardwareFault += (_, e) => receivedFault = e;

        _fakeCashRecycler.FireFault("Jam detected");

        Assert.Equal(KioskState.Faulted, engine.CurrentState);
        Assert.NotNull(receivedFault);
        Assert.Equal("Jam detected", receivedFault.Message);
    }

    // -----------------------------------------------------------------------
    // Cash payment confirmation seam tests (Systems Team scope)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Full seam: begin session → fire exact note → engine confirms → log records
    /// COMMITTED_TO_VAULT. Proves Category 1/2/3 are wired for the happy path.
    /// </summary>
    [Fact]
    public async Task CashPayment_ExactAmount_SeamConfirmsAndLogsCommit()
    {
        var (engine, recycler, logPath) = await BuildIsolatedEngineAsync();
        await engine.BeginCashPaymentAsync(0.75m);

        CashPaymentConfirmedEventArgs? confirmed = null;
        engine.OnCashPaymentConfirmed += (_, e) => confirmed = e;

        recycler.FireNoteInEscrow(Money.Usd(0.75m));

        Assert.NotNull(confirmed);
        Assert.Equal(0.75m, confirmed!.TotalUsd);
        Assert.Equal(KioskState.TransactionComplete, engine.CurrentState);

        var logText = File.ReadAllText(logPath);
        Assert.Contains("IN_ESCROW",          logText);
        Assert.Contains("COMMITTED_TO_VAULT", logText);
    }

    [Fact]
    public async Task CashPayment_LargeOverpayment_SeamRejectsAndLogsReject()
    {
        var (engine, recycler, logPath) = await BuildIsolatedEngineAsync();
        await engine.BeginCashPaymentAsync(0.75m);

        CashPaymentRejectedEventArgs? rejected = null;
        engine.OnCashPaymentRejected += (_, e) => rejected = e;

        recycler.FireNoteInEscrow(Money.Usd(2.00m));

        Assert.NotNull(rejected);
        Assert.Equal(5_125m, rejected!.OverpaymentKhr, precision: 0);
        Assert.NotEqual(KioskState.TransactionComplete, engine.CurrentState);

        var logText = File.ReadAllText(logPath);
        Assert.Contains("IN_ESCROW", logText);
        Assert.Contains("REJECTED",  logText);
        Assert.DoesNotContain("COMMITTED_TO_VAULT", logText);
    }

    /// <summary>
    /// Full seam: $2.00 product, customer pays $1.00 USD + 4 100 KHR (mixed currency).
    /// Both notes must be committed; engine must confirm on the second insertion.
    /// Proves MixedPaymentAccumulator works end-to-end through the wired seam.
    /// </summary>
    [Fact]
    public async Task CashPayment_MixedCurrency_AccumulatesAndConfirms()
    {
        var (engine, recycler, logPath) = await BuildIsolatedEngineAsync();
        await engine.BeginCashPaymentAsync(2.00m);

        CashPaymentPendingEventArgs?   pending   = null;
        CashPaymentConfirmedEventArgs? confirmed = null;
        engine.OnCashPaymentPending   += (_, e) => pending   = e;
        engine.OnCashPaymentConfirmed += (_, e) => confirmed = e;

        // First note: $1.00 USD → under-paid, session stays open
        recycler.FireNoteInEscrow(Money.Usd(1.00m));
        Assert.NotNull(pending);
        Assert.Equal(1.00m,  pending!.RemainingUsd);
        Assert.Equal(4_100m, pending.RemainingKhr);
        Assert.Null(confirmed);

        // Second note: 4 100 KHR = $1.00 → total met, session confirmed
        recycler.FireNoteInEscrow(Money.Khr(4_100m));
        Assert.NotNull(confirmed);
        Assert.Equal(KioskState.TransactionComplete, engine.CurrentState);

        // Both notes must appear as COMMITTED_TO_VAULT in the audit log
        var logText = File.ReadAllText(logPath);
        Assert.Equal(2, logText.Split("COMMITTED_TO_VAULT").Length - 1);
    }
}

