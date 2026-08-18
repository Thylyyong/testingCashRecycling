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

        public ObservableCollection<AdminMediaItem> MediaItems { get; } = new();

        public BrandingConfig Branding
        {
            get => _branding;
            set { _branding = value; OnPropertyChanged(); }
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

            InitializeDefaultMedia();
        }

        private void InitializeDefaultMedia()
        {
            MediaItems.Clear();

            MediaItems.Add(new AdminMediaItem
            {
                FileName = "8afe2ed44f87458f9b3b9ab0b873cc01.mp4",
                MediaType = "Video",
                DurationSeconds = 10,
                IsActive = true,
                SortOrder = 0
            });

            MediaItems.Add(new AdminMediaItem
            {
                FileName = "branding-logo.png",
                MediaType = "Image",
                DurationSeconds = 10,
                IsActive = true,
                SortOrder = 1
            });
        }

        public void MoveUp(AdminMediaItem item)
        {
            int index = MediaItems.IndexOf(item);
            if (index > 0)
            {
                MediaItems.Move(index, index - 1);
                ReindexSortOrders();
            }
        }

        public void MoveDown(AdminMediaItem item)
        {
            int index = MediaItems.IndexOf(item);
            if (index >= 0 && index < MediaItems.Count - 1)
            {
                MediaItems.Move(index, index + 1);
                ReindexSortOrders();
            }
        }

        public void ToggleActive(AdminMediaItem item)
        {
            item.IsActive = !item.IsActive;
            StatusMessage = $"Media '{item.FileName}' is now {item.StatusText}.";
        }

        public void RemoveMedia(AdminMediaItem item)
        {
            if (MediaItems.Contains(item))
            {
                MediaItems.Remove(item);
                ReindexSortOrders();
                StatusMessage = $"Media '{item.FileName}' removed.";
            }
        }

        public void AddNewMedia(string type)
        {
            string ext = string.Equals(type, "Video", StringComparison.OrdinalIgnoreCase) ? "mp4" : "png";
            string name = $"{Guid.NewGuid().ToString("N").Substring(0, 12)}.{ext}";

            var newItem = new AdminMediaItem
            {
                FileName = name,
                MediaType = type,
                DurationSeconds = 10,
                IsActive = true,
                SortOrder = MediaItems.Count
            };

            MediaItems.Add(newItem);
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
