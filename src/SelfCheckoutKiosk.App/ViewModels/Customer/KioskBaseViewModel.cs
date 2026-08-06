using Microsoft.UI.Xaml.Media.Animation;
using SelfCheckoutKiosk.App.Services;
using SelfCheckoutKiosk.App.Views.Customer;
using SelfCheckoutKiosk.App.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SelfCheckoutKiosk.App.ViewModels.Customer
{
    public class KioskBaseViewModel
    {
        public INavigationService NavigationService { get; }

        public ObservableCollection<BannerMedia> BannerMediaPaths { get; } = new();

        public string WelcomeMessage { get; set; } = "Welcome! Tap anywhere or press Start";

        private int _currentBannerIndex;
        public int CurrentBannerIndex
        {
            get => _currentBannerIndex;
            set => _currentBannerIndex = value;
        }

        public KioskBaseViewModel(INavigationService navigationService)
        {
            NavigationService = navigationService;

            LoadLocalAssets();

            if (BannerMediaPaths.Count > 0)
            {
                BannerMediaPaths[0].IsCurrent = true;
            }
        }

        private void LoadLocalAssets()
        {
            BannerMediaPaths.Add(new BannerMedia
            {
                Path = "ms-appx:///Assets/Images/banner1.jpg"
            });

            BannerMediaPaths.Add(new BannerMedia
            {
                Path = "ms-appx:///Assets/Images/banner2.jpg"
            });

            BannerMediaPaths.Add(new BannerMedia
            {
                Path = "ms-appx:///Assets/Images/banner3.jpg"
            });

            BannerMediaPaths.Add(new BannerMedia
            {
                Path = "ms-appx:///Assets/Images/banner4.jpg"
            });

            BannerMediaPaths.Add(new BannerMedia
            {
                Path = "ms-appx:///Assets/Images/banner5.jpg"
            });

            BannerMediaPaths.Add(new BannerMedia
            {
                Path = "ms-appx:///Assets/Images/banner6.jpg"
            });

            BannerMediaPaths.Add(new BannerMedia
            {
                Path = "ms-appx:///Assets/Images/banner7.jpg"
            });

            BannerMediaPaths.Add(new BannerMedia
            {
                Path = "ms-appx:///Assets/Images/banner8.jpg"
            });
        }

        public void NextBanner()
        {
            if (BannerMediaPaths.Count == 0)
                return;

            CurrentBannerIndex = (CurrentBannerIndex + 1) % BannerMediaPaths.Count;

            for (int i = 0; i < BannerMediaPaths.Count; i++)
            {
                BannerMediaPaths[i].IsCurrent = (i == CurrentBannerIndex);
            }
        }

        public void ProceedToCart()
        {
            NavigationService.NavigateTo(
                typeof(CartView),
                null,
                SlideNavigationTransitionEffect.FromRight
            );
        }
    }
}