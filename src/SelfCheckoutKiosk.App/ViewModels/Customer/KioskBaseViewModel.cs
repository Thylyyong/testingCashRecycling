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

        public BannerMedia? CurrentMedia => BannerMediaPaths.Count > 0 && CurrentBannerIndex < BannerMediaPaths.Count
            ? BannerMediaPaths[CurrentBannerIndex]
            : null;

        public KioskBaseViewModel(INavigationService navigationService)
        {
            NavigationService = navigationService;

            LoadMediaPlaylist();
            MediaBrandingService.Instance.PlaylistChanged += (s, e) =>
            {
                App.MainWindowInstance?.DispatcherQueue.TryEnqueue(LoadMediaPlaylist);
            };
        }

        public void LoadMediaPlaylist()
        {
            BannerMediaPaths.Clear();
            var playlist = MediaBrandingService.Instance.GetActiveBannerPlaylist();
            foreach (var item in playlist)
            {
                BannerMediaPaths.Add(item);
            }

            if (CurrentBannerIndex >= BannerMediaPaths.Count)
            {
                CurrentBannerIndex = 0;
            }

            if (BannerMediaPaths.Count > 0)
            {
                BannerMediaPaths[CurrentBannerIndex].IsCurrent = true;
            }
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

        public void PreviousBanner()
        {
            if (BannerMediaPaths.Count == 0)
                return;

            CurrentBannerIndex = (CurrentBannerIndex - 1 + BannerMediaPaths.Count) % BannerMediaPaths.Count;

            for (int i = 0; i < BannerMediaPaths.Count; i++)
            {
                BannerMediaPaths[i].IsCurrent = (i == CurrentBannerIndex);
            }
        }

        public void ProceedToHome()
        {
            NavigationService.NavigateTo(
                typeof(HomeView),
                null,
                SlideNavigationTransitionEffect.FromRight
            );
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