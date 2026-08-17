using Microsoft.UI.Xaml.Media.Animation;
using SelfCheckoutKiosk.App.Models;
using SelfCheckoutKiosk.App.Services;
using SelfCheckoutKiosk.App.Views.Customer;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace SelfCheckoutKiosk.App.ViewModels.Customer
{
    public class CartViewModel : INotifyPropertyChanged
    {
        private readonly IProductService _productService;
        private readonly ICartService _cartService;

        public INavigationService NavigationService { get; }

        public ObservableCollection<CartItem> Items => _cartService.Items;

        public bool IsEmpty => _cartService.IsEmpty;
        public int ItemCount => _cartService.TotalItemCount;
        public decimal Total => _cartService.TotalUsd;
        public decimal TotalKhr => _cartService.TotalKhr;
        public bool IsUsd => _cartService.IsUsd;
        public decimal ExchangeRate => _cartService.ExchangeRate;
        public bool HasItems => !IsEmpty;
        public string CurrencyLabel => IsUsd ? "USD" : "KHR";
        public string ItemCountText => $"{ItemCount}";
        public string ItemCountSubText => $"{ItemCount}";

        public string FormattedTotalUsd => IsUsd ? $"${Total:0.00}" : $"៛{TotalKhr:N0}";
        public string FormattedTotalKhr => IsUsd ? $"≈ ៛{TotalKhr:N0}" : $"≈ ${Total:0.00}";

        public event PropertyChangedEventHandler? PropertyChanged;

        public CartViewModel(INavigationService navigationService, IProductService productService, ICartService cartService)
        {
            NavigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
            _productService = productService ?? throw new ArgumentNullException(nameof(productService));
            _cartService = cartService ?? throw new ArgumentNullException(nameof(cartService));

            _cartService.PropertyChanged += OnCartServicePropertyChanged;
        }

        private void OnCartServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ICartService.TotalUsd)) OnPropertyChanged(nameof(Total));

            if (e.PropertyName == nameof(ICartService.TotalKhr)) OnPropertyChanged(nameof(TotalKhr));

            if (e.PropertyName == nameof(ICartService.TotalItemCount))
            {
                OnPropertyChanged(nameof(ItemCount));
                OnPropertyChanged(nameof(ItemCountText));
                OnPropertyChanged(nameof(ItemCountSubText));
            }

            if (e.PropertyName == nameof(ICartService.IsEmpty))
            {
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(HasItems)); 
            }

            if (e.PropertyName == nameof(ICartService.IsUsd))
            {
                OnPropertyChanged(nameof(IsUsd));
                OnPropertyChanged(nameof(CurrencyLabel));
            }


            OnPropertyChanged(nameof(FormattedTotalUsd));
            OnPropertyChanged(nameof(FormattedTotalKhr));
        }

        public void ToggleCurrency()
        {
            _cartService.IsUsd = !_cartService.IsUsd;
        }

        public Product? FindProductBySku(string sku)
        {
            if (string.IsNullOrWhiteSpace(sku)) return null;
            return _productService.GetAllProducts()
                .FirstOrDefault(p => p.Sku.Equals(sku, StringComparison.OrdinalIgnoreCase));
        }

        public bool TryAddScannedBarcode(string sku, out Product? foundProduct)
        {
            foundProduct = FindProductBySku(sku);
            if (foundProduct != null)
            {
                AddItem(foundProduct.Name, foundProduct.Sku, foundProduct.Price, 1);
                return true;
            }
            return false;
        }

        public void AddItem(string name, string sku, decimal price, int qty = 1)
        {
            _cartService.AddItem(name, sku, price, qty);
        }

        public void DecrementOrRemove(CartItem item)
        {
            _cartService.DecrementOrRemove(item);
        }

        public void ClearCart()
        {
            _cartService.ClearCart();
        }

        public void CancelOrderAndProceedHome()
        {
            _cartService.ClearCart();
            ProceedToHome();
        }

        public void ProceedToHome()
        {
            NavigationService.NavigateTo(typeof(HomeView), null, SlideNavigationTransitionEffect.FromLeft);
        }

        public void ProceedToPaymentSelection()
        {
            NavigationService.NavigateTo(typeof(PaymentSelectionView), null, SlideNavigationTransitionEffect.FromRight);
        }

        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}