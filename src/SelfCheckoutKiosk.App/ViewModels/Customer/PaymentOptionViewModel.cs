using Microsoft.UI.Xaml.Media.Animation;
using SelfCheckoutKiosk.App.Navigation;
using SelfCheckoutKiosk.App.Services;

namespace SelfCheckoutKiosk.App.ViewModels.Customer
{
    public class PaymentOptionViewModel
    {
        public INavigationService NavigationService { get; }

        public PaymentOptionViewModel(INavigationService navigationService)
        {
            NavigationService = navigationService;
        }

        public void ProceedToKioskBaseView()
        {
            NavigationService.NavigateTo(KioskRoute.Attract, SlideNavigationTransitionEffect.FromLeft);
        }
        
        public void GoBack()
        {
            NavigationService.NavigateTo(KioskRoute.Home, SlideNavigationTransitionEffect.FromLeft);
        }
    }
}