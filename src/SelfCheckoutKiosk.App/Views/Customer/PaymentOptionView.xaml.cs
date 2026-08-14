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

            AddHandler(
                PointerPressedEvent,
                new PointerEventHandler(UserInteraction),
                true);

            AddHandler(
                KeyDownEvent,
                new KeyEventHandler(UserInteraction),
                true);

            Loaded += PaymentOptionView_Loaded;
            Unloaded += PaymentOptionView_Unloaded;
        }

        private void PaymentOptionView_Loaded(object sender, RoutedEventArgs e)
        {
            StartInactivityTimer();
        }

        private void PaymentOptionView_Unloaded(object sender, RoutedEventArgs e)
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

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            ResetInactivityTimer();

            ViewModel.GoBack();
        }
    }
}