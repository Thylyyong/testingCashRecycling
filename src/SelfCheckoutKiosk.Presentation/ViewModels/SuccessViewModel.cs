using SelfCheckoutKiosk.Core.Engine;

namespace SelfCheckoutKiosk.Presentation.ViewModels;

/// <summary>SuccessView — change breakdown + receipt, then auto-reset.
/// TODO(Front-End): show dispensed ChangeBreakdown; call ResetToIdleAsync on
/// timeout or "done".</summary>
public sealed class SuccessViewModel(ILLCoreLogicEngine engine) : KioskViewModelBase(engine)
{
    public Task StartNewTransactionAsync() => Engine.ResetToIdleAsync();
}
