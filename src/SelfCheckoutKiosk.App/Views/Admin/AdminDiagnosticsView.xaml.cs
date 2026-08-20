using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SelfCheckoutKiosk.App.ViewModels.Admin;

namespace SelfCheckoutKiosk.App.Views.Admin
{
    public sealed partial class AdminDiagnosticsView : Page
    {
        public AdminDiagnosticsViewModel ViewModel { get; }

        public AdminDiagnosticsView()
        {
            InitializeComponent();
            ViewModel = new AdminDiagnosticsViewModel();
        }

        private void MediaBrandingButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.NavigateToMediaBranding();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ExitToCustomerMode();
        }

        private void ApproveAgeButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ApproveAgeRestrictedItem();
        }

        private void RejectAgeButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.RejectAgeRestrictedItem();
        }

        private async void ResetVaultButton_Click(object sender, RoutedEventArgs e)
        {
            var dangerStyle = new Style(typeof(Button));
            dangerStyle.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Windows.UI.Color.FromArgb(255, 220, 38, 38))));
            dangerStyle.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255))));
            dangerStyle.Setters.Add(new Setter(Control.FontWeightProperty, Microsoft.UI.Text.FontWeights.Bold));
            dangerStyle.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(8)));
            dangerStyle.Setters.Add(new Setter(FrameworkElement.HeightProperty, 38.0));
            dangerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(18, 0, 18, 0)));

            var dialog = new ContentDialog
            {
                Title = "Reset Cash Vault Counts",
                Content = new TextBlock
                {
                    Text = "Are you sure you want to reset the physical cash vault breakdown counts? All recorded banknote inventory will be cleared to zero for cash collection and reconciliation.",
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 71, 85, 105))
                },
                PrimaryButtonText = "Reset Counts",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                Style = (Style)Application.Current.Resources["KioskContentDialogStyle"],
                PrimaryButtonStyle = dangerStyle,
                CloseButtonStyle = (Style)Application.Current.Resources["DialogButtonStyle"],
                XamlRoot = this.XamlRoot,
                RequestedTheme = ElementTheme.Light
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                ViewModel.ResetVaultCounts();
            }
        }

        private async void ReprintReceiptButton_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.ReprintLastReceiptAsync();
        }

        private async void SyncCloudButton_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.SyncCloudAsync();
        }
    }
}
