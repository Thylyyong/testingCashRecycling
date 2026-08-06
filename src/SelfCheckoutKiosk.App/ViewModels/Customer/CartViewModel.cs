using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SelfCheckoutKiosk.App.ViewModels.Customer
{
    public class CartItem : INotifyPropertyChanged
    {
        public string Name { get; set; } = "";
        public string Sku { get; set; } = "";
        public decimal PricePerItem { get; set; }

        private int _quantity;
        public int Quantity
        {
            get => _quantity;
            set { _quantity = value; OnPropertyChanged(nameof(Quantity)); OnPropertyChanged(nameof(LineTotal)); }
        }

        public decimal LineTotal => PricePerItem * Quantity;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    internal class CartViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<CartItem> Items { get; } = new();

        public bool IsEmpty => Items.Count == 0;
        public int ItemCount => Items.Sum(i => i.Quantity);
        public decimal Total => Items.Sum(i => i.LineTotal);

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public CartViewModel()
        {
            Items.CollectionChanged += (s, e) => RaiseAll();
        }

        // --- Mock data helpers for testing ---

        public void LoadMockData()
        {
            Items.Clear();

            Items.Add(new CartItem { Name = "Organic Fresh Milk 1L", Sku = "8850123456789", PricePerItem = 2.50m, Quantity = 1 });
            Items.Add(new CartItem { Name = "Coca-Cola 330ml", Sku = "8850123456796", PricePerItem = 0.50m, Quantity = 2 });
            Items.Add(new CartItem { Name = "Pepsi 330ml", Sku = "8850123456802", PricePerItem = 0.50m, Quantity = 1 });
            Items.Add(new CartItem { Name = "Sprite Lemon-Lime 330ml", Sku = "8850123456819", PricePerItem = 0.50m, Quantity = 3 });
            Items.Add(new CartItem { Name = "Fanta Orange 330ml", Sku = "8850123456826", PricePerItem = 0.50m, Quantity = 1 });
            Items.Add(new CartItem { Name = "Dasani Drinking Water 600ml", Sku = "8850123456833", PricePerItem = 0.35m, Quantity = 2 });
            Items.Add(new CartItem { Name = "Pocari Sweat 500ml", Sku = "8850123456840", PricePerItem = 1.20m, Quantity = 1 });
            Items.Add(new CartItem { Name = "Red Bull Energy Drink 250ml", Sku = "8850123456857", PricePerItem = 1.80m, Quantity = 2 });
            Items.Add(new CartItem { Name = "Orange Juice 1L", Sku = "8850123456864", PricePerItem = 2.10m, Quantity = 1 });
            Items.Add(new CartItem { Name = "Green Tea 500ml", Sku = "8850123456871", PricePerItem = 0.95m, Quantity = 2 });
            Items.Add(new CartItem { Name = "Iced Coffee Latte 250ml", Sku = "8850123456888", PricePerItem = 1.60m, Quantity = 1 });

            RaiseAll();
        }

        public void ClearCart()
        {
            Items.Clear();
            RaiseAll();
        }

        public void AddItem(string name, string sku, decimal price, int qty = 1)
        {
            var existing = Items.FirstOrDefault(i => i.Sku == sku);
            if (existing != null) existing.Quantity += qty;
            else Items.Add(new CartItem { Name = name, Sku = sku, PricePerItem = price, Quantity = qty });
            RaiseAll();
        }

        public void RemoveItem(CartItem item)   
        {
            Items.Remove(item);
            RaiseAll();
        }

        private void RaiseAll()
        {
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(ItemCount));
            OnPropertyChanged(nameof(Total));
        }
    }
}
