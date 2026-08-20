using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Core.Currency;
using SelfCheckoutKiosk.Core.Engine;
using SelfCheckoutKiosk.Core.Licensing;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Domain.ValueObjects;
using Xunit;

namespace SelfCheckoutKiosk.Integration.Tests;

// TODO(Lead): remove once the fakes raise their events in the full seam test.
#pragma warning disable CS0067 // Event is declared but never raised (test fake)

/// <summary>
/// The one test that proves Category 1/2/3 are wired, not just individually
/// correct (Blueprint §6, step 9). Sprint-0 version asserts the graph
/// constructs with mock HAL and the engine starts Idle. The Back-End then
/// grows this into: scan -> escrow -> dispense -> assert TransactionComplete
/// with the correct change breakdown.
/// </summary>
public sealed class CompositionSeamTests
{
    private sealed class FakeCashRecycler : ICashRecycler
    {
        public event EventHandler<NoteInsertedEventArgs>? OnNoteInserted;
        public event EventHandler<NoteInEscrowEventArgs>? OnNoteInEscrow;
        public event EventHandler<HardwareFaultEventArgs>? OnFault;
        public event EventHandler<CashAcceptorStateChangedEventArgs>? OnAcceptorStateChanged;
        public event EventHandler<CashRecyclerJamEventArgs>? OnJam;
        public event EventHandler<CassetteInventoryChangedEventArgs>? OnCassetteInventoryChanged;

        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task ArmAcceptanceAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisarmAcceptanceAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAcceptingCashAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<DispenseResult> DispenseAsync(ChangeBreakdown change, CancellationToken ct = default)
            => Task.FromResult(new DispenseResult(true, ChangeBreakdown.Empty));
        public Task RejectEscrowedNoteAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeScanner : IBarcodeScanner
    {
        public event EventHandler<BarcodeScannedEventArgs>? OnBarcodeScanned;
        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakePrinter : IReceiptPrinter
    {
        public event EventHandler<PrintJobStatusEventArgs>? OnJobStatusChanged;
        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> IsPaperPresentAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task PrintRawAsync(ReadOnlyMemory<byte> payload, CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact]
    public void Engine_ConstructsAndStartsIdle_WithMockHal()
    {
        string logPath = Path.Combine(Path.GetTempPath(), $"hal-audit-{Guid.NewGuid():N}.log");
        try
        {
            using (var hardwareAppendLog = new HardwareAppendLog(logPath))
            {
                var engine = new LLCoreLogicEngine(
                    new FakeCashRecycler(), new FakeScanner(), new FakePrinter(),
                    new DualCurrencyCalculator(), new LowFloatMonitor(), new OfflineLicenseManager(),
                    hardwareAppendLog);

                Assert.Equal(KioskState.Idle, engine.CurrentState);
            } // HardwareAppendLog's FileStream must be closed before deleting the file below.
        }
        finally
        {
            if (File.Exists(logPath)) File.Delete(logPath);
        }
    }
}
