using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using SelfCheckoutKiosk.App.Services;
using SelfCheckoutKiosk.App.ViewModels.Customer;
using System;

namespace SelfCheckoutKiosk.App.Views.Customer
{
    public sealed partial class HomeView : Page
    {
        public HomeViewModel ViewModel { get; }

        private readonly DispatcherTimer _inactivityTimer;

        public HomeView()
        {
            InitializeComponent();

            INavigationService navigationService;

            if (App.MainWindowInstance?.NavigationService != null)
            {
                navigationService = App.MainWindowInstance.NavigationService;
            }
            else
            {
                navigationService = new NavigationService(Frame);
            }

            ViewModel = new HomeViewModel(navigationService);

            _inactivityTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(120)
            };

            _inactivityTimer.Tick += InactivityTimer_Tick;

            AddHandler(
                PointerPressedEvent,
                new PointerEventHandler(UserInteraction),
                true);

            AddHandler(
                KeyDownEvent,
                new KeyEventHandler(UserInteraction),
                true);

            Loaded += HomeView_Loaded;
            Unloaded += HomeView_Unloaded;
        }

        private void HomeView_Loaded(object sender, RoutedEventArgs e)
        {
            StoreHoursText.Text = ViewModel.StoreHoursText;

            StartInactivityTimer();
        }

        private void HomeView_Unloaded(object sender, RoutedEventArgs e)
        {
            StopInactivityTimer();
        }

        private void StartInactivityTimer()
        {
            _inactivityTimer.Stop();
            _inactivityTimer.Start();
        }

        private void ResetInactivityTimer()
        {
            _inactivityTimer.Stop();
            _inactivityTimer.Start();
        }

        private void StopInactivityTimer()
        {
            _inactivityTimer.Stop();
        }

        private void UserInteraction(object sender, RoutedEventArgs e)
        {
            ResetInactivityTimer();
        }

        private void InactivityTimer_Tick(object? sender, object e)
        {
            _inactivityTimer.Stop();

            ViewModel.ProceedToKioskBaseView();
        }

        private void ScanItemCard_Click(object sender, RoutedEventArgs e)
        {
            ResetInactivityTimer();

            ViewModel.ProceedToCart();
        }

        private void PaymentOptionsCard_Click(object sender, RoutedEventArgs e)
        {
            ResetInactivityTimer();

            ViewModel.ProceedToPaymentOptions();
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            ResetInactivityTimer();

            ViewModel.RequestHelp();
        }

        private void LanguageItem_Click(object sender, RoutedEventArgs e)
        {
            ResetInactivityTimer();

            if (sender is MenuFlyoutItem menuItem)
            {
                CurrentLanguageText.Text = menuItem.Text;

                if (menuItem.Text == "ភាសាខ្មែរ")
                {
                    CurrentLanguageText.FontFamily =
                        (Microsoft.UI.Xaml.Media.FontFamily)
                        Application.Current.Resources["KhmerFont"];

                    CurrentLanguageFlag.Source = new BitmapImage(
                        new Uri("ms-appx:///Assets/Images/Flag/km-flag.png"));
                }
                else
                {
                    CurrentLanguageText.FontFamily =
                        new Microsoft.UI.Xaml.Media.FontFamily("Segoe UI");

                    CurrentLanguageFlag.Source = new BitmapImage(
                        new Uri("ms-appx:///Assets/Images/Flag/en-flag.png"));
                }
            }
        }

        private void ScanIcon_Loaded(object sender, RoutedEventArgs e)
        {
            ScanIconAnimation.Begin();
        }

        private void PaymentIcon_Loaded(object sender, RoutedEventArgs e)
        {
            PaymentIconAnimation.Begin();
        }
    }
}