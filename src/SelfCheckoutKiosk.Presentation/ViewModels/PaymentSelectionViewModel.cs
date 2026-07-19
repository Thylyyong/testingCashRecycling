using SelfCheckoutKiosk.Core.Engine;
using SelfCheckoutKiosk.Domain.Enums;

namespace SelfCheckoutKiosk.Presentation.ViewModels;

/// <summary>PaymentSelectionView — Cash vs KHQR. TODO(Front-End): disable Cash
/// and show the exact-cash notice when the engine raises LowFloatStateTriggered.</summary>
public sealed class PaymentSelectionViewModel(ILLCoreLogicEngine engine) : KioskViewModelBase(engine)
{
    public Task SelectAsync(PaymentMethod method) => Engine.SelectPaymentMethodAsync(method);
}
