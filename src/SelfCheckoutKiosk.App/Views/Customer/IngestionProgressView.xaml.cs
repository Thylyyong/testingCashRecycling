using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SelfCheckoutKiosk.App.Models;
using SelfCheckoutKiosk.App.Services;
using SelfCheckoutKiosk.App.ViewModels.Customer;
using SelfCheckoutKiosk.Core.Abstractions;
using System;
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

            Loaded += IngestionProgressView_Loaded;
            Unloaded += IngestionProgressView_Unloaded;
        }

        private void IngestionProgressView_Loaded(object sender, RoutedEventArgs e)
        {
            HardwareStatusManager.Instance.PropertyChanged += HardwareStatusManager_PropertyChanged;
            App.PaymentServiceInstance.CashJamReported += PaymentService_CashJamReported;
            App.PaymentServiceInstance.CashFaultReported += PaymentService_CashFaultReported;
        }

        private void IngestionProgressView_Unloaded(object sender, RoutedEventArgs e)
        {
            HardwareStatusManager.Instance.PropertyChanged -= HardwareStatusManager_PropertyChanged;
            App.PaymentServiceInstance.CashJamReported -= PaymentService_CashJamReported;
            App.PaymentServiceInstance.CashFaultReported -= PaymentService_CashFaultReported;
        }

        private async void PaymentService_CashJamReported(object? sender, CashRecyclerJamEventArgs e)
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                var dialog = new ContentDialog
                {
                    Title = "Cash Machine Warning",
                    Content = $"Banknote mechanism warning: {e.Message}\nPlease check the banknote slot or ask store staff for assistance.",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                try { await dialog.ShowAsync(); } catch { }
            });
        }

        private async void PaymentService_CashFaultReported(object? sender, HardwareFaultEventArgs e)
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                Debug.WriteLine($"[Cash Fault] {e.Device}: {e.Message}");
            });
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            // If navigating to KHQR payment (e.g. after disconnect or split payment), preserve the accepted cash & remaining balance!
            if (!ViewModel.IsFullyPaid && e.SourcePageType != typeof(QRPaymentView))
            {
                App.PaymentServiceInstance.ResetTransaction();
            }
        }

        private bool _isHandlingDisconnect = false;

        private void HardwareStatusManager_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(HardwareStatusManager.IsCashAvailable) && !HardwareStatusManager.Instance.IsCashAvailable)
            {
                DispatcherQueue.TryEnqueue(async () =>
                {
                    if (!ViewModel.IsFullyPaid && !_isHandlingDisconnect)
                    {
                        _isHandlingDisconnect = true;

                        var font = LocalizationService.Instance.CurrentLanguage == "km"
                            ? (FontFamily)Application.Current.Resources["KhmerFont"]
                            : (FontFamily)(Application.Current.Resources["GlobalAppFont"] ?? new FontFamily("Segoe UI"));

                        var baseAccentStyle = (Style)Application.Current.Resources["AccentButtonStyle"];
                        var primaryButtonStyleWithFont = new Style(typeof(Button)) { BasedOn = baseAccentStyle };
                        primaryButtonStyleWithFont.Setters.Add(new Setter(Control.FontFamilyProperty, font));

                        decimal paidUsd = ViewModel.TotalPaidUsd;
                        decimal remainingUsd = ViewModel.TotalDueUsd - paidUsd;

                        string messageText = paidUsd > 0
                            ? $"The cash acceptor connection was interrupted. ${paidUsd:0.00} has been recorded for this order. Please complete the remaining balance of ${remainingUsd:0.00} using KHQR Digital Payment."
                            : "The cash acceptor was disconnected. Please complete your transaction using KHQR Digital Payment.";

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
                                        Glyph = "\uE7BA", // Warning / Plug icon
                                        FontFamily = new FontFamily("Segoe Fluent Icons"),
                                        FontSize = 44,
                                        Foreground = (Brush)Application.Current.Resources["AccentBlueBrush"],
                                        HorizontalAlignment = HorizontalAlignment.Center
                                    },
                                    new TextBlock
                                    {
                                        Text = "Cash Machine Disconnected",
                                        FontSize = 20,
                                        FontWeight = FontWeights.SemiBold,
                                        TextAlignment = TextAlignment.Center,
                                        HorizontalAlignment = HorizontalAlignment.Center,
                                        FontFamily = font
                                    },
                                    new TextBlock
                                    {
                                        Text = messageText,
                                        FontSize = 16,
                                        TextWrapping = TextWrapping.Wrap,
                                        TextAlignment = TextAlignment.Center,
                                        HorizontalAlignment = HorizontalAlignment.Center,
                                        MaxWidth = 450,
                                        FontFamily = font
                                    }
                                }
                            },
                            Style = (Style)Application.Current.Resources["KioskContentDialogStyle"],
                            PrimaryButtonText = "Continue to KHQR Payment",
                            PrimaryButtonStyle = primaryButtonStyleWithFont,
                            XamlRoot = this.Content?.XamlRoot ?? this.XamlRoot,
                            RequestedTheme = ElementTheme.Light
                        };

                        try
                        {
                            await dialog.ShowAsync();
                        }
                        catch { }

                        // Navigate to KHQR payment carrying the remaining balance
                        ViewModel.NavigationService.NavigateTo(
                            typeof(QRPaymentView),
                            null,
                            Microsoft.UI.Xaml.Media.Animation.SlideNavigationTransitionEffect.FromRight
                        );
                    }
                });
            }
        }

        public static Visibility BoolToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _isHandlingDisconnect = false;
            ViewModel.InitializeTransaction();
        }

        private void AttemptRow_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is Border border && border.DataContext is PaymentAttempt attempt)
            {
                Windows.UI.Color bg;
                Windows.UI.Color borderCol;
                Windows.UI.Color accent;
                string glyph;

                switch (attempt.Result)
                {
                    case PaymentAttemptResult.Accepted:
                        bg = Windows.UI.Color.FromArgb(255, 236, 253, 245);       // emerald-50
                        borderCol = Windows.UI.Color.FromArgb(255, 167, 243, 208); // emerald-200
                        accent = Windows.UI.Color.FromArgb(255, 5, 150, 105);     // emerald-600
                        glyph = "\uE73E"; // CheckMark
                        break;
                    case PaymentAttemptResult.Rejected:
                        bg = Windows.UI.Color.FromArgb(255, 254, 242, 242);       // red-50
                        borderCol = Windows.UI.Color.FromArgb(255, 254, 202, 202); // red-200
                        accent = Windows.UI.Color.FromArgb(255, 220, 38, 38);     // red-600
                        glyph = "\uE711"; // ChromeClose
                        break;
                    case PaymentAttemptResult.Warning:
                        bg = Windows.UI.Color.FromArgb(255, 255, 251, 235);       // amber-50
                        borderCol = Windows.UI.Color.FromArgb(255, 253, 230, 138); // amber-200
                        accent = Windows.UI.Color.FromArgb(255, 217, 119, 6);     // amber-600
                        glyph = "\uE7BA"; // Warning
                        break;
                    case PaymentAttemptResult.Error:
                        bg = Windows.UI.Color.FromArgb(255, 254, 242, 242);       // red-50
                        borderCol = Windows.UI.Color.FromArgb(255, 248, 113, 113); // red-400
                        accent = Windows.UI.Color.FromArgb(255, 185, 28, 28);     // red-700
                        glyph = "\uEA39"; // Error
                        break;
                    case PaymentAttemptResult.Info:
                    default:
                        bg = Windows.UI.Color.FromArgb(255, 239, 246, 255);       // blue-50
                        borderCol = Windows.UI.Color.FromArgb(255, 191, 219, 254); // blue-200
                        accent = Windows.UI.Color.FromArgb(255, 37, 99, 235);     // blue-600
                        glyph = "\uE946"; // Info
                        break;
                }

                border.Background = new SolidColorBrush(bg);
                border.BorderBrush = new SolidColorBrush(borderCol);

                if (border.FindName("StatusIcon") is FontIcon statusIcon)
                {
                    statusIcon.Glyph = glyph;
                    statusIcon.Foreground = new SolidColorBrush(accent);
                }

                if (border.FindName("AmountText") is TextBlock amountText)
                {
                    amountText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 41, 59));
                    amountText.Visibility = attempt.HasAmount ? Visibility.Visible : Visibility.Collapsed;
                }

                if (border.FindName("StatusBadge") is TextBlock statusBadge)
                {
                    statusBadge.Foreground = new SolidColorBrush(accent);
                }
            }
        }

        private async void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ViewModel.CanNavigateBack) return;

            var font = LocalizationService.Instance.CurrentLanguage == "km"
                ? (FontFamily)Application.Current.Resources["KhmerFont"]
                : (FontFamily)(Application.Current.Resources["GlobalAppFont"] ?? new FontFamily("Segoe UI"));

            // Primary Button: AccentButtonStyle + font
            var baseAccentStyle = (Style)Application.Current.Resources["AccentButtonStyle"];
            var primaryButtonStyleWithFont = new Style(typeof(Button)) { BasedOn = baseAccentStyle };
            primaryButtonStyleWithFont.Setters.Add(new Setter(Control.FontFamilyProperty, font));

            // Secondary Button: DialogButtonStyle + font
            var baseDialogStyle = (Style)Application.Current.Resources["DialogButtonStyle"];
            var secondaryButtonStyleWithFont = new Style(typeof(Button)) { BasedOn = baseDialogStyle };
            secondaryButtonStyleWithFont.Setters.Add(new Setter(Control.FontFamilyProperty, font));

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
                            Foreground = (Brush)Application.Current.Resources["AccentBlueBrush"],
                            HorizontalAlignment = HorizontalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = Localizer.GetString("ChangePaymentMethodTitle"),
                            FontSize = 20,
                            FontWeight = FontWeights.SemiBold,
                            TextAlignment = TextAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            FontFamily = font
                        },
                        new TextBlock
                        {
                            Text = Localizer.GetString("ChangePaymentMethodMessage"),
                            FontSize = 16,
                            TextWrapping = TextWrapping.Wrap,
                            TextAlignment = TextAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            MaxWidth = 450,
                            FontFamily = font
                        }
                    }
                },
                Style = (Style)Application.Current.Resources["KioskContentDialogStyle"],
                PrimaryButtonText = Localizer.GetString("ContinuePayment"),
                SecondaryButtonText = Localizer.GetString("BackToPaymentSelection"),
                PrimaryButtonStyle = primaryButtonStyleWithFont,
                SecondaryButtonStyle = secondaryButtonStyleWithFont,
                XamlRoot = this.XamlRoot,
                RequestedTheme = ElementTheme.Light
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Secondary)
            {
                App.PaymentServiceInstance.ResetTransaction();
                ViewModel.NavigateBackToPaymentSelection();
            }
        }

        private void ConfirmPaymentButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ConfirmPaymentAndProceed();
        }
    }
}