using SelfCheckoutKiosk.Core.Engine;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Presentation.Services;

namespace SelfCheckoutKiosk.Presentation.ViewModels;

/// <summary>PaymentSelectionView — Cash vs KHQR. Disables Cash and surfaces
/// <see cref="LowFloatNoticeMessage"/> whenever the engine is in exact-cash-only
/// lockout, matching <see cref="LockScreenViewModel"/>/<see cref="IngestionProgressViewModel.IsLowFloatLocked"/>.</summary>
public sealed class PaymentSelectionViewModel : KioskViewModelBase
{
    public PaymentSelectionViewModel(ILLCoreLogicEngine engine, IUiDispatcher dispatcher)
        : base(engine, dispatcher)
    {
        Engine.LowFloatStateTriggered += (_, _) => Dispatcher.Post(() => SetCashAvailable(false));
        Engine.LowFloatStateCleared += (_, _) => Dispatcher.Post(() => SetCashAvailable(true));
    }

    private bool _isCashAvailable = true;
    public bool IsCashAvailable => _isCashAvailable;

    private void SetCashAvailable(bool value)
    {
        if (SetProperty(ref _isCashAvailable, value, nameof(IsCashAvailable)))
            OnPropertyChanged(nameof(LowFloatNoticeMessage));
    }

    public string? LowFloatNoticeMessage =>
        IsCashAvailable ? null : "Exact Cash / Digital Payments Only";

    public Task SelectAsync(PaymentMethod method) => Engine.SelectPaymentMethodAsync(method);
}
