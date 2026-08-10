using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SelfCheckoutKiosk.App.Models;
using SelfCheckoutKiosk.App.Services;
using SelfCheckoutKiosk.App.ViewModels.Customer;
using System.Diagnostics;

namespace SelfCheckoutKiosk.App.Views.Customer
{
    public sealed partial class SuccessView : Page
    {
        public SuccessViewModel? ViewModel { get; private set; }

        public SuccessView()
        {
            InitializeComponent();

            DoneButton.Click += DoneButton_Click;
            //ViewReceiptButton.Click += ViewReceiptButton_Click;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is not Payment payment)
            {
                Debug.WriteLine("[WARN] SuccessView reached without a Payment parameter. Returning home.");

                INavigationService fallbackNav = App.MainWindowInstance?.NavigationService
                    ?? new NavigationService(Frame);
                fallbackNav.NavigateTo(typeof(HomeView), null);
                return;
            }

            INavigationService navigationService = App.MainWindowInstance?.NavigationService
                ?? new NavigationService(Frame);

            ViewModel = new SuccessViewModel(
                navigationService,
                App.ReceiptPrinterServiceInstance,
                payment
            );

            RenderReceipt();

            // Fire-and-forget stub print — matches "print automatically on success" behavior.
            // Swap this for a button-triggered call instead if you want the user to control it.
            ViewModel.PrintReceipt();
        }

        private void RenderReceipt()
        {
            if (ViewModel == null) return;

            TransactionIdText.Text = $"Transaction #{ViewModel.TransactionId}";
            CompletedAtText.Text = ViewModel.CompletedAtText;
            MethodLabelText.Text = ViewModel.MethodLabel;

            CashDetailsSection.Visibility = ViewModel.IsCash ? Visibility.Visible : Visibility.Collapsed;

            if (ViewModel.IsCash)
            {
                TotalDueText.Text = ViewModel.FormattedTotalDue;
                TotalPaidText.Text = ViewModel.FormattedTotalPaid;

                //ChangeDueRow.Visibility = ViewModel.HasChangeDue ? Visibility.Visible : Visibility.Collapsed;
                //ChangeDueText.Text = ViewModel.FormattedChangeDue;

                //AttemptsSummaryText.Text =
                //    $"{ViewModel.AcceptedAttemptCount} accepted, {ViewModel.RejectedAttemptCount} rejected";
            }
        }

        private async void ViewReceiptButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;

            var dialog = new ContentDialog
            {
                Title = "Receipt",
                Content = new ScrollViewer
                {
                    Content = new TextBlock
                    {
                        Text = ViewModel.BuildVirtualReceiptText(),
                        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                        FontSize = 13,
                        TextWrapping = TextWrapping.Wrap
                    },
                    MaxHeight = 400
                },
                CloseButtonText = "Close",
                XamlRoot = this.Content?.XamlRoot ?? this.XamlRoot
            };

            await dialog.ShowAsync();
        }

        private void DoneButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel?.ReturnHome();
        }
    }
}