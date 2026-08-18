using Microsoft.UI.Text;
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
        public LocalizationService Localizer => LocalizationService.Instance;
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

        public static Visibility BoolToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            ViewModel.InitializeTransaction();
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
        }

        private async void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ViewModel.CanNavigateBack) return;

            var globalFont = (FontFamily)Application.Current.Resources["GlobalAppFont"];

            // Primary Button: AccentButtonStyle + GlobalAppFont
            var baseAccentStyle = (Style)Application.Current.Resources["AccentButtonStyle"];
            var primaryButtonStyleWithFont = new Style(typeof(Button)) { BasedOn = baseAccentStyle };
            primaryButtonStyleWithFont.Setters.Add(new Setter(Control.FontFamilyProperty, globalFont));

            // Secondary Button: DialogButtonStyle + GlobalAppFont
            var baseDialogStyle = (Style)Application.Current.Resources["DialogButtonStyle"];
            var secondaryButtonStyleWithFont = new Style(typeof(Button)) { BasedOn = baseDialogStyle };
            secondaryButtonStyleWithFont.Setters.Add(new Setter(Control.FontFamilyProperty, globalFont));

            var dialog = new ContentDialog
            {
                Content = new StackPanel
                {
                    Spacing = 16,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children =
                    {
                        new FontIcon
                        {
                            Glyph = "\uE814", // Warning / Alert icon
                            FontFamily = new FontFamily("Segoe Fluent Icons"),
                            FontSize = 42,
                            Foreground = (Brush)Application.Current.Resources["DangerBrush"],
                            HorizontalAlignment = HorizontalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = Localizer.GetString("CancelPaymentTitle"),
                            FontSize = 20,
                            FontWeight = FontWeights.SemiBold,
                            TextAlignment = TextAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            FontFamily = globalFont
                        },
                        new TextBlock
                        {
                            Text = Localizer.GetString("CancelPaymentMessage"),
                            FontSize = 16,
                            TextWrapping = TextWrapping.Wrap,
                            TextAlignment = TextAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            MaxWidth = 450,
                            FontFamily = globalFont
                        }
                    }
                },
                Style = (Style)Application.Current.Resources["KioskContentDialogStyle"],
                PrimaryButtonText = Localizer.GetString("ContinuePayment"),
                SecondaryButtonText = Localizer.GetString("CancelPayment"),
                PrimaryButtonStyle = primaryButtonStyleWithFont,
                SecondaryButtonStyle = secondaryButtonStyleWithFont,
                XamlRoot = this.XamlRoot,
                RequestedTheme = ElementTheme.Light
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Secondary)
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