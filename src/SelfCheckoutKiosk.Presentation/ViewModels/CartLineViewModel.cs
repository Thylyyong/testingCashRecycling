namespace SelfCheckoutKiosk.Presentation.ViewModels;

/// <summary>
/// One row in the on-screen cart list. The engine's ledger
/// (<c>Transaction.LineItems</c>) records one <c>LineItem</c> per scan
/// (Blueprint §3) and never merges repeats — this type is a purely
/// presentation-side merge of same-EAN scans into a single "x3" row, the way
/// a shopper expects to see a receipt. It does not feed back into the
/// ledger; <see cref="CartViewModel.RunningTotalUsd"/> always comes straight
/// from the engine's own <c>OnBalanceChanged</c> total, never a client-side
/// re-sum of these rows.
/// </summary>
public sealed class CartLineViewModel(string ean13, string description, decimal unitPriceUsd)
    : ObservableObject
{
    public string Ean13 { get; } = ean13;
    public string Description { get; } = description;
    public decimal UnitPriceUsd { get; } = unitPriceUsd;

    private int _quantity = 1;
    public int Quantity
    {
        get => _quantity;
        set
        {
            if (SetProperty(ref _quantity, value))
                OnPropertyChanged(nameof(LineTotalUsd));
        }
    }

    public decimal LineTotalUsd => UnitPriceUsd * Quantity;
}
