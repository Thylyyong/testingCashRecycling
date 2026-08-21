using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SelfCheckoutKiosk.App.Services;
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

            ViewModel.GoBack();
        }

        private void AdminButton_Click(object sender, RoutedEventArgs e)
        {
            ResetInactivityTimer();
            App.MainWindowInstance?.NavigationService.NavigateTo(
                typeof(Views.Admin.AdminLoginView),
                null,
                Microsoft.UI.Xaml.Media.Animation.SlideNavigationTransitionEffect.FromBottom
            );
        }
    }
}