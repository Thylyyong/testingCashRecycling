using Microsoft.UI.Xaml.Media.Animation;
using SelfCheckoutKiosk.App.Services;
using SelfCheckoutKiosk.App.Views.Customer;

namespace SelfCheckoutKiosk.App.ViewModels.Customer
{
    public class PaymentOptionViewModel
    {
        public INavigationService NavigationService { get; }

        public PaymentOptionViewModel(INavigationService navigationService)
        {
            NavigationService = navigationService;
        }

        public void ProceedToCash()
        {
            NavigationService.NavigateTo(
                typeof(IngestionProgressView),
                null,
                SlideNavigationTransitionEffect.FromRight);
        }

        public void ProceedToKhqr()
        {
            NavigationService.NavigateTo(
                typeof(QRPaymentView),
                null,
                SlideNavigationTransitionEffect.FromRight);
        }

        public void ProceedToKioskBaseView()
        {
            NavigationService.NavigateTo(
                typeof(KioskBaseView),
                null,
                SlideNavigationTransitionEffect.FromLeft);
        }

        public void GoBack()
        {
            NavigationService.NavigateTo(
                typeof(CartView),
                null,
                SlideNavigationTransitionEffect.FromLeft
            );
        }
    }
}