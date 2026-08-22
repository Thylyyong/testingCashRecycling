using Microsoft.UI.Xaml.Media.Animation;
using SelfCheckoutKiosk.App.Models;
using SelfCheckoutKiosk.App.Navigation;
using SelfCheckoutKiosk.App.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace SelfCheckoutKiosk.App.ViewModels.Admin
{
    public class MediaBrandingViewModel : INotifyPropertyChanged
    {
        private readonly INavigationService _navigationService;

        private string _statusMessage = string.Empty;
        private bool _hasStatusMessage = false;

        public ObservableCollection<AdminMediaItem> MediaItems => MediaBrandingService.Instance.MediaItems;

        public BrandingConfig Branding => MediaBrandingService.Instance.Branding;

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                _hasStatusMessage = !string.IsNullOrEmpty(value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }

        public bool HasStatusMessage => _hasStatusMessage;

        public MediaBrandingViewModel(INavigationService? navigationService = null)
        {
            _navigationService = navigationService
                ?? App.MainWindowInstance?.NavigationService
                ?? new NavigationService(null!);

            MediaBrandingService.Instance.BrandingChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(Branding));
            };
        }

        public void MoveUp(AdminMediaItem item)
        {
            MediaBrandingService.Instance.MoveUp(item);
            StatusMessage = $"Moved up '{item.FileName}'.";
        }

        public void MoveDown(AdminMediaItem item)
        {
            MediaBrandingService.Instance.MoveDown(item);
            StatusMessage = $"Moved down '{item.FileName}'.";
        }

        public void ToggleActive(AdminMediaItem item)
        {
            MediaBrandingService.Instance.ToggleActive(item);
            StatusMessage = $"Media '{item.FileName}' is now {item.StatusText}.";
        }

        public void ToggleAudio(AdminMediaItem item)
        {
            MediaBrandingService.Instance.ToggleAudio(item);
            StatusMessage = $"Sound for '{item.FileName}' is now {(item.IsAudioEnabled ? "ON" : "OFF (Muted)")}.";
        }

        public void RemoveMedia(AdminMediaItem item)
        {
            MediaBrandingService.Instance.RemoveMedia(item);
            StatusMessage = $"Media '{item.FileName}' removed.";
        }

        public void AddNewMedia(string type)
        {
            bool isVideo = string.Equals(type, "Video", StringComparison.OrdinalIgnoreCase);
            string name = isVideo ? $"video{new Random().Next(1, 3)}.mp4" : $"banner{new Random().Next(1, 10)}.jpg";

            var newItem = new AdminMediaItem
            {
                FileName = name,
                MediaType = type,
                DurationSeconds = isVideo ? 15 : 7,
                IsActive = true,
                SortOrder = MediaItems.Count
            };

            MediaBrandingService.Instance.AddMedia(newItem);
            StatusMessage = $"Added new {type}: {name}";
        }

        public void SaveBranding()
        {
            MediaBrandingService.Instance.SaveConfiguration();
            StatusMessage = "Branding settings and media playlist saved successfully!";
            Debug.WriteLine($"[Branding Saved] Company: {Branding.CompanyName}, Tagline: {Branding.Tagline}, Logo: {Branding.LogoFileName}");
        }

        public void NavigateBackToDiagnostics()
        {
            _navigationService.NavigateTo(KioskRoute.AdminDiagnostics, SlideNavigationTransitionEffect.FromLeft);
        }

        public void ExitToCustomerMode()
        {
            _navigationService.NavigateBackToCustomer(SlideNavigationTransitionEffect.FromLeft);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
