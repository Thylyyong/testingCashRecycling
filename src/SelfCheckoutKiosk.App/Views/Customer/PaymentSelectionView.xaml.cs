using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using SelfCheckoutKiosk.App.Services;
using SelfCheckoutKiosk.App.ViewModels.Customer;

namespace SelfCheckoutKiosk.App.Views.Customer
{
    /// <summary>
    /// Page that lets the customer choose a payment method.
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
        }

        // Navigate using the root window frame so it replaces the whole page, not a sub-frame
        private Frame GetRootFrame()
            => App.MainWindowInstance?.MainRootFrame ?? this.Frame;

        private void CashCard_Click(object sender, RoutedEventArgs e)
        {
            GetRootFrame().Navigate(
                typeof(IngestionProgressView),
                null,
                new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });
        }

        private void KhqrCard_Click(object sender, RoutedEventArgs e)
        {
            GetRootFrame().Navigate(
                typeof(QRPaymentView),
                null,
                new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });
        }

        private void CardCard_Click(object sender, RoutedEventArgs e)
        {
            // Disabled – not reachable until payment terminal integration is ready
        }

        private void IntlQrCard_Click(object sender, RoutedEventArgs e)
        {
            // Not yet implemented
        }

        private void MembershipCard_Click(object sender, RoutedEventArgs e)
        {
            // Not yet implemented
        }

        private void CouponCard_Click(object sender, RoutedEventArgs e)
        {
            // Not yet implemented
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            GetRootFrame().Navigate(
                typeof(CartView),
                null,
                new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromLeft });
        }
    }
}