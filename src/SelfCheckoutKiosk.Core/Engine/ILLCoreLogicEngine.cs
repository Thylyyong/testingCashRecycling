using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Domain.Enums;

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

    event EventHandler<KioskStateChangedEventArgs>? OnStateChanged;
    event EventHandler<ProductAddedEventArgs>? OnProductAdded;
    event EventHandler<BalanceChangedEventArgs>? OnBalanceChanged;
    event EventHandler? LowFloatStateTriggered;
    event EventHandler? LowFloatStateCleared;
    event EventHandler<HardwareFaultEventArgs>? OnHardwareFault;

    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<ScanResult> SubmitScanAsync(string rawScan, CancellationToken cancellationToken = default);
    Task SelectPaymentMethodAsync(PaymentMethod method, CancellationToken cancellationToken = default);
    Task ResetToIdleAsync(CancellationToken cancellationToken = default);
}
