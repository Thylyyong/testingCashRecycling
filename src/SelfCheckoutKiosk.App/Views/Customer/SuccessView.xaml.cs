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
        private const int AutoReturnSeconds = 10;
        private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(50);

        public SuccessViewModel? ViewModel { get; private set; }
        private DispatcherTimer? _autoReturnTimer;
        private double _secondsRemaining = AutoReturnSeconds;
        private int _lastDisplayedSecond = AutoReturnSeconds;

        public SuccessView()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            Payment payment = e.Parameter as Payment ?? new Payment
            {
                Method = PaymentMethod.Cash,
                TotalDueUsd = App.CartServiceInstance.TotalUsd > 0 ? App.CartServiceInstance.TotalUsd : 5.00m,
                TotalPaidUsd = App.CartServiceInstance.TotalUsd > 0 ? App.CartServiceInstance.TotalUsd : 5.00m,
                ChangeDueUsd = 0,
                ExchangeRate = App.CartServiceInstance.ExchangeRate > 0 ? App.CartServiceInstance.ExchangeRate : 4100m,
                IsFullyPaid = true,
                CompletedAt = DateTime.Now
            };

            INavigationService navigationService = App.MainWindowInstance?.NavigationService
                ?? new NavigationService(Frame);

            ViewModel = new SuccessViewModel(
                navigationService,
                App.ReceiptPrinterServiceInstance,
                payment
            );

            RenderDetails();
            StartAutoReturnTimer();

            bool isPrinterAvailable = HardwareStatusManager.Instance.IsPrinterAvailable;
            if (isPrinterAvailable)
            {
                try
                {
                    ViewModel.PrintReceipt();
                    PrinterSuccessBanner.Visibility = Visibility.Visible;
                    PrinterWarningBanner.Visibility = Visibility.Collapsed;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[WARN] Receipt print failed: {ex.Message}");
                    PrinterSuccessBanner.Visibility = Visibility.Collapsed;
                    PrinterWarningBanner.Visibility = Visibility.Visible;
                }
            }
            else
            {
                PrinterSuccessBanner.Visibility = Visibility.Collapsed;
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
            _lastDisplayedSecond = -1;
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
                DispatcherQueue.TryEnqueue(() =>
                {
                    ViewModel?.ReturnHome();
                });
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
            if (RedirectProgressScale != null)
            {
                RedirectProgressScale.ScaleX = progressRatio;
            }

            // Text only updates once per whole second (avoids flicker)
            int wholeSecond = (int)Math.Ceiling(_secondsRemaining);
            if (wholeSecond != _lastDisplayedSecond)
            {
                _lastDisplayedSecond = wholeSecond;
                if (CountdownNumberText != null)
                {
                    CountdownNumberText.Text = wholeSecond.ToString();
                }
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
            if (ViewModel != null)
            {
                ViewModel.ReturnHome();
            }
            else
            {
                var nav = App.MainWindowInstance?.NavigationService ?? new NavigationService(Frame);
                nav.NavigateTo(typeof(KioskBaseView), null, Microsoft.UI.Xaml.Media.Animation.SlideNavigationTransitionEffect.FromRight);
            }
        }
    }
}