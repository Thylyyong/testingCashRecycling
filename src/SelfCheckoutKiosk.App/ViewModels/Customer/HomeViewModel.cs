using Microsoft.UI.Xaml.Media.Animation;
using SelfCheckoutKiosk.App.Models;
using SelfCheckoutKiosk.App.Services;
using SelfCheckoutKiosk.App.Views.Customer;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SelfCheckoutKiosk.App.ViewModels.Customer
{
    public class HomeViewModel : INotifyPropertyChanged
    {
        public INavigationService NavigationService { get; }

        public BrandingConfig Branding => MediaBrandingService.Instance.Branding;

        public string CompanyName => Branding.CompanyName;
        public string Tagline => Branding.Tagline;
        public string LogoUri => Branding.LogoUri;
        public string StoreHours
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Branding.StoreHours) && Branding.StoreHours != "Open until 10:00 PM" && Branding.StoreHours != "Open until 9:00 PM")
                {
                    return Branding.StoreHours;
                }
                return LocalizationService.Instance.GetString("OpenUntil");
            }
        }

        public HomeViewModel(INavigationService navigationService)
        {
            NavigationService = navigationService;
            MediaBrandingService.Instance.BrandingChanged += MediaBrandingService_BrandingChanged;
        }

        private void MediaBrandingService_BrandingChanged(object? sender, EventArgs e)
        {
            OnPropertyChanged(nameof(Branding));
            OnPropertyChanged(nameof(CompanyName));
            OnPropertyChanged(nameof(Tagline));
            OnPropertyChanged(nameof(LogoUri));
            OnPropertyChanged(nameof(StoreHours));
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
            // Handled via ShowStaffAssistanceAlertAsync / Help dialog
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}