using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using SelfCheckoutKiosk.App.ViewModels.Customer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SelfCheckoutKiosk.App.Views.Customer
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class CartView : Page
    {
        private readonly CartViewModel _viewModel = new();

        public CartView()
        {
            InitializeComponent();

            BackToScanButton.Click += BackToScanButton_Click;
            CheckoutButton.Click += CheckoutButton_Click;

            this.Loaded += CartView_Loaded;
        }

        private void CartView_Loaded(object sender, RoutedEventArgs e)
        {
            // TEST: swap this line to try the other state
            _viewModel.LoadMockData();   // populated state
            // _viewModel.ClearCart();   // empty state

            RefreshCartUI();
        }

        // The one method that decides which state shows
        private void RefreshCartUI()
        {
            if (_viewModel.IsEmpty)
            {
                EmptyCartState.Visibility = Visibility.Visible;
                ItemsListArea.Visibility = Visibility.Collapsed;
                ScanAnimation.Begin();
            }
            else
            {
                ScanAnimation.Stop();
                EmptyCartState.Visibility = Visibility.Collapsed;
                ItemsListArea.Visibility = Visibility.Visible;
                CartItemsControl.ItemsSource = _viewModel.Items;
            }

            ItemCountLabel.Text = $"{_viewModel.ItemCount} items";
            ItemCountSubLabel.Text = $"{_viewModel.ItemCount} items in cart";
            TotalAmountLabel.Text = $"${_viewModel.Total:0.00}";
        }

        private void Increment_Click(object sender, RoutedEventArgs e)
        {
            var item = (CartItem)((Button)sender).Tag;
            item.Quantity++;
            RefreshCartUI();
        }

        private void Decrement_Click(object sender, RoutedEventArgs e)
        {
            var item = (CartItem)((Button)sender).Tag;
            if (item.Quantity > 1) item.Quantity--;
            RefreshCartUI();
        }

        private void RemoveItem_Click(object sender, RoutedEventArgs e)
        {
            var item = (CartItem)((Button)sender).Tag;
            _viewModel.RemoveItem(item);
            RefreshCartUI();
        }

        private void BackToScanButton_Click(object sender, RoutedEventArgs e)
        {
            var navigationService = App.MainWindowInstance!.NavigationService;
            if (navigationService.CurrentPageType == typeof(CartView))
                navigationService.NavigateTo(typeof(KioskBaseView), null, SlideNavigationTransitionEffect.FromLeft);
            else
                navigationService.GoBack(SlideNavigationTransitionEffect.FromLeft);
        }

        private void CheckoutButton_Click(object sender, RoutedEventArgs e)
        {
            var navigationService = App.MainWindowInstance!.NavigationService;
            // navigationService.NavigateTo(typeof(PaymentView));
        }
    }
}
