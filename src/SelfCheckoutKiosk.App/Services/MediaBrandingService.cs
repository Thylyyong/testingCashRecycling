using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using SelfCheckoutKiosk.App.Models;

namespace SelfCheckoutKiosk.App.Services;

/// <summary>
/// Centralized service managing media playlist (videos &amp; images) and kiosk branding.
/// Shared across Admin Media Diagnostics and Customer HomeView / KioskBaseView welcome banner.
/// Persists configuration to local JSON storage for cross-session state retention.
/// </summary>
public sealed class MediaBrandingService
{
    private static readonly Lazy<MediaBrandingService> _lazy = new(() => new MediaBrandingService());
    public static MediaBrandingService Instance => _lazy.Value;

    private static readonly string PrimaryConfigFilePath = Path.Combine(AppContext.BaseDirectory, "branding_config.json");
    private static readonly string FallbackConfigFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SelfCheckoutKiosk",
        "branding_config.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public ObservableCollection<AdminMediaItem> MediaItems { get; } = new();
    public BrandingConfig Branding { get; } = new();

    public event EventHandler? PlaylistChanged;
    public event EventHandler? BrandingChanged;

    private bool _isLoading = false;

    private MediaBrandingService()
    {
        LoadPersistedConfiguration();

        Branding.PropertyChanged += (s, e) =>
        {
            if (!_isLoading)
            {
                BrandingChanged?.Invoke(this, EventArgs.Empty);
            }
        };

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
        if (!_isLoading)
        {
            PlaylistChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Item_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_isLoading) return;

        if (e.PropertyName == nameof(AdminMediaItem.IsActive) ||
            e.PropertyName == nameof(AdminMediaItem.IsAudioEnabled) ||
            e.PropertyName == nameof(AdminMediaItem.SortOrder) ||
            e.PropertyName == nameof(AdminMediaItem.DurationSeconds) ||
            e.PropertyName == nameof(AdminMediaItem.FileName))
        {
            PlaylistChanged?.Invoke(this, EventArgs.Empty);
            SaveConfiguration();
        }
    }

    private void LoadPersistedConfiguration()
    {
        _isLoading = true;
        try
        {
            string? loadPath = null;
            if (File.Exists(PrimaryConfigFilePath))
            {
                loadPath = PrimaryConfigFilePath;
            }
            else if (File.Exists(FallbackConfigFilePath))
            {
                loadPath = FallbackConfigFilePath;
            }

            if (loadPath != null)
            {
                string json = File.ReadAllText(loadPath);
                var stored = JsonSerializer.Deserialize<BrandingStorageModel>(json, JsonOptions);
                if (stored != null)
                {
                    if (stored.Branding != null)
                    {
                        Branding.CompanyName = stored.Branding.CompanyName ?? "CA Solution";
                        Branding.Tagline = stored.Branding.Tagline ?? "Scan & Go Self-Checkout";
                        Branding.LogoFileName = stored.Branding.LogoFileName ?? "ca.ico";
                        Branding.KioskId = stored.Branding.KioskId ?? "KIOSK-01";
                        Branding.StoreHours = stored.Branding.StoreHours ?? "Open until 10:00 PM";
                    }

                    MediaItems.Clear();

                    var loadedItems = stored.MediaItems ?? new List<AdminMediaItem>();
                    var defaultItems = GetDefaultMediaList();
                    var loadedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    // 1. Restore all saved items in their custom sort order and saved active toggle state
                    foreach (var item in loadedItems.OrderBy(m => m.SortOrder))
                    {
                        MediaItems.Add(item);
                        if (!string.IsNullOrWhiteSpace(item.FileName))
                        {
                            loadedFileNames.Add(item.FileName);
                        }
                    }

                    // 2. Ensure default bundled media items exist on startup (restores temporarily deleted items on app launch)
                    int nextOrder = MediaItems.Count;
                    foreach (var def in defaultItems)
                    {
                        if (!loadedFileNames.Contains(def.FileName))
                        {
                            def.SortOrder = nextOrder++;
                            MediaItems.Add(def);
                        }
                    }

                    ReindexSortOrders();
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MediaBrandingService] Could not load persisted branding config: {ex.Message}");
        }
        finally
        {
            _isLoading = false;
        }

        // Fallback to default playlist if no stored items found
        InitializeDefaultPlaylist();
    }

    public void SaveConfiguration()
    {
        if (_isLoading) return;

        try
        {
            var model = new BrandingStorageModel
            {
                Branding = Branding,
                MediaItems = MediaItems.ToList()
            };

            string json = JsonSerializer.Serialize(model, JsonOptions);

            // Attempt to write to base directory first; fallback to LocalAppData if directory is write-protected (e.g. Program Files)
            try
            {
                File.WriteAllText(PrimaryConfigFilePath, json);
                Debug.WriteLine($"[MediaBrandingService] Branding configuration persisted to: {PrimaryConfigFilePath}");
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
            {
                string? dir = Path.GetDirectoryName(FallbackConfigFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(FallbackConfigFilePath, json);
                Debug.WriteLine($"[MediaBrandingService] Fallback: Branding configuration persisted to: {FallbackConfigFilePath}");
            }

            BrandingChanged?.Invoke(this, EventArgs.Empty);
            PlaylistChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MediaBrandingService] Failed to persist branding config: {ex.Message}");
        }
    }

    public static List<AdminMediaItem> GetDefaultMediaList()
    {
        return new List<AdminMediaItem>
        {
            // 1. video1.mp4 (Promotional Video 1)
            new AdminMediaItem
            {
                FileName = "video1.mp4",
                MediaType = "Video",
                DurationSeconds = 15,
                IsActive = true,
                SortOrder = 0
            },
            // 2. banner1.jpg (Welcome Banner)
            new AdminMediaItem
            {
                FileName = "banner1.jpg",
                MediaType = "Image",
                DurationSeconds = 7,
                IsActive = true,
                SortOrder = 1
            },
            // 3. video2.mp4 (Promotional Video 2)
            new AdminMediaItem
            {
                FileName = "video2.mp4",
                MediaType = "Video",
                DurationSeconds = 15,
                IsActive = true,
                SortOrder = 2
            },
            // 4. banner2.jpg - banner5.jpg (Featured Store Banners)
            new AdminMediaItem { FileName = "banner2.jpg", MediaType = "Image", DurationSeconds = 7, IsActive = true, SortOrder = 3 },
            new AdminMediaItem { FileName = "banner3.jpg", MediaType = "Image", DurationSeconds = 7, IsActive = true, SortOrder = 4 },
            new AdminMediaItem { FileName = "banner4.jpg", MediaType = "Image", DurationSeconds = 7, IsActive = true, SortOrder = 5 },
            new AdminMediaItem { FileName = "banner5.jpg", MediaType = "Image", DurationSeconds = 7, IsActive = true, SortOrder = 6 }
        };
    }

    private void InitializeDefaultPlaylist()
    {
        _isLoading = true;
        try
        {
            MediaItems.Clear();
            foreach (var item in GetDefaultMediaList())
            {
                MediaItems.Add(item);
            }
        }
        finally
        {
            _isLoading = false;
        }
    }

    public void MoveUp(AdminMediaItem item)
    {
        int index = MediaItems.IndexOf(item);
        if (index > 0)
        {
            MediaItems.Move(index, index - 1);
            ReindexSortOrders();
            SaveConfiguration();
        }
    }

    public void MoveDown(AdminMediaItem item)
    {
        int index = MediaItems.IndexOf(item);
        if (index >= 0 && index < MediaItems.Count - 1)
        {
            MediaItems.Move(index, index + 1);
            ReindexSortOrders();
            SaveConfiguration();
        }
    }

    public void ToggleActive(AdminMediaItem item)
    {
        item.IsActive = !item.IsActive;
        SaveConfiguration();
    }

    public void ToggleAudio(AdminMediaItem item)
    {
        item.IsAudioEnabled = !item.IsAudioEnabled;
        SaveConfiguration();
    }

    public void RemoveMedia(AdminMediaItem item)
    {
        if (MediaItems.Remove(item))
        {
            ReindexSortOrders();
            SaveConfiguration();
        }
    }

    public void AddMedia(AdminMediaItem item)
    {
        item.SortOrder = MediaItems.Count;
        MediaItems.Add(item);
        SaveConfiguration();
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
                IsAudioEnabled = m.IsAudioEnabled,
                DurationSeconds = m.DurationSeconds > 0 ? m.DurationSeconds : 7
            })
            .ToList();

        if (active.Count == 0)
        {
            active.Add(new BannerMedia
            {
                Path = "ms-appx:///Assets/Images/Banner/banner1.jpg",
                IsVideo = false,
                IsAudioEnabled = false,
                DurationSeconds = 7
            });
        }

        return active;
    }
}

/// <summary>
/// Data contract for serializing branding configuration and media items to JSON.
/// </summary>
public class BrandingStorageModel
{
    public BrandingConfig Branding { get; set; } = new();
    public List<AdminMediaItem> MediaItems { get; set; } = new();
}
