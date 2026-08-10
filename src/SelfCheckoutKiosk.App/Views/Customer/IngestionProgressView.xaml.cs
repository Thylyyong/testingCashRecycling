using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SelfCheckoutKiosk.App.Models;
using SelfCheckoutKiosk.App.Services;
using SelfCheckoutKiosk.App.ViewModels.Customer;
using System.Diagnostics;

namespace SelfCheckoutKiosk.App.Views.Customer
{
    public sealed partial class IngestionProgressView : Page
    {
        public IngestionProgressViewModel ViewModel { get; }

        public IngestionProgressView()
        {
            InitializeComponent();

            INavigationService navigationService = App.MainWindowInstance?.NavigationService
                ?? new NavigationService(Frame);

            ViewModel = new IngestionProgressViewModel(
                navigationService,
                App.CartServiceInstance,
                App.PaymentServiceInstance
            );

            BackButton.Click += BackButton_Click;
            ConfirmPaymentButton.Click += ConfirmPaymentButton_Click;

            foreach (var child in UsdNotesPanel.Children)
            {
                if (child is Button usdBtn && usdBtn.Tag is string usdTag && decimal.TryParse(usdTag, out var usdVal))
                    usdBtn.Click += (s, e) => SubmitCash(usdVal, isUsd: true);
            }

            foreach (var child in KhrNotesPanel.Children)
            {
                if (child is Button khrBtn && khrBtn.Tag is string khrTag && decimal.TryParse(khrTag, out var khrVal))
                    khrBtn.Click += (s, e) => SubmitCash(khrVal, isUsd: false);
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            ViewModel.InitializeTransaction();
            RefreshUI();
        }

        private void AttemptRow_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is Border border && border.DataContext is PaymentAttempt attempt)
            {
                bool isAccepted = attempt.Result == PaymentAttemptResult.Accepted;

                border.Background = new SolidColorBrush(isAccepted
                    ? Windows.UI.Color.FromArgb(255, 236, 253, 245)
                    : Windows.UI.Color.FromArgb(255, 254, 242, 242));

                border.BorderBrush = new SolidColorBrush(isAccepted
                    ? Windows.UI.Color.FromArgb(255, 167, 243, 208)
                    : Windows.UI.Color.FromArgb(255, 254, 202, 202));

                var accentColor = isAccepted
                    ? Windows.UI.Color.FromArgb(255, 5, 150, 105)
                    : Windows.UI.Color.FromArgb(255, 220, 38, 38);

                if (border.FindName("StatusIcon") is FontIcon statusIcon)
                {
                    statusIcon.Glyph = isAccepted ? "\uE73E" : "\uE711"; // checkmark / cross
                    statusIcon.Foreground = new SolidColorBrush(accentColor);
                }

                if (border.FindName("AmountText") is TextBlock amountText)
                {
                    amountText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 45, 55, 72));
                }
            }
        }

        private void SubmitCash(decimal amount, bool isUsd)
        {
            bool accepted = ViewModel.SubmitCash(amount, isUsd, out string reason);

            Debug.WriteLine($"[PAYMENT] {(accepted ? "Accepted" : "Rejected")} {amount} {(isUsd ? "USD" : "KHR")} — {reason}");

            RefreshUI();
        }

        private void RefreshUI()
        {
            TotalDueText.Text = ViewModel.FormattedTotalDue;
            TotalPaidText.Text = ViewModel.FormattedTotalPaid;
            RemainingDueText.Text = ViewModel.FormattedRemaining;

            PaidProgressBar.Value = ViewModel.TotalDueUsd > 0
                ? (double)(ViewModel.TotalPaidUsd / ViewModel.TotalDueUsd) * 100
                : 0;

            AttemptsListControl.ItemsSource = null;
            AttemptsListControl.ItemsSource = ViewModel.Attempts;

            ConfirmPaymentButton.IsEnabled = ViewModel.IsFullyPaid;
            RemainingDueText.Foreground = new SolidColorBrush(ViewModel.IsFullyPaid
                ? Windows.UI.Color.FromArgb(255, 5, 150, 105)
                : Windows.UI.Color.FromArgb(255, 220, 38, 38));

            BackButton.Visibility = ViewModel.CanNavigateBack ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ViewModel.CanNavigateBack) return;

            var dialog = new ContentDialog
            {
                Title = "Cancel Payment?",
                Content = new TextBlock
                {
                    Text = "Going back will cancel this payment and return you to payment method selection. Continue?",
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 420
                },
                PrimaryButtonText = "Cancel & Go Back",
                SecondaryButtonText = "Continue Payment",
                PrimaryButtonStyle = (Style)Application.Current.Resources["AccentButtonStyle"],
                XamlRoot = this.Content?.XamlRoot ?? this.XamlRoot,
                RequestedTheme = ElementTheme.Light
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                ViewModel.NavigateBackToPaymentSelection();
            }
        }

        private void ConfirmPaymentButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ConfirmPaymentAndProceed();
        }
    }
}