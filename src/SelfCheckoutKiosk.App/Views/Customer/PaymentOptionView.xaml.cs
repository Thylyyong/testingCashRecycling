using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using SelfCheckoutKiosk.App.Services;
using SelfCheckoutKiosk.App.ViewModels.Customer;
using System;

namespace SelfCheckoutKiosk.App.Views.Customer
{
    /// <summary>
    /// View-only page showing all available payment method options.
    /// </summary>
    public sealed partial class PaymentOptionView : Page
    {
        public PaymentOptionViewModel ViewModel { get; }
        public LocalizationService Localizer => LocalizationService.Instance;

        private readonly DispatcherTimer _inactivityTimer;

        public PaymentOptionView()
        {
            InitializeComponent();

            INavigationService navigationService = App.MainWindowInstance?.NavigationService
                ?? new NavigationService(Frame);

            ViewModel = new PaymentOptionViewModel(navigationService);

            _inactivityTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(120)
            };

            _inactivityTimer.Tick += InactivityTimer_Tick;

            AddHandler(PointerPressedEvent, new PointerEventHandler(UserInteraction), true);
            AddHandler(KeyDownEvent, new KeyEventHandler(UserInteraction), true);

            Loaded += PaymentOptionView_Loaded;
            Unloaded += PaymentOptionView_Unloaded;
        }

        private Frame GetRootFrame()
            => App.MainWindowInstance?.MainRootFrame ?? this.Frame;

        private void PaymentOptionView_Loaded(object sender, RoutedEventArgs e)
            => _inactivityTimer.Start();

        private void PaymentOptionView_Unloaded(object sender, RoutedEventArgs e)
            => _inactivityTimer.Stop();

        private void ResetInactivityTimer()
        {
            _inactivityTimer.Stop();
            _inactivityTimer.Start();
        }

        private void UserInteraction(object sender, RoutedEventArgs e)
            => ResetInactivityTimer();

        private void InactivityTimer_Tick(object? sender, object e)
        {
            _inactivityTimer.Stop();
            ViewModel.ProceedToKioskBaseView();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            ResetInactivityTimer();
            GetRootFrame().Navigate(
                typeof(CartView),
                null,
                new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromLeft });
        }

        private void CashCard_Click(object sender, RoutedEventArgs e)
        {
            ResetInactivityTimer();
            GetRootFrame().Navigate(
                typeof(IngestionProgressView),
                null,
                new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });
        }

        private void KhqrCard_Click(object sender, RoutedEventArgs e)
        {
            ResetInactivityTimer();
            GetRootFrame().Navigate(
                typeof(QRPaymentView),
                null,
                new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });
        }
    }
}