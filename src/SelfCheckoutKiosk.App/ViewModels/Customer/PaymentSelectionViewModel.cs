using Microsoft.UI.Xaml.Media.Animation;
using SelfCheckoutKiosk.App.Services;
using SelfCheckoutKiosk.App.Views.Customer;
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
        }

        public void Cancel()
        {
            Cancelled?.Invoke(this, EventArgs.Empty);
        }

        public void ProceedToCart()
        {
            NavigationService.NavigateTo(
                typeof(CartView),
                null,
                SlideNavigationTransitionEffect.FromLeft
            );
        }

        public void ProceedToHome()
        {
            NavigationService.NavigateTo(
                typeof(HomeView),
                null,
                SlideNavigationTransitionEffect.FromLeft
            );
        }
    }
}