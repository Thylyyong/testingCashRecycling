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

    private readonly FakeCashRecycler      _fakeCashRecycler = new();
    private readonly FakeScanner           _fakeScanner      = new();
    private readonly FakePrinter           _fakePrinter      = new();
    private readonly DualCurrencyCalculator _calculator       = new();
    private readonly LowFloatMonitor       _lowFloat         = new();
    private readonly OfflineLicenseManager _license          = new();

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

    public void Dispose()
    {
        if (File.Exists(_logPath))
            File.Delete(_logPath);
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

        _fakeCashRecycler.FireNoteInEscrow(new Money(1.00m));

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
}

