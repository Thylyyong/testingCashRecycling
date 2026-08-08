using Microsoft.UI.Xaml.Media.Animation;
using SelfCheckoutKiosk.App.Models;
using SelfCheckoutKiosk.App.Services;
using SelfCheckoutKiosk.App.Views.Customer;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace SelfCheckoutKiosk.App.ViewModels.Customer
{
    public class CartItem : INotifyPropertyChanged
    {
        public Product Product { get; }

        // Forward properties to Product so existing XAML bindings continue working without changes
        public string Name => Product.Name;
        public string Sku => Product.Sku;
        public decimal PricePerItem => Product.Price;

        private int _quantity;
        public int Quantity
        {
            get => _quantity;
            set
            {
                if (_quantity != value)
                {
                    _quantity = value;
                    OnPropertyChanged(nameof(Quantity));
                    OnPropertyChanged(nameof(LineTotal));
                }
            }
        }

        public decimal LineTotal => PricePerItem * Quantity;

        public CartItem(Product product, int quantity = 1)
        {
            Product = product ?? throw new ArgumentNullException(nameof(product));
            _quantity = quantity;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class CartViewModel : INotifyPropertyChanged
    {
        private readonly IProductService _productService;

        public INavigationService NavigationService { get; }
        public ObservableCollection<CartItem> Items { get; } = new();

        public bool IsEmpty => Items.Count == 0;
        public int ItemCount => Items.Sum(i => i.Quantity);
        public decimal Total => Items.Sum(i => i.LineTotal);

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public CartViewModel(INavigationService navigationService, IProductService productService)
        {
            NavigationService = navigationService;
            _productService = productService;

            Items.CollectionChanged += OnItemsCollectionChanged;
        }

        public void LoadMockData()
        {
            Items.Clear();

            // Fetch product catalog directly from service
            foreach (var product in _productService.GetAllProducts())
            {
                AddItem(product, 1);
            }
        }

        public void ClearCart()
        {
            Items.Clear();
        }

        /// <summary>
        /// Adds a product to the cart or increments quantity if it already exists.
        /// </summary>
        public void AddItem(Product product, int qty = 1)
        {
            if (product == null || string.IsNullOrWhiteSpace(product.Sku)) return;

            // 1. Case-insensitive SKU lookup
            var existing = Items.FirstOrDefault(i => string.Equals(i.Sku, product.Sku, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                // 2. Increment quantity on existing item
                existing.Quantity += qty;

                // 3. Move re-scanned item to bottom of list
                int oldIndex = Items.IndexOf(existing);
                if (oldIndex < Items.Count - 1)
                {
                    Items.Move(oldIndex, Items.Count - 1);
                }
            }
            else
            {
                // 4. Add new item to cart
                Items.Add(new CartItem(product, qty));
            }
        }

        /// <summary>
        /// Primitive overload for backward compatibility.
        /// </summary>
        public void AddItem(string name, string sku, decimal price, int qty = 1)
        {
            var product = new Product
            {
                Name = name,
                Sku = sku,
                Price = price
            };

            AddItem(product, qty);
        }

        public void RemoveItem(CartItem item)
        {
            Items.Remove(item);
        }

        public void DecrementOrRemove(CartItem item)
        {
            if (item.Quantity > 1)
            {
                item.Quantity--;
            }
            else
            {
                Items.Remove(item);
            }
        }

        private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (CartItem item in e.OldItems)
                    item.PropertyChanged -= OnItemPropertyChanged;
            }

            if (e.NewItems != null)
            {
                foreach (CartItem item in e.NewItems)
                    item.PropertyChanged += OnItemPropertyChanged;
            }

            RaiseAll();
        }

        private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CartItem.Quantity) || e.PropertyName == nameof(CartItem.LineTotal))
            {
                RaiseAll();
            }
        }

        private void RaiseAll()
        {
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(ItemCount));
            OnPropertyChanged(nameof(Total));
        }

        public void ProceedToHome()
        {
            NavigationService.NavigateTo(
                typeof(HomeView),
                null,
                SlideNavigationTransitionEffect.FromLeft
            );
        }

        public void ProceedToPaymentSelection()
        {
            NavigationService.NavigateTo(
                typeof(PaymentSelectionView),
                null,
                SlideNavigationTransitionEffect.FromRight
            );
        }
    }
}