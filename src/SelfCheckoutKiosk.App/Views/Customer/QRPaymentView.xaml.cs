using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SelfCheckoutKiosk.App.Services;
using SelfCheckoutKiosk.App.ViewModels.Customer;
using System;

namespace SelfCheckoutKiosk.App.Views.Customer
{
    public partial class QRPaymentView : Page
    {
        public QRPaymentViewModel ViewModel { get; }
        public LocalizationService Localizer => LocalizationService.Instance;

        public QRPaymentView()
        {
            InitializeComponent();

            INavigationService navigationService = App.MainWindowInstance?.NavigationService
                ?? new NavigationService(Frame);

            ViewModel = new QRPaymentViewModel(
                navigationService,
                App.CartServiceInstance,
                App.PaymentServiceInstance
            );

            BackButton.Click += BackButton_Click;
            SimulateCancelButton.Click += SimulateCancelButton_Click;
            SimulateSuccessButton.Click += SimulateSuccessButton_Click;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            ViewModel.InitializeTransaction();
        }

        private async void BackButton_Click(object sender, RoutedEventArgs e)
        {
            var font = LocalizationService.Instance.CurrentLanguage == "km"
                ? (FontFamily)Application.Current.Resources["KhmerFont"]
                : (FontFamily)(Application.Current.Resources["GlobalAppFont"] ?? new FontFamily("Segoe UI"));

            var baseAccentStyle = (Style)Application.Current.Resources["AccentButtonStyle"];
            var primaryButtonStyleWithFont = new Style(typeof(Button)) { BasedOn = baseAccentStyle };
            primaryButtonStyleWithFont.Setters.Add(new Setter(Control.FontFamilyProperty, font));

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
                            Glyph = "\uE814", // Warning / Question icon
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
                ViewModel.NavigateBackToPaymentSelection();
            }
        }

        private void SimulateCancelButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.NavigateBackToPaymentSelection();
        }

        private void SimulateSuccessButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.SimulatePaymentSuccessAndProceed();
        }

        private async void HelpButton_Click(object sender, RoutedEventArgs e)
        {
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
                            FontWeight = FontWeights.SemiBold,
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

        private void AdminButton_Click(object sender, RoutedEventArgs e)
        {
            App.MainWindowInstance?.NavigationService.NavigateTo(
                typeof(Views.Admin.AdminLoginView),
                null,
                Microsoft.UI.Xaml.Media.Animation.SlideNavigationTransitionEffect.FromBottom
            );
        }
    }
}