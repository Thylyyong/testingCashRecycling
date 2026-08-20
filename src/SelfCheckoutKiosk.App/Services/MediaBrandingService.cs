using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using SelfCheckoutKiosk.App.Models;

namespace SelfCheckoutKiosk.App.Services;

/// <summary>
/// Centralized service managing media playlist (videos & images) and kiosk branding.
/// Shared across Admin Media Diagnostics and Customer KioskBaseView welcome banner.
/// </summary>
public sealed class MediaBrandingService
{
    private static readonly Lazy<MediaBrandingService> _lazy = new(() => new MediaBrandingService());
    public static MediaBrandingService Instance => _lazy.Value;

    public ObservableCollection<AdminMediaItem> MediaItems { get; } = new();
    public BrandingConfig Branding { get; } = new();

    public event EventHandler? PlaylistChanged;

    private MediaBrandingService()
    {
        InitializeDefaultPlaylist();
        MediaItems.CollectionChanged += MediaItems_CollectionChanged;
        foreach (var item in MediaItems)
        {
            item.PropertyChanged += Item_PropertyChanged;
        }
    }

    private void MediaItems_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (AdminMediaItem item in e.NewItems)
            {
                item.PropertyChanged += Item_PropertyChanged;
            }
        }
        if (e.OldItems != null)
        {
            foreach (AdminMediaItem item in e.OldItems)
            {
                item.PropertyChanged -= Item_PropertyChanged;
            }
        }
        PlaylistChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Item_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AdminMediaItem.IsActive) ||
            e.PropertyName == nameof(AdminMediaItem.SortOrder) ||
            e.PropertyName == nameof(AdminMediaItem.DurationSeconds) ||
            e.PropertyName == nameof(AdminMediaItem.FileName))
        {
            PlaylistChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void InitializeDefaultPlaylist()
    {
        MediaItems.Clear();

        // 1. video1.mp4 (Promotional Video 1)
        MediaItems.Add(new AdminMediaItem
        {
            FileName = "video1.mp4",
            MediaType = "Video",
            DurationSeconds = 15,
            IsActive = true,
            SortOrder = 0
        });

        // 2. banner1.jpg (Welcome Banner)
        MediaItems.Add(new AdminMediaItem
        {
            FileName = "banner1.jpg",
            MediaType = "Image",
            DurationSeconds = 7,
            IsActive = true,
            SortOrder = 1
        });

        // 3. video2.mp4 (Promotional Video 2)
        MediaItems.Add(new AdminMediaItem
        {
            FileName = "video2.mp4",
            MediaType = "Video",
            DurationSeconds = 15,
            IsActive = true,
            SortOrder = 2
        });

        // 4. banner2.jpg - banner5.jpg (Featured Store Banners)
        for (int i = 2; i <= 5; i++)
        {
            MediaItems.Add(new AdminMediaItem
            {
                FileName = $"banner{i}.jpg",
                MediaType = "Image",
                DurationSeconds = 7,
                IsActive = true,
                SortOrder = i + 1
            });
        }
    }

    public void MoveUp(AdminMediaItem item)
    {
        int index = MediaItems.IndexOf(item);
        if (index > 0)
        {
            MediaItems.Move(index, index - 1);
            ReindexSortOrders();
            PlaylistChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void MoveDown(AdminMediaItem item)
    {
        int index = MediaItems.IndexOf(item);
        if (index >= 0 && index < MediaItems.Count - 1)
        {
            MediaItems.Move(index, index + 1);
            ReindexSortOrders();
            PlaylistChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ToggleActive(AdminMediaItem item)
    {
        item.IsActive = !item.IsActive;
        PlaylistChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveMedia(AdminMediaItem item)
    {
        if (MediaItems.Remove(item))
        {
            ReindexSortOrders();
            PlaylistChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void AddMedia(AdminMediaItem item)
    {
        item.SortOrder = MediaItems.Count;
        MediaItems.Add(item);
        PlaylistChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ReindexSortOrders()
    {
        for (int i = 0; i < MediaItems.Count; i++)
        {
            MediaItems[i].SortOrder = i;
        }
    }

    public List<BannerMedia> GetActiveBannerPlaylist()
    {
        var active = MediaItems
            .Where(m => m.IsActive)
            .OrderBy(m => m.SortOrder)
            .Select(m => new BannerMedia
            {
                Path = m.UriPath,
                IsVideo = m.IsVideo,
                DurationSeconds = m.DurationSeconds > 0 ? m.DurationSeconds : 7
            })
            .ToList();

        if (active.Count == 0)
        {
            active.Add(new BannerMedia
            {
                Path = "ms-appx:///Assets/Images/Banner/banner1.jpg",
                IsVideo = false,
                DurationSeconds = 7
            });
        }

        return active;
    }
}
