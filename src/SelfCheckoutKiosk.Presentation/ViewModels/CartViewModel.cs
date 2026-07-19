using SelfCheckoutKiosk.Core.Engine;

namespace SelfCheckoutKiosk.Presentation.ViewModels;

/// <summary>CartView — scanned items + running total. TODO(Front-End):
/// bind ObservableCollection of line items to OnProductAdded; total to
/// OnBalanceChanged; expose a "proceed to payment" command.</summary>
public sealed class CartViewModel(ILLCoreLogicEngine engine) : KioskViewModelBase(engine)
{
    private decimal _runningTotalUsd;
    public decimal RunningTotalUsd
    {
        get => _runningTotalUsd;
        private set => SetProperty(ref _runningTotalUsd, value);
    }

    public Task SubmitScanAsync(string rawScan) => Engine.SubmitScanAsync(rawScan);
}
