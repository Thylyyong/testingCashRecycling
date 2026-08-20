using System.Collections.ObjectModel;
using SelfCheckoutKiosk.Core.Currency;
using SelfCheckoutKiosk.Core.Engine;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Presentation.Services;

namespace SelfCheckoutKiosk.Presentation.ViewModels;

/// <summary>CartView — scanned items + running total. The kiosk's resting
/// screen: shown for both <see cref="KioskState.Idle"/> (empty cart, "scan
/// to begin") and <see cref="KioskState.Scanning"/> (one or more items).</summary>
public sealed class CartViewModel : KioskViewModelBase
{
    public CartViewModel(ILLCoreLogicEngine engine, IUiDispatcher dispatcher)
        : base(engine, dispatcher)
    {
        Engine.OnProductAdded += HandleProductAdded;
        Engine.OnBalanceChanged += HandleBalanceChanged;
    }

    /// <summary>Presentation-side merge of same-EAN scans — see
    /// <see cref="CartLineViewModel"/>'s doc comment for why this never
    /// feeds back into the engine's ledger.</summary>
    public ObservableCollection<CartLineViewModel> Items { get; } = [];

    private decimal _runningTotalUsd;
    public decimal RunningTotalUsd
    {
        get => _runningTotalUsd;
        private set => SetProperty(ref _runningTotalUsd, value);
    }

    /// <summary>Display-only KHR equivalent of <see cref="RunningTotalUsd"/>
    /// at the engine's cached offline rate — rounded up to the nearest 100 KHR per Cambodian retail rule.</summary>
    public decimal RunningTotalKhr => DualCurrencyCalculator.CalculateTotalKhr(RunningTotalUsd, Engine.UsdToKhrRate);

    public bool HasItems => Items.Count > 0;

    public Task SubmitScanAsync(string rawScan) => Engine.SubmitScanAsync(rawScan);

    /// <summary>Cash vs KHQR is decided by whichever payment-method command
    /// the "PAY NOW" affordance resolves to (e.g. a chooser dialog in the
    /// view) — this just forwards the choice into the engine.</summary>
    public Task SelectPaymentMethodAsync(PaymentMethod method) => Engine.SelectPaymentMethodAsync(method);

    public Task CancelAsync() => Engine.ResetToIdleAsync();

    /// <summary>Explicit user-intent hook for the "PAY NOW" affordance.
    /// The actual Cart→Payment transition is driven entirely by the engine's
    /// own state machine (via <c>OnStateChanged</c>) once a payment method is
    /// selected — this exists so a view can bind a command to the user's tap
    /// without asserting a state change itself.</summary>
    public void ProceedToPayment()
    {
    }

    private void HandleProductAdded(object? sender, ProductAddedEventArgs e)
    {
        Dispatcher.Post(() =>
        {
            CartLineViewModel? existing = Items.FirstOrDefault(i => i.Ean13 == e.Product.Ean13);
            if (existing is not null)
            {
                existing.Quantity++;
            }
            else
            {
                Items.Add(new CartLineViewModel(e.Product.Ean13, e.Product.Description, e.Product.UsdPrice));
            }

            OnPropertyChanged(nameof(HasItems));
        });
    }

    private void HandleBalanceChanged(object? sender, BalanceChangedEventArgs e)
    {
        Dispatcher.Post(() =>
        {
            RunningTotalUsd = e.TotalUsd;
            OnPropertyChanged(nameof(RunningTotalKhr));
        });
    }
}
