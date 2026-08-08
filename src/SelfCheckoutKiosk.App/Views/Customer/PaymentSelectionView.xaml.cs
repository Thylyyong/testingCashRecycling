using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SelfCheckoutKiosk.App.Services;
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
    /// Page that lets the customer choose a payment method.
    /// </summary>
    public sealed partial class PaymentSelectionView : Page
    {
        public PaymentSelectionViewModel ViewModel { get; }

        public PaymentSelectionView()
        {
            InitializeComponent();

            INavigationService navigationService = App.MainWindowInstance?.NavigationService
                ?? new NavigationService(Frame);

            ViewModel = new PaymentSelectionViewModel(navigationService);
        }

        private void CashCard_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.SelectPaymentMethod("Cash");
            // TODO: Navigate to cash payment flow
        }

        private void KhqrCard_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.SelectPaymentMethod("KHQR");
            // TODO: Navigate to KHQR payment flow
        }

        private void CardCard_Click(object sender, RoutedEventArgs e)
        {
            // Disabled by default - not reachable until enabled
            ViewModel.SelectPaymentMethod("Card");
        }

        private void IntlQrCard_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.SelectPaymentMethod("InternationalQR");
        }

        private void MembershipCard_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.SelectPaymentMethod("MembershipCard");
        }

        private void CouponCard_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.SelectPaymentMethod("Coupon");
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.Cancel();
            ViewModel.ProceedToCart();
        }
    }
}