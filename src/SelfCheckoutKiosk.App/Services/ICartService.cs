using System.Collections.ObjectModel;
using System.ComponentModel;
using SelfCheckoutKiosk.App.Models;

namespace SelfCheckoutKiosk.App.Services
{
    public interface ICartService : INotifyPropertyChanged
    {
        ObservableCollection<CartItem> Items { get; }

        decimal TotalUsd { get; }
        decimal TotalKhr { get; }
        int TotalItemCount { get; }
        bool IsEmpty { get; }

        bool IsUsd { get; set; }
        decimal ExchangeRate { get; set; }

        void AddItem(string name, string sku, decimal price, int quantity = 1);
        void DecrementOrRemove(CartItem item);
        void RemoveItem(CartItem item);
        void ClearCart();
    }
}