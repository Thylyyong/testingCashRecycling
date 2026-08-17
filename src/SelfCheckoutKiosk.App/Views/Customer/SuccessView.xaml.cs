using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SelfCheckoutKiosk.App.Models;
using SelfCheckoutKiosk.App.Services;
using SelfCheckoutKiosk.App.ViewModels.Customer;
using System;
using System.Diagnostics;

namespace SelfCheckoutKiosk.App.Views.Customer
{
    public sealed partial class SuccessView : Page
    {
        public LocalizationService Localizer => LocalizationService.Instance;
        private const int AutoReturnSeconds = 60;
        private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(50);

        public SuccessViewModel? ViewModel { get; private set; }
        private DispatcherTimer? _autoReturnTimer;
        private double _secondsRemaining = AutoReturnSeconds;
        private int _lastDisplayedSecond = AutoReturnSeconds;

        public SuccessView()
        {
            InitializeComponent();
            DoneButton.Click += DoneButton_Click;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is not Payment payment)
            {
                Debug.WriteLine("[WARN] SuccessView reached without a Payment parameter. Returning home.");
                INavigationService fallbackNav = App.MainWindowInstance?.NavigationService
                    ?? new NavigationService(Frame);
                fallbackNav.NavigateTo(typeof(HomeView), null);
                return;
            }

            INavigationService navigationService = App.MainWindowInstance?.NavigationService
                ?? new NavigationService(Frame);

            ViewModel = new SuccessViewModel(
                navigationService,
                App.ReceiptPrinterServiceInstance,
                payment
            );

            RenderDetails();
            StartAutoReturnTimer();

            // PrintReceipt() is void, so there's no real success/failure signal here.
            // This only catches thrown exceptions — if the printer service fails
            // silently (logs internally, returns normally), this banner won't fire.
            // Best real fix: have PrintReceipt() / IReceiptPrinterService.Print()
            // return bool, or raise a PrintFailed event you can subscribe to.
            try
            {
                ViewModel.PrintReceipt();
                PrinterWarningBanner.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WARN] Receipt print failed: {ex.Message}");
                PrinterWarningBanner.Visibility = Visibility.Visible;
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            StopAutoReturnTimer();
        }

        private void RenderDetails()
        {
            if (ViewModel == null) return;

            TransactionIdText.Text = $"Transaction #{ViewModel.TransactionId}";
            MethodLabelText.Text = $"{Localizer.GetString("PaidWith")} {ViewModel.MethodLabel}";

            // Split out instead of FormattedTotalPaid, which bundles USD + KHR together
            TotalPaidText.Text = $"${ViewModel.Payment.TotalPaidUsd:0.00}";
            TotalPaidKhrText.Text = $"≈ ៛{ViewModel.Payment.TotalPaidKhr:N0}";
        }

        private void StartAutoReturnTimer()
        {
            _secondsRemaining = AutoReturnSeconds;
            _lastDisplayedSecond = AutoReturnSeconds;
            UpdateTimerUI();

            _autoReturnTimer = new DispatcherTimer
            {
                Interval = TickInterval
            };
            _autoReturnTimer.Tick += AutoReturnTimer_Tick;
            _autoReturnTimer.Start();
        }

        private void AutoReturnTimer_Tick(object? sender, object e)
        {
            _secondsRemaining -= TickInterval.TotalSeconds;

            if (_secondsRemaining <= 0)
            {
                StopAutoReturnTimer();
                ViewModel?.ReturnHome();
            }
            else
            {
                UpdateTimerUI();
            }
        }

        private void UpdateTimerUI()
        {
            // Bar smoothly shrinks from both left and right toward the center
            double progressRatio = Math.Clamp(_secondsRemaining / AutoReturnSeconds, 0.0, 1.0);
            RedirectProgressScale.ScaleX = progressRatio;

            // Text only updates once per whole second (avoids flicker)
            int wholeSecond = (int)Math.Ceiling(_secondsRemaining);
            if (wholeSecond != _lastDisplayedSecond)
            {
                _lastDisplayedSecond = wholeSecond;
                CountdownNumberText.Text = wholeSecond.ToString();
            }
        }

        private void StopAutoReturnTimer()
        {
            if (_autoReturnTimer != null)
            {
                _autoReturnTimer.Stop();
                _autoReturnTimer.Tick -= AutoReturnTimer_Tick;
                _autoReturnTimer = null;
            }
        }

        private void DoneButton_Click(object sender, RoutedEventArgs e)
        {
            StopAutoReturnTimer();
            ViewModel?.ReturnHome();
        }
    }
}