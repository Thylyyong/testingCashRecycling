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
    /// VIEW-ONLY PAGE: Showcase of accepted payment methods and live hardware availability.
    /// Reached from HomeView ("Payment Options" button).
    /// Used purely for customers to see what payment methods the kiosk supports (Cash, KHQR, Cards)
    /// and their live online/offline state. Does NOT perform order checkout.
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
            SizeChanged += Page_SizeChanged;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            UpdateAvailability();

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

        private void PaymentOptionView_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateAvailability();
            HardwareStatusManager.Instance.PropertyChanged += HardwareStatusManager_PropertyChanged;
            StartInactivityTimer();
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
                BackButton.Height = 64;
                BackButtonText.FontSize = 20;
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
                    BackButton.Height = 88;
                    BackButtonText.FontSize = 26;
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
                    BackButton.Height = 64;
                    BackButtonText.FontSize = 20;
                }
            }
        }

        private void PaymentOptionView_Unloaded(object sender, RoutedEventArgs e)
        {
            HardwareStatusManager.Instance.PropertyChanged -= HardwareStatusManager_PropertyChanged;
            StopInactivityTimer();
        }

        private void HardwareStatusManager_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            DispatcherQueue.TryEnqueue(UpdateAvailability);
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
            AppSound.ButtonClick();
            ViewModel.GoBack();
        }

        private async void AdminButton_Click(object sender, RoutedEventArgs e)
        {
            ResetInactivityTimer();
            AppSound.ButtonClick();
            await AdminLoginDialog.ShowAsync(this.XamlRoot);
        }
    }
}