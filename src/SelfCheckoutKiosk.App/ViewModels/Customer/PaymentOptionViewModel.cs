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
                typeof(HomeView),
                null,
                SlideNavigationTransitionEffect.FromLeft
            );
        }
    }
}