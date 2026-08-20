using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Domain.ValueObjects;

namespace SelfCheckoutKiosk.Core.Engine;

/// <summary>
/// Public surface of the deterministic core engine (Blueprint §3 & §6).
/// ViewModels bind to THIS — never to hardware types directly. The engine is
/// the single subscriber to all HAL event streams.
///
/// Front-End devs mock this interface for MVVM work on Day 1; the Back-End
/// dev fills in <see cref="LLCoreLogicEngine"/> behind it — the two proceed
/// in parallel without blocking each other.
/// </summary>
public interface ILLCoreLogicEngine
{
    KioskState CurrentState { get; }

    /// <summary>Sprint-0 offline USD→KHR display/change-calculation rate.
    /// Exposed read-only so screens can render a KHR-equivalent price
    /// alongside the ledger-currency (USD) total without duplicating the
    /// rate as a second hard-coded constant in the UI layer.</summary>
    decimal UsdToKhrRate { get; }

    /// <summary>The change breakdown dispensed for the most recently completed
    /// cash tender, or <c>null</c> before any transaction has completed / after
    /// <see cref="ResetToIdleAsync"/>. Screens read this to render the
    /// dispensed-change confirmation.</summary>
    ChangeBreakdown? LastChangeBreakdown { get; }

    event EventHandler<KioskStateChangedEventArgs>? OnStateChanged;
    event EventHandler<ProductAddedEventArgs>? OnProductAdded;
    event EventHandler<BalanceChangedEventArgs>? OnBalanceChanged;
    event EventHandler<NoteProcessedEventArgs>? OnNoteProcessed;
    event EventHandler? LowFloatStateTriggered;
    event EventHandler? LowFloatStateCleared;
    event EventHandler<HardwareFaultEventArgs>? OnHardwareFault;

    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<ScanResult> SubmitScanAsync(string rawScan, CancellationToken cancellationToken = default);
    Task SelectPaymentMethodAsync(PaymentMethod method, CancellationToken cancellationToken = default);
    Task ResetToIdleAsync(CancellationToken cancellationToken = default);

    /// <summary>Point-in-time snapshot for the admin diagnostics panel. Never
    /// blocks on subsystems that aren't wired up yet (e.g. Licensing) —
    /// degrades those fields instead of throwing.</summary>
    Task<AdminDiagnosticsSnapshot> GetDiagnosticsSnapshotAsync(CancellationToken cancellationToken = default);
}
