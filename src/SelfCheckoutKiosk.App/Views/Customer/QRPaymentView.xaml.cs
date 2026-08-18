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
            var globalFont = (FontFamily)Application.Current.Resources["GlobalAppFont"];

            var baseAccentStyle = (Style)Application.Current.Resources["AccentButtonStyle"];
            var primaryButtonStyleWithFont = new Style(typeof(Button)) { BasedOn = baseAccentStyle };
            primaryButtonStyleWithFont.Setters.Add(new Setter(Control.FontFamilyProperty, globalFont));

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
                            Glyph = "\uE814", // Warning Icon
                            FontFamily = new FontFamily("Segoe Fluent Icons"),
                            FontSize = 42,
                            Foreground = (Brush)Application.Current.Resources["DangerBrush"],
                            HorizontalAlignment = HorizontalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = Localizer.GetString("CancelPaymentTitle") ?? "Cancel QR Payment?",
                            FontSize = 20,
                            FontWeight = FontWeights.SemiBold,
                            TextAlignment = TextAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            FontFamily = globalFont
                        },
                        new TextBlock
                        {
                            Text = Localizer.GetString("CancelPaymentMessage") ?? "Are you sure you want to cancel QR payment and return to payment selection?",
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
                PrimaryButtonText = Localizer.GetString("ContinuePayment") ?? "Continue Payment",
                SecondaryButtonText = Localizer.GetString("CancelPayment") ?? "Cancel Payment",
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
    }
}