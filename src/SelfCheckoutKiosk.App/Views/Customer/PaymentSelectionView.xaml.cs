using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SelfCheckoutKiosk.App.Navigation;
using SelfCheckoutKiosk.App.Services;
using SelfCheckoutKiosk.App.Services.Audio;
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
            SizeChanged += Page_SizeChanged;
        }

        private void PaymentSelectionView_Loaded(object sender, RoutedEventArgs e)
        {
            AppSound.ChoosePaymentMethod();
            UpdateAvailability();
            HardwareStatusManager.Instance.PropertyChanged += HardwareStatusManager_PropertyChanged;
            ApplyCardLayout(this.ActualWidth, this.ActualHeight);

            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                ApplyCardLayout(this.ActualWidth, this.ActualHeight);
                this.InvalidateMeasure();
                this.InvalidateArrange();
                this.UpdateLayout();
            });
        }

        private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyCardLayout(e.NewSize.Width, e.NewSize.Height);
        }

        private void ApplyCardLayout(double width, double height)
        {
            if (CardsGrid == null || CashViewbox == null) return;

            // Only switch to 3 cards per row if the app window is actually wide landscape (Width > Height and Width >= 1200)
            bool isWideLandscape = (width > height && width >= 1200);

            if (isWideLandscape)
            {
                // 3 Columns x 2 Rows for Big Horizontal Widescreen (3 cards per row)
                CardsGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                CardsGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
                CardsGrid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);

                CardsGrid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
                CardsGrid.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
                CardsGrid.RowDefinitions[2].Height = new GridLength(0);

                Grid.SetRow(CashViewbox, 0); Grid.SetColumn(CashViewbox, 0);
                Grid.SetRow(KhqrViewbox, 0); Grid.SetColumn(KhqrViewbox, 1);
                Grid.SetRow(CardViewbox, 0); Grid.SetColumn(CardViewbox, 2);

                Grid.SetRow(IntlQrViewbox, 1); Grid.SetColumn(IntlQrViewbox, 0);
                Grid.SetRow(MembershipViewbox, 1); Grid.SetColumn(MembershipViewbox, 1);
                Grid.SetRow(CouponViewbox, 1); Grid.SetColumn(CouponViewbox, 2);

                CardsGrid.Padding = new Thickness(48, 16, 48, 16);
                CardsGrid.RowSpacing = 16;
                CardsGrid.ColumnSpacing = 16;
                HeaderContainer.Margin = new Thickness(48, 16, 48, 12);
                FooterContainer.Padding = new Thickness(48, 0, 48, 20);
                HeaderTitle.FontSize = 32;
                HeaderSubTitle.FontSize = 14;
                CancelButton.Height = 64;
                CancelButtonText.FontSize = 20;
                HelpButton.Padding = new Thickness(18, 10, 18, 10);
            }
            else
            {
                // 2 Columns x 3 Rows (2 cards per row) for ALL Vertical / Portrait Kiosk Displays
                CardsGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                CardsGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
                CardsGrid.ColumnDefinitions[2].Width = new GridLength(0);

                CardsGrid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
                CardsGrid.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
                CardsGrid.RowDefinitions[2].Height = new GridLength(1, GridUnitType.Star);

                Grid.SetRow(CashViewbox, 0); Grid.SetColumn(CashViewbox, 0);
                Grid.SetRow(KhqrViewbox, 0); Grid.SetColumn(KhqrViewbox, 1);

                Grid.SetRow(CardViewbox, 1); Grid.SetColumn(CardViewbox, 0);
                Grid.SetRow(IntlQrViewbox, 1); Grid.SetColumn(IntlQrViewbox, 1);

                Grid.SetRow(MembershipViewbox, 2); Grid.SetColumn(MembershipViewbox, 0);
                Grid.SetRow(CouponViewbox, 2); Grid.SetColumn(CouponViewbox, 1);

                if (height >= 1200)
                {
                    // Large 27.5" Vertical Kiosk (1080x1920)
                    CardsGrid.Padding = new Thickness(64, 24, 64, 24);
                    CardsGrid.RowSpacing = 20;
                    CardsGrid.ColumnSpacing = 20;
                    HeaderContainer.Margin = new Thickness(64, 36, 64, 24);
                    FooterContainer.Padding = new Thickness(64, 0, 64, 36);
                    HeaderTitle.FontSize = 48;
                    HeaderSubTitle.FontSize = 20;
                    CancelButton.Height = 88;
                    CancelButtonText.FontSize = 26;
                    HelpButton.Padding = new Thickness(24, 12, 24, 12);
                }
                else
                {
                    // Standard Testing Vertical Window (< 1200px)
                    CardsGrid.Padding = new Thickness(24, 12, 24, 12);
                    CardsGrid.RowSpacing = 12;
                    CardsGrid.ColumnSpacing = 12;
                    HeaderContainer.Margin = new Thickness(24, 16, 24, 12);
                    FooterContainer.Padding = new Thickness(24, 0, 24, 16);
                    HeaderTitle.FontSize = 32;
                    HeaderSubTitle.FontSize = 13;
                    CancelButton.Height = 64;
                    CancelButtonText.FontSize = 20;
                    HelpButton.Padding = new Thickness(16, 8, 16, 8);
                }
            }
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
            AppSound.ButtonClick();
            ViewModel.SelectPaymentMethod("Cash");
        }

        private void KhqrCard_Click(object sender, RoutedEventArgs e)
        {
            if (!HardwareStatusManager.Instance.IsQrAvailable) return;
            AppSound.ButtonClick();
            ViewModel.SelectPaymentMethod("KHQR");
        }

        private void CardCard_Click(object sender, RoutedEventArgs e)
        {
            if (!HardwareStatusManager.Instance.IsCardAvailable) return;
            AppSound.ButtonClick();
            ViewModel.SelectPaymentMethod("Card");
        }

        private void IntlQrCard_Click(object sender, RoutedEventArgs e)
        {
            if (!HardwareStatusManager.Instance.IsIntlQrAvailable) return;
            AppSound.ButtonClick();
            ViewModel.SelectPaymentMethod("InternationalQR");
        }

        private void MembershipCard_Click(object sender, RoutedEventArgs e)
        {
            if (!HardwareStatusManager.Instance.IsMembershipAvailable) return;
            AppSound.ButtonClick();
            ViewModel.SelectPaymentMethod("MembershipCard");
        }

        private void CouponCard_Click(object sender, RoutedEventArgs e)
        {
            if (!HardwareStatusManager.Instance.IsCouponAvailable) return;
            AppSound.ButtonClick();
            ViewModel.SelectPaymentMethod("Coupon");
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            AppSound.ButtonClick();
            ViewModel.Cancel();
            ViewModel.ProceedToCart();
        }

        private async void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            AppSound.ButtonClick();
            var globalFont = (FontFamily)(Application.Current.Resources["GlobalAppFont"] ?? new FontFamily("Segoe UI"));

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
                            Glyph = "\uE946",
                            FontFamily = new FontFamily("Segoe Fluent Icons"),
                            FontSize = 42,
                            Foreground = (Brush)Application.Current.Resources["AccentBlueBrush"],
                            HorizontalAlignment = HorizontalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = Localizer.GetString("HelpIsOnTheWay"),
                            FontSize = 20,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            TextAlignment = TextAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            FontFamily = globalFont
                        },
                        new TextBlock
                        {
                            Text = Localizer.GetString("HelpMessage"),
                            FontSize = 16,
                            TextWrapping = TextWrapping.Wrap,
                            TextAlignment = TextAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            FontFamily = globalFont
                        }
                    }
                },
                Style = (Style)Application.Current.Resources["KioskContentDialogStyle"],
                CloseButtonText = Localizer.GetString("OK"),
                CloseButtonStyle = (Style)Application.Current.Resources["DialogButtonStyle"],
                XamlRoot = this.XamlRoot,
                RequestedTheme = ElementTheme.Light
            };

            await dialog.ShowAsync();
        }

        private async void AdminButton_Click(object sender, RoutedEventArgs e)
        {
            AppSound.ButtonClick();
            await AdminLoginDialog.ShowAsync(this.XamlRoot);
        }
    }
}