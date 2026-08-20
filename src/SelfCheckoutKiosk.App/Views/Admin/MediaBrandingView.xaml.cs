using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SelfCheckoutKiosk.App.Models;
using SelfCheckoutKiosk.App.ViewModels.Admin;
using System;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace SelfCheckoutKiosk.App.Views.Admin
{
    public sealed partial class MediaBrandingView : Page
    {
        public MediaBrandingViewModel ViewModel { get; }

        public MediaBrandingView()
        {
            InitializeComponent();
            ViewModel = new MediaBrandingViewModel();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.NavigateBackToDiagnostics();
        }

        private async void AddImageButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker();
                if (App.MainWindowInstance != null)
                {
                    var hwnd = WindowNative.GetWindowHandle(App.MainWindowInstance);
                    InitializeWithWindow.Initialize(picker, hwnd);
                }
                picker.ViewMode = PickerViewMode.Thumbnail;
                picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
                picker.FileTypeFilter.Add(".png");
                picker.FileTypeFilter.Add(".jpg");
                picker.FileTypeFilter.Add(".jpeg");

                var file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    ViewModel.MediaItems.Add(new AdminMediaItem
                    {
                        FileName = file.Name,
                        MediaType = "Image",
                        DurationSeconds = 10,
                        IsActive = true,
                        SortOrder = ViewModel.MediaItems.Count
                    });
                    ViewModel.StatusMessage = $"Added image '{file.Name}' to playlist.";
                }
            }
            catch
            {
                ViewModel.AddNewMedia("Image");
            }
        }

        private async void AddVideoButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker();
                if (App.MainWindowInstance != null)
                {
                    var hwnd = WindowNative.GetWindowHandle(App.MainWindowInstance);
                    InitializeWithWindow.Initialize(picker, hwnd);
                }
                picker.ViewMode = PickerViewMode.Thumbnail;
                picker.SuggestedStartLocation = PickerLocationId.VideosLibrary;
                picker.FileTypeFilter.Add(".mp4");
                picker.FileTypeFilter.Add(".mov");
                picker.FileTypeFilter.Add(".wmv");

                var file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    ViewModel.MediaItems.Add(new AdminMediaItem
                    {
                        FileName = file.Name,
                        MediaType = "Video",
                        DurationSeconds = 15,
                        IsActive = true,
                        SortOrder = ViewModel.MediaItems.Count
                    });
                    ViewModel.StatusMessage = $"Added video '{file.Name}' to playlist.";
                }
            }
            catch
            {
                ViewModel.AddNewMedia("Video");
            }
        }

        private void MoveUp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is AdminMediaItem item)
            {
                ViewModel.MoveUp(item);
            }
        }

        private void MoveDown_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is AdminMediaItem item)
            {
                ViewModel.MoveDown(item);
            }
        }

        private void DeleteMedia_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is AdminMediaItem item)
            {
                ViewModel.RemoveMedia(item);
            }
        }

        private void SaveBrandingButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.SaveBranding();
        }

        private async void ChangeLogoButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker();
                if (App.MainWindowInstance != null)
                {
                    var hwnd = WindowNative.GetWindowHandle(App.MainWindowInstance);
                    InitializeWithWindow.Initialize(picker, hwnd);
                }
                picker.ViewMode = PickerViewMode.Thumbnail;
                picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
                picker.FileTypeFilter.Add(".png");
                picker.FileTypeFilter.Add(".jpg");
                picker.FileTypeFilter.Add(".jpeg");
                picker.FileTypeFilter.Add(".ico");

                var file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    ViewModel.Branding.LogoFileName = !string.IsNullOrEmpty(file.Path) ? file.Path : file.Name;
                    ViewModel.StatusMessage = $"Logo updated to '{file.Name}'. Click 'Save Branding' to persist.";
                }
            }
            catch
            {
                ViewModel.Branding.LogoFileName = "ca.ico";
                ViewModel.StatusMessage = "Logo reset to default 'ca.ico'.";
            }
        }
    }
}
