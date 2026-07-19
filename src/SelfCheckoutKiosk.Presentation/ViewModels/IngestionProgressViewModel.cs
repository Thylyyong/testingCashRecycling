using SelfCheckoutKiosk.Core.Engine;

namespace SelfCheckoutKiosk.Presentation.ViewModels;

/// <summary>IngestionProgressView — tendered / remaining while cash goes in.
/// TODO(Front-End): bind Tendered + Remaining to OnBalanceChanged; surface the
/// low-float lock overlay via LowFloatStateTriggered / LowFloatStateCleared.</summary>
public sealed class IngestionProgressViewModel(ILLCoreLogicEngine engine) : KioskViewModelBase(engine)
{
    private decimal _remainingUsd;
    public decimal RemainingUsd
    {
        get => _remainingUsd;
        private set => SetProperty(ref _remainingUsd, value);
    }
}
