using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Core.Engine;
using SelfCheckoutKiosk.Domain.Entities;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Domain.ValueObjects;

namespace SelfCheckoutKiosk.Presentation.Tests;

/// <summary>Manually-implemented fake — no mocking framework, matching the
/// Core.Tests convention. Tests drive it by raising the engine events
/// directly and reading back what ViewModels called.</summary>
public sealed class FakeLLCoreLogicEngine : ILLCoreLogicEngine
{
    public KioskState CurrentState { get; set; } = KioskState.Idle;
    public decimal UsdToKhrRate { get; set; } = 4100m;
    public ChangeBreakdown? LastChangeBreakdown { get; set; }

    public event EventHandler<KioskStateChangedEventArgs>? OnStateChanged;
    public event EventHandler<ProductAddedEventArgs>? OnProductAdded;
    public event EventHandler<BalanceChangedEventArgs>? OnBalanceChanged;
    public event EventHandler<NoteProcessedEventArgs>? OnNoteProcessed;
    public event EventHandler? LowFloatStateTriggered;
    public event EventHandler? LowFloatStateCleared;
    public event EventHandler<HardwareFaultEventArgs>? OnHardwareFault;

    public int ResetToIdleCallCount { get; private set; }
    public List<PaymentMethod> SelectedPaymentMethods { get; } = [];

    public void RaiseStateChanged(KioskState previous, KioskState current)
    {
        CurrentState = current;
        OnStateChanged?.Invoke(this, new KioskStateChangedEventArgs(previous, current));
    }

    public void RaiseProductAdded(Product product, decimal runningTotalUsd) =>
        OnProductAdded?.Invoke(this, new ProductAddedEventArgs(product, runningTotalUsd));

    public void RaiseBalanceChanged(decimal totalUsd, decimal tenderedUsd, decimal remainingUsd) =>
        OnBalanceChanged?.Invoke(this, new BalanceChangedEventArgs(totalUsd, tenderedUsd, remainingUsd));

    public void RaiseNoteProcessed(Money note, bool accepted, decimal tenderedUsd, decimal remainingUsd) =>
        OnNoteProcessed?.Invoke(this, new NoteProcessedEventArgs(note, accepted, tenderedUsd, remainingUsd));

    public void RaiseLowFloatTriggered() => LowFloatStateTriggered?.Invoke(this, EventArgs.Empty);

    public void RaiseLowFloatCleared() => LowFloatStateCleared?.Invoke(this, EventArgs.Empty);

    public void RaiseHardwareFault(string device, string message) =>
        OnHardwareFault?.Invoke(this, new HardwareFaultEventArgs(device, message));

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<ScanResult> SubmitScanAsync(string rawScan, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ScanResult(ScanCategory.Ean13Product, true));

    public Task SelectPaymentMethodAsync(PaymentMethod method, CancellationToken cancellationToken = default)
    {
        SelectedPaymentMethods.Add(method);
        return Task.CompletedTask;
    }

    public Task ResetToIdleAsync(CancellationToken cancellationToken = default)
    {
        ResetToIdleCallCount++;
        return Task.CompletedTask;
    }

    public Task<AdminDiagnosticsSnapshot> GetDiagnosticsSnapshotAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new AdminDiagnosticsSnapshot(
            CashRecyclerConnected: true,
            BarcodeScannerConnected: true,
            ReceiptPrinterConnected: true,
            KhrCassetteCounts: new Dictionary<int, int>(),
            UnresolvedHardwareAppendLogEntries: 0,
            LicenseTier: "Unknown",
            LicenseExpiresAtUtc: null,
            LastSyncTimestampUtc: null));
}
