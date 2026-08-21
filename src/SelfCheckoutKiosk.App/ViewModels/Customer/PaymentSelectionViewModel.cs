using Microsoft.UI.Xaml.Media.Animation;
using SelfCheckoutKiosk.App.Navigation;
using SelfCheckoutKiosk.App.Services;
using System;
using System.ComponentModel;

namespace SelfCheckoutKiosk.App.ViewModels.Customer
{
    public class PaymentSelectionViewModel : INotifyPropertyChanged
    {
        public INavigationService NavigationService { get; }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public event EventHandler<string>? PaymentMethodSelected;
        public event EventHandler? Cancelled;

        public PaymentSelectionViewModel(INavigationService navigationService)
        {
            NavigationService = navigationService;
        }

        public void SelectPaymentMethod(string methodKey)
        {
            PaymentMethodSelected?.Invoke(this, methodKey);

            if (methodKey == "Cash") {
                ProceedToIngestionProgress();
            } else if (methodKey == "KHQR")
            {
                ProceedToQRPayment();
            }
        }

        public void Cancel()
        {
            Cancelled?.Invoke(this, EventArgs.Empty);
        }

        public void ProceedToCart()
        {
            NavigationService.NavigateTo(KioskRoute.Cart, SlideNavigationTransitionEffect.FromLeft);
        }

        public void ProceedToIngestionProgress()
        {
            NavigationService.NavigateTo(KioskRoute.CashIngestion, SlideNavigationTransitionEffect.FromRight);
        }

        public void ProceedToQRPayment()
        {
            NavigationService.NavigateTo(KioskRoute.QRPayment, SlideNavigationTransitionEffect.FromRight);
        }
    }
}