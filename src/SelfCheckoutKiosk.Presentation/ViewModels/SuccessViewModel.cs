using SelfCheckoutKiosk.Core.Engine;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Domain.ValueObjects;
using SelfCheckoutKiosk.Presentation.Services;

namespace SelfCheckoutKiosk.Presentation.ViewModels;

/// <summary>SuccessView — change breakdown + receipt, then auto-reset to
/// <see cref="KioskState.Idle"/> after <see cref="ResetDelay"/> unless the
/// user taps "done" first via <see cref="StartNewTransactionAsync"/>.</summary>
public sealed class SuccessViewModel : KioskViewModelBase, IDisposable
{
    private static readonly TimeSpan DefaultResetDelay = TimeSpan.FromSeconds(10);

    private readonly TimeSpan _resetDelay;
    private Timer? _resetTimer;

    public SuccessViewModel(ILLCoreLogicEngine engine, IUiDispatcher dispatcher, TimeSpan? resetDelay = null)
        : base(engine, dispatcher)
    {
        _resetDelay = resetDelay ?? DefaultResetDelay;

        Engine.OnStateChanged += (_, e) => Dispatcher.Post(() =>
        {
            if (e.Current == KioskState.TransactionComplete)
                OnArrivedAtSuccess();
            else
                CancelResetTimer();
        });

        if (Engine.CurrentState == KioskState.TransactionComplete)
            OnArrivedAtSuccess();
    }

    private ChangeBreakdown? _changeBreakdown;
    public ChangeBreakdown? ChangeBreakdown
    {
        get => _changeBreakdown;
        private set => SetProperty(ref _changeBreakdown, value);
    }

    private void OnArrivedAtSuccess()
    {
        ChangeBreakdown = Engine.LastChangeBreakdown;
        ArmResetTimer();
    }

    private void ArmResetTimer()
    {
        CancelResetTimer();
        _resetTimer = new Timer(
            _ => Dispatcher.Post(() => _ = StartNewTransactionAsync()),
            state: null,
            dueTime: _resetDelay,
            period: Timeout.InfiniteTimeSpan);
    }

    private void CancelResetTimer()
    {
        _resetTimer?.Dispose();
        _resetTimer = null;
    }

    public Task StartNewTransactionAsync()
    {
        CancelResetTimer();
        return Engine.ResetToIdleAsync();
    }

    public void Dispose() => CancelResetTimer();
}
