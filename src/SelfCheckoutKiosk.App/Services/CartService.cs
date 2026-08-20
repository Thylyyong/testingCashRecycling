using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using SelfCheckoutKiosk.App.Models;

namespace SelfCheckoutKiosk.App.Services
{
    public class CartService : ICartService
    {
        private bool _isUsd = true;
        private decimal _exchangeRate = 4100m;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<CartItem> Items { get; } = new();

        public bool IsUsd
        {
            get => _isUsd;
            set
            {
                if (_isUsd != value)
                {
                    _isUsd = value;
                    OnPropertyChanged();
                }
            }
        }

        public decimal ExchangeRate
        {
            get => _exchangeRate;
            set
            {
                if (_exchangeRate != value)
                {
                    _exchangeRate = value;
                    OnPropertyChanged();
                    RecalculateTotals();
                }
            }
        }

        public decimal TotalUsd => Items.Sum(i => i.LineTotal);
        public decimal TotalKhr => SelfCheckoutKiosk.Core.Currency.DualCurrencyCalculator.CalculateTotalKhr(TotalUsd, ExchangeRate);
        public int TotalItemCount => Items.Sum(i => i.Quantity);
        public bool IsEmpty => Items.Count == 0;

        public CartService()
        {
            Items.CollectionChanged += OnItemsCollectionChanged;
        }

        public void AddItem(string name, string sku, decimal price, int quantity = 1)
        {
            var existing = Items.FirstOrDefault(i => i.Sku.Equals(sku, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.Quantity += quantity;

                // Bring to top so user gets visual confirmation
                int currentIndex = Items.IndexOf(existing);
                if (currentIndex > 0)
                {
                    Items.Move(currentIndex, 0);
                }
            }
            else
            {
                // Insert new item at top
                Items.Insert(0, new CartItem
                {
                    Sku = sku,
                    Name = name,
                    UnitPrice = price,
                    Quantity = quantity
                });
            }
        }

        public void DecrementOrRemove(CartItem item)
        {
            if (item == null) return;

            if (item.Quantity > 1)
            {
                item.Quantity--;
            }
            else
            {
                Items.Remove(item);
            }
        }

        public void RemoveItem(CartItem item)
        {
            if (item != null && Items.Contains(item))
            {
                Items.Remove(item);
            }
        }

        public void ClearCart()
        {
            foreach (var item in Items)
            {
                item.PropertyChanged -= Item_PropertyChanged;
            }
            Items.Clear();
            RecalculateTotals();
        }

        private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (CartItem item in e.NewItems)
                {
                    item.PropertyChanged += Item_PropertyChanged;
                }
            }

            if (e.OldItems != null)
            {
                foreach (CartItem item in e.OldItems)
                {
                    item.PropertyChanged -= Item_PropertyChanged;
                }
            }

            RecalculateTotals();
        }

        private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CartItem.Quantity) || e.PropertyName == nameof(CartItem.LineTotal))
            {
                RecalculateTotals();
            }
        }

        private void RecalculateTotals()
        {
            OnPropertyChanged(nameof(TotalUsd));
            OnPropertyChanged(nameof(TotalKhr));
            OnPropertyChanged(nameof(TotalItemCount));
            OnPropertyChanged(nameof(IsEmpty));
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}