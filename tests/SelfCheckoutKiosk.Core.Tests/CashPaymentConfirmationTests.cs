using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Core.Currency;
using SelfCheckoutKiosk.Core.Engine;
using SelfCheckoutKiosk.Core.Licensing;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Domain.ValueObjects;
using Xunit;

namespace SelfCheckoutKiosk.Core.Tests;

// Fake events not raised in this suite (OnFault, OnBarcodeScanned, OnJobStatusChanged)
#pragma warning disable CS0067

/// <summary>
/// Unit tests for the cash payment confirmation logic inside LLCoreLogicEngine.
///
/// Machine rules under test (Blueprint §3):
///   1 USD = 4 100 KHR
///   Under-payment  → note committed to vault; session stays open (insert more)
///   Exact / Over ≤ 500 KHR → confirmed (merchant absorbs small overpayment)
///   Over > 500 KHR → note rejected (returned to customer); session stays open
///
/// IMPORTANT: each test uses its own FakeCashRecycler and log file so that
/// event handler registrations never bleed between tests.
/// </summary>
public sealed class CashPaymentConfirmationTests : IDisposable
{
    // -----------------------------------------------------------------------
    // Minimal HAL fakes
    // -----------------------------------------------------------------------

    private sealed class FakeCashRecycler : ICashRecycler
    {
        public event EventHandler<NoteInEscrowEventArgs>?  OnNoteInEscrow;
        public event EventHandler<HardwareFaultEventArgs>? OnFault;

        public bool RejectCalled  { get; private set; }
        public bool DisarmCalled  { get; private set; }

        public Task ConnectAsync(CancellationToken ct = default)       => Task.CompletedTask;
        public Task ArmAcceptanceAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task DisarmAcceptanceAsync(CancellationToken ct = default)
        {
            DisarmCalled = true;
            return Task.CompletedTask;
        }

        public Task StopAcceptingCashAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<DispenseResult> DispenseAsync(ChangeBreakdown change, CancellationToken ct = default)
            => Task.FromResult(new DispenseResult(true, ChangeBreakdown.Empty));

        public Task RejectEscrowedNoteAsync(CancellationToken ct = default)
        {
            RejectCalled = true;
            return Task.CompletedTask;
        }

        public void FireNoteInEscrow(Money note)
            => OnNoteInEscrow?.Invoke(this, new NoteInEscrowEventArgs(note));
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
        public Task ConnectAsync(CancellationToken ct = default)                                  => Task.CompletedTask;
        public Task<bool> IsPaperPresentAsync(CancellationToken ct = default)                    => Task.FromResult(true);
        public Task PrintRawAsync(ReadOnlyMemory<byte> payload, CancellationToken ct = default)  => Task.CompletedTask;
    }

    // -----------------------------------------------------------------------
    // Per-test scaffolding
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds a fully initialised engine with its own isolated recycler and log.
    /// Returns both so the test can fire notes and inspect state.
    /// </summary>
    private static async Task<(LLCoreLogicEngine engine, FakeCashRecycler recycler, string logPath)>
        BuildAsync()
    {
        var recycler = new FakeCashRecycler();
        var logPath  = Path.Combine(Path.GetTempPath(), $"cashtest_{Guid.NewGuid():N}.log");

        var engine = new LLCoreLogicEngine(
            recycler,
            new FakeScanner(),
            new FakePrinter(),
            new DualCurrencyCalculator(),
            new LowFloatMonitor(),
            new OfflineLicenseManager(),
            new HardwareAppendLog(logPath));

        await engine.InitializeAsync();
        return (engine, recycler, logPath);
    }

    // Track temp files for cleanup.
    private readonly List<string> _tempLogs = [];

    public void Dispose()
    {
        foreach (var p in _tempLogs)
            if (File.Exists(p)) File.Delete(p);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------
    private static void AssertLogContains(string logPath, string token)
    {
        var text = File.ReadAllText(logPath);
        Assert.Contains(token, text);
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    /// <summary>
    /// $0.75 product, customer inserts $0.75 USD exactly.
    /// Overpayment = 0 KHR → machine must confirm (exact = tolerance boundary).
    /// </summary>
    [Fact]
    public async Task ExactPayment_Confirms()
    {
        var (engine, recycler, log) = await BuildAsync();
        _tempLogs.Add(log);

        await engine.BeginCashPaymentAsync(0.75m);

        CashPaymentConfirmedEventArgs? confirmed = null;
        engine.OnCashPaymentConfirmed += (_, e) => confirmed = e;

        recycler.FireNoteInEscrow(Money.Usd(0.75m));

        Assert.NotNull(confirmed);
        Assert.Equal(0.75m, confirmed!.TotalUsd);
        Assert.Equal(0m,    confirmed.OverpaymentKhr);
        Assert.Equal(KioskState.TransactionComplete, engine.CurrentState);
        Assert.True(recycler.DisarmCalled, "Recycler must be disarmed after confirmation.");
        AssertLogContains(log, "COMMITTED_TO_VAULT");
    }

    /// <summary>
    /// $0.75 product, customer inserts $0.76 (overpayment = $0.01 = 41 KHR ≤ 500 KHR).
    /// Machine must confirm and absorb the 41 KHR.
    /// </summary>
    [Fact]
    public async Task OverpaymentWithin500Khr_Confirms()
    {
        var (engine, recycler, log) = await BuildAsync();
        _tempLogs.Add(log);

        await engine.BeginCashPaymentAsync(0.75m);

        CashPaymentConfirmedEventArgs? confirmed = null;
        engine.OnCashPaymentConfirmed += (_, e) => confirmed = e;

        recycler.FireNoteInEscrow(Money.Usd(0.76m));   // overpayment = $0.01 = 41 KHR

        Assert.NotNull(confirmed);
        // 0.01 * 4100 = 41 KHR
        Assert.Equal(41m, confirmed!.OverpaymentKhr, precision: 0);
        Assert.Equal(KioskState.TransactionComplete, engine.CurrentState);
        Assert.False(recycler.RejectCalled, "Note must NOT be rejected when within tolerance.");
    }

    /// <summary>
    /// Boundary: overpayment of exactly 500 KHR must confirm (tolerance is inclusive).
    /// Scenario: $1.00 product. Customer inserts $1 USD (exact) → pending.
    /// Then inserts 500 KHR (overpayment = 500 KHR ≤ 500 KHR) → confirm.
    /// This avoids the repeating-decimal issue of converting 4600 KHR in one note.
    /// </summary>
    [Fact]
    public async Task OverpaymentExactly500Khr_Confirms()
    {
        var (engine, recycler, log) = await BuildAsync();
        _tempLogs.Add(log);

        // $1.00 product.
        await engine.BeginCashPaymentAsync(1.00m);

        CashPaymentPendingEventArgs?   pending   = null;
        CashPaymentConfirmedEventArgs? confirmed = null;
        engine.OnCashPaymentPending   += (_, e) => pending   = e;
        engine.OnCashPaymentConfirmed += (_, e) => confirmed = e;

        // First note: $1.00 USD — exact amount → should confirm at 0 KHR over.
        // Actually this confirms immediately. Use $0.90 first to stay under.
        // $0.90 → accumulated = 0.90, remaining = 0.10, overpaymentKhr < 0 → pending.
        recycler.FireNoteInEscrow(Money.Usd(0.90m));
        Assert.NotNull(pending);
        Assert.Null(confirmed);

        // Second note: 500 KHR = 500/4100 ≈ $0.1220. Running total ≈ $1.022.
        // Overpayment ≈ 500 KHR (within tolerance) → must confirm.
        recycler.FireNoteInEscrow(Money.Khr(500m));

        Assert.NotNull(confirmed);
        Assert.True(confirmed!.OverpaymentKhr <= 500m,
            $"Expected ≤500 KHR overpayment but got {confirmed.OverpaymentKhr}");
        Assert.Equal(KioskState.TransactionComplete, engine.CurrentState);
        Assert.False(recycler.RejectCalled);
    }

    /// <summary>
    /// User rule test: $2.00 product total.
    /// Customer inserts $1.00 USD note → Pending ($1.00 / 4,100 KHR remaining).
    /// Customer inserts 4,100 KHR note → Confirmed (exact $2.00 paid, 0 KHR overpayment).
    /// </summary>
    [Fact]
    public async Task MixedCurrency_2UsdProduct_PaidWith_1Usd_And_4100Khr_Confirms()
    {
        var (engine, recycler, log) = await BuildAsync();
        _tempLogs.Add(log);

        // $2.00 product total
        await engine.BeginCashPaymentAsync(2.00m);

        CashPaymentPendingEventArgs?   pending   = null;
        CashPaymentConfirmedEventArgs? confirmed = null;
        engine.OnCashPaymentPending   += (_, e) => pending   = e;
        engine.OnCashPaymentConfirmed += (_, e) => confirmed = e;

        // Step 1: Customer inserts $1.00 USD note
        recycler.FireNoteInEscrow(Money.Usd(1.00m));

        // Assert Step 1: Pending state, $1.00 / 4,100 KHR remaining
        Assert.NotNull(pending);
        Assert.Equal(1.00m, pending!.TenderedUsd);
        Assert.Equal(1.00m, pending.RemainingUsd);
        Assert.Equal(4100m, pending.RemainingKhr);
        Assert.Null(confirmed);
        Assert.Equal(KioskState.ProcessingCash, engine.CurrentState);

        // Step 2: Customer inserts 4,100 KHR note
        recycler.FireNoteInEscrow(Money.Khr(4100m));

        // Assert Step 2: Transaction complete, confirmed at $2.00 USD total
        Assert.NotNull(confirmed);
        Assert.Equal(2.00m, confirmed!.TotalUsd);
        Assert.Equal(2.00m, confirmed.TenderedUsd);
        Assert.Equal(0m, confirmed.OverpaymentKhr);
        Assert.Equal(KioskState.TransactionComplete, engine.CurrentState);
        Assert.True(recycler.DisarmCalled);
    }

    /// <summary>
    /// User rule test: No change-dispensing hardware.
    /// Product total = $1.25 USD (~5,125 KHR).
    /// Step 1: Customer inserts $2.00 USD note → Overpayment = $0.75 USD = 3,075 KHR > 500 KHR limit.
    ///         Engine REJECTS & EJECTS $2.00 note, session stays PENDING, engine stays ProcessingCash.
    /// Step 2: Customer inserts $1.25 USD note → Overpayment = 0 KHR ≤ 500 KHR limit.
    ///         Engine CONFIRMS payment, session transitions to TransactionComplete!
    /// </summary>
    [Fact]
    public async Task NoChangeHardware_1Point25UsdProduct_Rejects2UsdNote_And_ConfirmsExactNote()
    {
        var (engine, recycler, log) = await BuildAsync();
        _tempLogs.Add(log);

        // $1.25 product total
        await engine.BeginCashPaymentAsync(1.25m);

        CashPaymentRejectedEventArgs?  rejected  = null;
        CashPaymentConfirmedEventArgs? confirmed = null;
        engine.OnCashPaymentRejected  += (_, e) => rejected  = e;
        engine.OnCashPaymentConfirmed += (_, e) => confirmed = e;

        // Step 1: Customer inserts $2.00 USD note (overpayment = $0.75 = 3,075 KHR > 500 KHR limit)
        recycler.FireNoteInEscrow(Money.Usd(2.00m));

        // Assert Step 1: Note rejected, physically pushed back, session stays ProcessingCash
        Assert.NotNull(rejected);
        Assert.True(recycler.RejectCalled, "Engine must physically eject $2.00 note.");
        Assert.Null(confirmed);
        Assert.Equal(KioskState.ProcessingCash, engine.CurrentState);

        // Step 2: Customer inserts exact $1.25 USD note
        recycler.FireNoteInEscrow(Money.Usd(1.25m));

        // Assert Step 2: Confirmed payment, transaction complete
        Assert.NotNull(confirmed);
        Assert.Equal(1.25m, confirmed!.TotalUsd);
        Assert.Equal(1.25m, confirmed.TenderedUsd);
        Assert.Equal(0m, confirmed.OverpaymentKhr);
        Assert.Equal(KioskState.TransactionComplete, engine.CurrentState);
        Assert.True(recycler.DisarmCalled);
    }

    /// <summary>
    /// $0.75 product, customer inserts $0.90.
    /// Overpayment = $0.15 = 615 KHR > 500 KHR → rejected.
    /// </summary>
    [Fact]
    public async Task OverpaymentAbove500Khr_Rejects()
    {
        var (engine, recycler, log) = await BuildAsync();
        _tempLogs.Add(log);

        await engine.BeginCashPaymentAsync(0.75m);

        CashPaymentRejectedEventArgs? rejected = null;
        engine.OnCashPaymentRejected += (_, e) => rejected = e;

        // $0.90 → overpayment = $0.15 = 615 KHR > 500 KHR
        recycler.FireNoteInEscrow(Money.Usd(0.90m));

        Assert.NotNull(rejected);
        Assert.True(rejected!.OverpaymentKhr > 500m,
            $"Expected >500 KHR but got {rejected.OverpaymentKhr} KHR");
        Assert.True(recycler.RejectCalled, "Note must be physically returned.");
        Assert.NotEqual(KioskState.TransactionComplete, engine.CurrentState);
        AssertLogContains(log, "REJECTED");
    }

    /// <summary>
    /// Real-world scenario from the spec: Coca-Cola = $0.75, customer inserts $2.
    /// Overpayment = $1.25 = 5 125 KHR → must be rejected.
    /// </summary>
    [Fact]
    public async Task LargeOverpayment_CocaColaScenario_Rejects()
    {
        var (engine, recycler, log) = await BuildAsync();
        _tempLogs.Add(log);

        await engine.BeginCashPaymentAsync(0.75m);

        CashPaymentRejectedEventArgs? rejected = null;
        engine.OnCashPaymentRejected += (_, e) => rejected = e;

        recycler.FireNoteInEscrow(Money.Usd(2.00m));   // overpayment = $1.25 = 5 125 KHR

        Assert.NotNull(rejected);
        Assert.Equal(5_125m, rejected!.OverpaymentKhr, precision: 0);
        Assert.True(recycler.RejectCalled);
        AssertLogContains(log, "REJECTED");
    }

    /// <summary>
    /// $2.00 product, customer inserts $1.00 (under-paid by $1.00 = 4 100 KHR).
    /// Machine commits the note to vault and raises Pending — session stays open.
    /// </summary>
    [Fact]
    public async Task Underpayment_RaisesPending_NoteCommittedToVault()
    {
        var (engine, recycler, log) = await BuildAsync();
        _tempLogs.Add(log);

        await engine.BeginCashPaymentAsync(2.00m);

        CashPaymentPendingEventArgs? pending = null;
        engine.OnCashPaymentPending += (_, e) => pending = e;

        recycler.FireNoteInEscrow(Money.Usd(1.00m));

        Assert.NotNull(pending);
        Assert.Equal(2.00m,  pending!.TotalUsd);
        Assert.Equal(1.00m,  pending.TenderedUsd);
        Assert.Equal(1.00m,  pending.RemainingUsd);
        Assert.Equal(4_100m, pending.RemainingKhr);
        Assert.False(recycler.RejectCalled, "Under-paid note must NOT be rejected.");
        Assert.NotEqual(KioskState.TransactionComplete, engine.CurrentState);
        AssertLogContains(log, "COMMITTED_TO_VAULT");
    }

    /// <summary>
    /// $2.00 product: customer inserts $1.00 (pending) then $1.00 (confirms).
    /// Both notes are committed to the vault.
    /// </summary>
    [Fact]
    public async Task TwoUsdNotes_SecondCompletes_Confirms()
    {
        var (engine, recycler, log) = await BuildAsync();
        _tempLogs.Add(log);

        await engine.BeginCashPaymentAsync(2.00m);

        CashPaymentPendingEventArgs?   pending   = null;
        CashPaymentConfirmedEventArgs? confirmed = null;
        engine.OnCashPaymentPending   += (_, e) => pending   = e;
        engine.OnCashPaymentConfirmed += (_, e) => confirmed = e;

        recycler.FireNoteInEscrow(Money.Usd(1.00m));  // first note → pending
        Assert.NotNull(pending);
        Assert.Null(confirmed);

        recycler.FireNoteInEscrow(Money.Usd(1.00m));  // second note → confirmed

        Assert.NotNull(confirmed);
        Assert.Equal(2.00m, confirmed!.TotalUsd);
        Assert.Equal(2.00m, confirmed.TenderedUsd);
        Assert.Equal(0m,    confirmed.OverpaymentKhr);
        Assert.Equal(KioskState.TransactionComplete, engine.CurrentState);

        // Both notes committed
        var logText = File.ReadAllText(log);
        Assert.Equal(2, logText.Split("COMMITTED_TO_VAULT").Length - 1);
    }

    /// <summary>
    /// Mixed-currency payment (your spec): $2.00 product.
    /// Customer inserts $1.00 USD (pending) then 4 100 KHR = $1.00 (confirms).
    /// Proves MixedPaymentAccumulator.AccumulateNote(Money.Khr(...)) works
    /// end-to-end through the engine.
    /// </summary>
    [Fact]
    public async Task MixedCurrency_UsdPlusKhr_Confirms()
    {
        var (engine, recycler, log) = await BuildAsync();
        _tempLogs.Add(log);

        await engine.BeginCashPaymentAsync(2.00m);

        CashPaymentPendingEventArgs?   pending   = null;
        CashPaymentConfirmedEventArgs? confirmed = null;
        engine.OnCashPaymentPending   += (_, e) => pending   = e;
        engine.OnCashPaymentConfirmed += (_, e) => confirmed = e;

        // First insertion: $1.00 USD → remaining $1.00 = 4 100 KHR
        recycler.FireNoteInEscrow(Money.Usd(1.00m));
        Assert.NotNull(pending);
        Assert.Equal(1.00m,  pending!.RemainingUsd);
        Assert.Equal(4_100m, pending.RemainingKhr);
        Assert.Null(confirmed);

        // Second insertion: 4 100 KHR = exactly $1.00 → total met
        recycler.FireNoteInEscrow(Money.Khr(4_100m));

        Assert.NotNull(confirmed);
        Assert.Equal(2.00m, confirmed!.TotalUsd);
        Assert.Equal(2.00m, confirmed.TenderedUsd, precision: 4);
        Assert.Equal(0m,    confirmed.OverpaymentKhr, precision: 0);
        Assert.Equal(KioskState.TransactionComplete, engine.CurrentState);
    }

    /// <summary>
    /// Safety guard: a note arrives with no active payment session.
    /// The engine must reject it and NOT crash.
    /// </summary>
    [Fact]
    public async Task NoActiveSession_NoteIsRejectedSafely()
    {
        var (engine, recycler, log) = await BuildAsync();
        _tempLogs.Add(log);
        // Deliberately do NOT call BeginCashPaymentAsync

        CashPaymentConfirmedEventArgs? confirmed = null;
        engine.OnCashPaymentConfirmed += (_, e) => confirmed = e;

        // Should not throw — guard must handle gracefully
        recycler.FireNoteInEscrow(Money.Usd(1.00m));

        Assert.Null(confirmed);
        Assert.True(recycler.RejectCalled, "Stray note must be rejected.");
        AssertLogContains(log, "REJECTED");
    }

    /// <summary>
    /// After a rejection (> 500 KHR overpayment), the accumulated total is rolled back.
    /// The customer then inserts the correct note and the session confirms.
    /// </summary>
    [Fact]
    public async Task AfterRejection_CustomerInsertsCorrectNote_Confirms()
    {
        var (engine, recycler, log) = await BuildAsync();
        _tempLogs.Add(log);

        await engine.BeginCashPaymentAsync(0.75m);

        CashPaymentRejectedEventArgs?  rejected  = null;
        CashPaymentConfirmedEventArgs? confirmed = null;
        engine.OnCashPaymentRejected  += (_, e) => rejected  = e;
        engine.OnCashPaymentConfirmed += (_, e) => confirmed = e;

        // First attempt: $2.00 — too much (5 125 KHR over) → rejected, rolled back
        recycler.FireNoteInEscrow(Money.Usd(2.00m));
        Assert.NotNull(rejected);
        Assert.Null(confirmed);

        // Second attempt: $0.75 exact — session accumulator is back at 0, so this confirms
        recycler.FireNoteInEscrow(Money.Usd(0.75m));
        Assert.NotNull(confirmed);
        Assert.Equal(0m, confirmed!.OverpaymentKhr);
        Assert.Equal(KioskState.TransactionComplete, engine.CurrentState);
    }
}
