using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SelfCheckoutKiosk.App.Services;
using SelfCheckoutKiosk.App.ViewModels.Customer;
using SelfCheckoutKiosk.Core.Abstractions;
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
    /// CHECKOUT PAYMENT SELECTION PAGE: Choose payment method to complete order.
    /// Reached AFTER CartView ("Pay Now" button).
    /// Once the customer selects Cash or KHQR, this actively launches the payment flow
    /// (IngestionProgressView for Cash or QRPaymentView for KHQR).
    /// </summary>
    public sealed partial class PaymentSelectionView : Page
    {
        public PaymentSelectionViewModel ViewModel { get; }
        public LocalizationService Localizer => LocalizationService.Instance;
        public PaymentSelectionView()
        {
            InitializeComponent();

            INavigationService navigationService = App.MainWindowInstance?.NavigationService
                ?? new NavigationService(Frame);

            ViewModel = new PaymentSelectionViewModel(navigationService);

            Loaded += PaymentSelectionView_Loaded;
            Unloaded += PaymentSelectionView_Unloaded;
        }

        private void PaymentSelectionView_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateAvailability();
            HardwareStatusManager.Instance.PropertyChanged += HardwareStatusManager_PropertyChanged;
        }

        private void PaymentSelectionView_Unloaded(object sender, RoutedEventArgs e)
        {
            HardwareStatusManager.Instance.PropertyChanged -= HardwareStatusManager_PropertyChanged;
        }

        private void HardwareStatusManager_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            DispatcherQueue.TryEnqueue(UpdateAvailability);
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            UpdateAvailability();

            // Machine is disarmed (LED OFF) while customer is on payment selection
            if (App.CashRecyclerInstance is ICashRecycler recycler)
            {
                Task.Run(async () =>
                {
                    try { await recycler.DisarmAcceptanceAsync(); } catch { }
                });
            }
        }

        private void UpdateAvailability()
        {
            var primaryBrandBrush = (Brush)Application.Current.Resources["PrimaryBrandBrush"];
            var textMutedBrush = (Brush)Application.Current.Resources["TextMutedBrush"];
            var textDarkSlateBrush = (Brush)Application.Current.Resources["TextDarkSlateBrush"];
            var textSecondaryBrush = (Brush)Application.Current.Resources["TextSecondaryBrush"];

            // 1. Cash
            bool cashAvailable = HardwareStatusManager.Instance.IsCashAvailable;
            CashCard.IsEnabled = cashAvailable;
            CashCard.Opacity = cashAvailable ? 1.0 : 0.45;
            CashCard.Style = (Style)Resources[cashAvailable ? "PaymentCardEnabledStyle" : "PaymentCardDisabledStyle"];
            CashIcon.Foreground = cashAvailable ? primaryBrandBrush : textMutedBrush;
            CashTitleText.Foreground = cashAvailable ? textDarkSlateBrush : textMutedBrush;
            CashSubtitleText.Text = cashAvailable ? Localizer.GetString("CashSubTitle") : Localizer.GetString("Unavailable");
            CashSubtitleText.Foreground = cashAvailable ? textSecondaryBrush : textMutedBrush;

            // 2. KHQR
            bool qrAvailable = HardwareStatusManager.Instance.IsQrAvailable;
            KhqrCard.IsEnabled = qrAvailable;
            KhqrCard.Opacity = qrAvailable ? 1.0 : 0.45;
            KhqrCard.Style = (Style)Resources[qrAvailable ? "PaymentCardEnabledStyle" : "PaymentCardDisabledStyle"];
            KhqrIcon.Foreground = qrAvailable ? primaryBrandBrush : textMutedBrush;
            KhqrTitleText.Foreground = qrAvailable ? textDarkSlateBrush : textMutedBrush;
            KhqrSubtitleText.Text = qrAvailable ? Localizer.GetString("KhqrSubTitle") : Localizer.GetString("Unavailable");
            KhqrSubtitleText.Foreground = qrAvailable ? textSecondaryBrush : textMutedBrush;

            // 3. Debit / Credit Card
            bool cardAvailable = HardwareStatusManager.Instance.IsCardAvailable;
            CardCard.IsEnabled = cardAvailable;
            CardCard.Opacity = cardAvailable ? 1.0 : 0.45;
            CardCard.Style = (Style)Resources[cardAvailable ? "PaymentCardEnabledStyle" : "PaymentCardDisabledStyle"];
            CardIcon.Foreground = cardAvailable ? primaryBrandBrush : textMutedBrush;
            CardTitleText.Foreground = cardAvailable ? textDarkSlateBrush : textMutedBrush;
            CardSubtitleText.Text = cardAvailable ? Localizer.GetString("CardTitle") : Localizer.GetString("Unavailable");
            CardSubtitleText.Foreground = cardAvailable ? textSecondaryBrush : textMutedBrush;

            // 4. International QR
            bool intlQrAvailable = HardwareStatusManager.Instance.IsIntlQrAvailable;
            IntlQrCard.IsEnabled = intlQrAvailable;
            IntlQrCard.Opacity = intlQrAvailable ? 1.0 : 0.45;
            IntlQrCard.Style = (Style)Resources[intlQrAvailable ? "PaymentCardEnabledStyle" : "PaymentCardDisabledStyle"];
            IntlQrIcon.Foreground = intlQrAvailable ? primaryBrandBrush : textMutedBrush;
            IntlQrTitleText.Foreground = intlQrAvailable ? textDarkSlateBrush : textMutedBrush;
            IntlQrSubtitleText.Text = intlQrAvailable ? Localizer.GetString("IntlQrTitle") : Localizer.GetString("Unavailable");
            IntlQrSubtitleText.Foreground = intlQrAvailable ? textSecondaryBrush : textMutedBrush;

            // 5. Membership Card
            bool membershipAvailable = HardwareStatusManager.Instance.IsMembershipAvailable;
            MembershipCard.IsEnabled = membershipAvailable;
            MembershipCard.Opacity = membershipAvailable ? 1.0 : 0.45;
            MembershipCard.Style = (Style)Resources[membershipAvailable ? "PaymentCardEnabledStyle" : "PaymentCardDisabledStyle"];
            MembershipIcon.Foreground = membershipAvailable ? primaryBrandBrush : textMutedBrush;
            MembershipTitleText.Foreground = membershipAvailable ? textDarkSlateBrush : textMutedBrush;
            MembershipSubtitleText.Text = membershipAvailable ? Localizer.GetString("MembershipTitle") : Localizer.GetString("Unavailable");
            MembershipSubtitleText.Foreground = membershipAvailable ? textSecondaryBrush : textMutedBrush;

            // 6. Coupon
            bool couponAvailable = HardwareStatusManager.Instance.IsCouponAvailable;
            CouponCard.IsEnabled = couponAvailable;
            CouponCard.Opacity = couponAvailable ? 1.0 : 0.45;
            CouponCard.Style = (Style)Resources[couponAvailable ? "PaymentCardEnabledStyle" : "PaymentCardDisabledStyle"];
            CouponIcon.Foreground = couponAvailable ? primaryBrandBrush : textMutedBrush;
            CouponTitleText.Foreground = couponAvailable ? textDarkSlateBrush : textMutedBrush;
            CouponSubtitleText.Text = couponAvailable ? Localizer.GetString("CouponTitle") : Localizer.GetString("Unavailable");
            CouponSubtitleText.Foreground = couponAvailable ? textSecondaryBrush : textMutedBrush;
        }

        private void CashCard_Click(object sender, RoutedEventArgs e)
        {
            if (!HardwareStatusManager.Instance.IsCashAvailable) return;
            ViewModel.SelectPaymentMethod("Cash");
        }

        private void KhqrCard_Click(object sender, RoutedEventArgs e)
        {
            if (!HardwareStatusManager.Instance.IsQrAvailable) return;
            ViewModel.SelectPaymentMethod("KHQR");
        }

        private void CardCard_Click(object sender, RoutedEventArgs e)
        {
            if (!HardwareStatusManager.Instance.IsCardAvailable) return;
            ViewModel.SelectPaymentMethod("Card");
        }

        private void IntlQrCard_Click(object sender, RoutedEventArgs e)
        {
            if (!HardwareStatusManager.Instance.IsIntlQrAvailable) return;
            ViewModel.SelectPaymentMethod("InternationalQR");
        }

        private void MembershipCard_Click(object sender, RoutedEventArgs e)
        {
            if (!HardwareStatusManager.Instance.IsMembershipAvailable) return;
            ViewModel.SelectPaymentMethod("MembershipCard");
        }

        private void CouponCard_Click(object sender, RoutedEventArgs e)
        {
            if (!HardwareStatusManager.Instance.IsCouponAvailable) return;
            ViewModel.SelectPaymentMethod("Coupon");
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.Cancel();
            ViewModel.ProceedToCart();
        }
    }
}