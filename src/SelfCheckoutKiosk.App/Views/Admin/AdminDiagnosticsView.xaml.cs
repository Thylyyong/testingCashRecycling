using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
