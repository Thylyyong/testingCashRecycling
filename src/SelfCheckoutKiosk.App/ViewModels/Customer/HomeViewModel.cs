using Microsoft.UI.Xaml.Media.Animation;
using SelfCheckoutKiosk.App.Services;
using SelfCheckoutKiosk.App.Views.Customer;

namespace SelfCheckoutKiosk.App.ViewModels.Customer
{
    public class HomeViewModel
    {
        public INavigationService NavigationService { get; }

        public string StoreHoursText { get; set; } = "Open until 9:00 PM";

        public HomeViewModel(INavigationService navigationService)
        {
            NavigationService = navigationService;
        }

        public void ProceedToCart()
        {
            NavigationService.NavigateTo(
                typeof(CartView),
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

        public void ProceedToPaymentOptions()
        {
            NavigationService.NavigateTo(
                typeof(PaymentOptionView),
                null,
                SlideNavigationTransitionEffect.FromRight);
        }

        public void RequestHelp()
        {
            // TODO: Call attendant / show help overlay
        }
    }
}