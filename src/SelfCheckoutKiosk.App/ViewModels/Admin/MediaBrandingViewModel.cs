using Microsoft.UI.Xaml.Media.Animation;
using SelfCheckoutKiosk.App.Models;
using SelfCheckoutKiosk.App.Services;
using SelfCheckoutKiosk.App.Views;
using SelfCheckoutKiosk.App.Views.Admin;
using SelfCheckoutKiosk.App.Views.Customer;
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

        private BrandingConfig _branding = new();
        private string _statusMessage = string.Empty;
        private bool _hasStatusMessage = false;

        public ObservableCollection<AdminMediaItem> MediaItems => MediaBrandingService.Instance.MediaItems;

        public BrandingConfig Branding
        {
            get => MediaBrandingService.Instance.Branding;
            set { OnPropertyChanged(); }
        }

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
            StatusMessage = "Branding settings and media playlist saved successfully!";
            Debug.WriteLine($"[Branding Saved] Company: {Branding.CompanyName}, Tagline: {Branding.Tagline}");
        }

        private void ReindexSortOrders()
        {
            for (int i = 0; i < MediaItems.Count; i++)
            {
                MediaItems[i].SortOrder = i;
            }
        }

        public void NavigateBackToDiagnostics()
        {
            _navigationService.NavigateTo(
                typeof(AdminDiagnosticsView),
                null,
                new SuppressNavigationTransitionInfo()
            );
        }

        public void ExitToCustomerMode()
        {
            _navigationService.NavigateTo(
                typeof(KioskBaseView),
                null,
                new SuppressNavigationTransitionInfo()
            );
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
