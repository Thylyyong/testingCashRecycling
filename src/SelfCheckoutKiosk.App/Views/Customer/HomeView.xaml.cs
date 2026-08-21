using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SelfCheckoutKiosk.App.Navigation;
using SelfCheckoutKiosk.App.Services;
using SelfCheckoutKiosk.App.ViewModels.Customer;
using System;
using System.ComponentModel;

namespace SelfCheckoutKiosk.App.Views.Customer
{
    public sealed partial class HomeView : Page
    {
        public HomeViewModel ViewModel { get; }

        public LocalizationService Localizer => LocalizationService.Instance;
        private readonly DispatcherTimer _inactivityTimer;
        private ContentDialog? _activeDialog;

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
                Interval = TimeSpan.FromSeconds(20)
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

        private async void HomeView_Loaded(object sender, RoutedEventArgs e)
        {
            // Sync font, flag, and dropdown label with the current language loaded at startup
            SyncLanguageUI(LocalizationService.Instance.CurrentLanguage);

            //await LocalizationService.Instance.SetLanguageAsync(LocalizationService.Instance.CurrentLanguage);
            LocalizationService.Instance.PropertyChanged += Localizer_PropertyChanged;

            StartInactivityTimer();
        }

        private void HomeView_Unloaded(object sender, RoutedEventArgs e)
        {
            LocalizationService.Instance.PropertyChanged -= Localizer_PropertyChanged;
            StopInactivityTimer();
        }

        private void Localizer_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Keep the font/flag/label in the dropdown button synced whenever language changes,
            // regardless of which page or control triggered the change.
            SyncLanguageUI(LocalizationService.Instance.CurrentLanguage);
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

            // Dismiss any open dialog before returning to base view
            _activeDialog?.Hide();
            _activeDialog = null;

            ViewModel.ProceedToKioskBaseView();
        }

        private async void ScanItemCard_Click(object sender, RoutedEventArgs e)
        {
            ResetInactivityTimer();

            if (!HardwareStatusManager.Instance.IsCashAvailable && !HardwareStatusManager.Instance.IsQrAvailable)
            {
                await ShowNoPaymentAvailableDialogAsync();
                return;
            }

            ViewModel.ProceedToCart();
        }

        private async Task ShowNoPaymentAvailableDialogAsync()
        {
            var font = LocalizationService.Instance.CurrentLanguage == "km"
                ? (FontFamily)Application.Current.Resources["KhmerFont"]
                : (FontFamily)(Application.Current.Resources["GlobalAppFont"] ?? new FontFamily("Segoe UI"));

            var baseDialogStyle = (Style)Application.Current.Resources["DialogButtonStyle"];
            var closeButtonStyleWithFont = new Style(typeof(Button)) { BasedOn = baseDialogStyle };
            closeButtonStyleWithFont.Setters.Add(new Setter(Control.FontFamilyProperty, font));

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
                            Glyph = "\uE783",
                            FontFamily = new FontFamily("Segoe Fluent Icons"),
                            FontSize = 48,
                            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 239, 68, 68)),
                            HorizontalAlignment = HorizontalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = Localizer.GetString("ServiceUnavailableTitle"),
                            FontSize = 22,
                            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                            TextAlignment = TextAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            FontFamily = font
                        },
                        new TextBlock
                        {
                            Text = Localizer.GetString("ServiceUnavailableMessage"),
                            FontSize = 16,
                            TextWrapping = TextWrapping.Wrap,
                            TextAlignment = TextAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            FontFamily = font
                        }
                    }
                },
                Style = (Style)Application.Current.Resources["KioskContentDialogStyle"],
                CloseButtonText = Localizer.GetString("OK"),
                CloseButtonStyle = closeButtonStyleWithFont,
                XamlRoot = this.XamlRoot,
                RequestedTheme = ElementTheme.Light
            };

            _activeDialog = dialog;

            try
            {
                await dialog.ShowAsync();
            }
            finally
            {
                _activeDialog = null;
            }
        }

        // =========================================================================
        // PaymentOptionView is for VIEWING ACCEPTED PAYMENT METHODS & LIVE STATUS ONLY.
        // It does NOT start checkout or redirect; it allows customers to view what's online.
        // =========================================================================
        private void PaymentOptionsCard_Click(object sender, RoutedEventArgs e)
        {
            ResetInactivityTimer();
            ViewModel.ProceedToPaymentOptions();
        }

        private async void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            ResetInactivityTimer();

            var font = LocalizationService.Instance.CurrentLanguage == "km"
                ? (FontFamily)Application.Current.Resources["KhmerFont"]
                : (FontFamily)(Application.Current.Resources["GlobalAppFont"] ?? new FontFamily("Segoe UI"));

            // Close Button: DialogButtonStyle + font
            var baseDialogStyle = (Style)Application.Current.Resources["DialogButtonStyle"];
            var closeButtonStyleWithFont = new Style(typeof(Button)) { BasedOn = baseDialogStyle };
            closeButtonStyleWithFont.Setters.Add(new Setter(Control.FontFamilyProperty, font));

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
                            FontWeight = FontWeights.SemiBold,
                            TextAlignment = TextAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            FontFamily = font
                        },
                        new TextBlock
                        {
                            Text = Localizer.GetString("HelpMessage"),
                            FontSize = 16,
                            TextWrapping = TextWrapping.Wrap,
                            TextAlignment = TextAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            FontFamily = font
                        }
                    }
                },
                Style = (Style)Application.Current.Resources["KioskContentDialogStyle"],
                CloseButtonText = Localizer.GetString("OK"),
                CloseButtonStyle = closeButtonStyleWithFont,
                XamlRoot = this.XamlRoot,
                RequestedTheme = ElementTheme.Light
            };

            _activeDialog = dialog;

            try
            {
                await dialog.ShowAsync();
            }
            finally
            {
                _activeDialog = null;
            }
        }

        private async void LanguageItem_Click(object sender, RoutedEventArgs e)
        {
            ResetInactivityTimer();

            if (sender is MenuFlyoutItem menuItem)
            {
                string selectedLang = menuItem.Text == "ភាសាខ្មែរ" ? "km" : "en";

                // Updates the dictionary and raises PropertyChanged(string.Empty),
                // which refreshes every bound TextBlock across every open page automatically.
                await LocalizationService.Instance.SetLanguageAsync(selectedLang);

                // Font/flag on the dropdown itself isn't dictionary-driven, so sync it explicitly.
                SyncLanguageUI(selectedLang);
            }
        }

        // Centralized method to update font, flag, and dropdown label on load or switch.
        private void SyncLanguageUI(string langCode)
        {
            var font = langCode == "km"
                ? (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["KhmerFont"]
                : new Microsoft.UI.Xaml.Media.FontFamily("Segoe UI");

            if (langCode == "km")
            {
                CurrentLanguageText.Text = "ភាសាខ្មែរ";
                CurrentLanguageText.FontFamily = font;

                CurrentLanguageFlag.Source = new BitmapImage(
                    new Uri("ms-appx:///Assets/Images/Flag/km-flag.png"));
            }
            else
            {
                CurrentLanguageText.Text = "English";
                CurrentLanguageText.FontFamily = font;

                CurrentLanguageFlag.Source = new BitmapImage(
                    new Uri("ms-appx:///Assets/Images/Flag/en-flag.png"));
            }

            if (StoreHoursText != null) StoreHoursText.FontFamily = font;
            if (HelpButtonText != null) HelpButtonText.FontFamily = font;
            if (WelcomeTitleText != null) WelcomeTitleText.FontFamily = font;
            if (WelcomeSubheadingText != null) WelcomeSubheadingText.FontFamily = font;
            if (ScanTitleText != null) ScanTitleText.FontFamily = font;
            if (ScanSubtitleText != null) ScanSubtitleText.FontFamily = font;
            if (PaymentTitleText != null) PaymentTitleText.FontFamily = font;
            if (PaymentSubtitleText != null) PaymentSubtitleText.FontFamily = font;
        }

        private void ScanIcon_Loaded(object sender, RoutedEventArgs e)
        {
            ScanIconAnimation.Begin();
        }

        private void AdminButton_Click(object sender, RoutedEventArgs e)
        {
            ResetInactivityTimer();
            AppRouter.ToAdmin();
        }

        private void PaymentIcon_Loaded(object sender, RoutedEventArgs e)
        {
            PaymentIconAnimation.Begin();
        }
    }
}