using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using SelfCheckoutKiosk.App.Models;

namespace SelfCheckoutKiosk.App.Services;

/// <summary>
/// Serializable data transfer object for persisting vault inventory state to disk.
/// </summary>
public sealed class VaultInventoryData
{
    public Dictionary<int, int> KhrCounts { get; set; } = new();
    public Dictionary<int, int> UsdCounts { get; set; } = new();
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Tracks physical banknotes deposited into the cash recycler / bill validator vault during transactions.
/// Dynamically updates counts upon note ingestion and persists state to disk across application restarts
/// until manually reset by store attendants or managers.
/// </summary>
public sealed class VaultInventoryService : INotifyPropertyChanged
{
    private static readonly Lazy<VaultInventoryService> _lazy = new(() => new VaultInventoryService());
    public static VaultInventoryService Instance => _lazy.Value;

    private static readonly string ConfigFilePath = Path.Combine(AppContext.BaseDirectory, "vault_inventory.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly object _syncLock = new();

    private readonly Dictionary<int, int> _khrCounts = new()
    {
        { 100, 0 },
        { 500, 0 },
        { 1000, 0 },
        { 5000, 0 },
        { 10000, 0 },
        { 20000, 0 },
        { 50000, 0 }
    };

    private readonly Dictionary<int, int> _usdCounts = new()
    {
        { 1, 0 },
        { 5, 0 },
        { 10, 0 },
        { 20, 0 },
        { 50, 0 },
        { 100, 0 }
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? VaultInventoryChanged;

    public IReadOnlyDictionary<int, int> KhrCounts
    {
        get
        {
            lock (_syncLock)
            {
                return new Dictionary<int, int>(_khrCounts);
            }
        }
    }

    public IReadOnlyDictionary<int, int> UsdCounts
    {
        get
        {
            lock (_syncLock)
            {
                return new Dictionary<int, int>(_usdCounts);
            }
        }
    }

    private VaultInventoryService()
    {
        LoadPersistedVault();
    }

    /// <summary>
    /// Records a physically deposited banknote into the vault count and persists to disk.
    /// </summary>
    public void RecordDeposit(string currency, int denomination, int count = 1)
    {
        if (count <= 0) return;

        lock (_syncLock)
        {
            if (string.Equals(currency, "KHR", StringComparison.OrdinalIgnoreCase))
            {
                if (_khrCounts.ContainsKey(denomination))
                    _khrCounts[denomination] += count;
                else
                    _khrCounts[denomination] = count;
            }
            else if (string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase))
            {
                if (_usdCounts.ContainsKey(denomination))
                    _usdCounts[denomination] += count;
                else
                    _usdCounts[denomination] = count;
            }

            SavePersistedVault();
        }

        OnPropertyChanged(nameof(KhrCounts));
        OnPropertyChanged(nameof(UsdCounts));
        VaultInventoryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Resets all note counts in the vault to zero (e.g. after physical cash collection/reconciliation)
    /// and persists the reset state to disk.
    /// </summary>
    public void ResetVault()
    {
        lock (_syncLock)
        {
            var khrKeys = new List<int>(_khrCounts.Keys);
            foreach (var key in khrKeys)
            {
                _khrCounts[key] = 0;
            }

            var usdKeys = new List<int>(_usdCounts.Keys);
            foreach (var key in usdKeys)
            {
                _usdCounts[key] = 0;
            }

            SavePersistedVault();
        }

        OnPropertyChanged(nameof(KhrCounts));
        OnPropertyChanged(nameof(UsdCounts));
        VaultInventoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void LoadPersistedVault()
    {
        lock (_syncLock)
        {
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    string json = File.ReadAllText(ConfigFilePath);
                    var data = JsonSerializer.Deserialize<VaultInventoryData>(json, JsonOptions);
                    if (data != null)
                    {
                        if (data.KhrCounts != null)
                        {
                            foreach (var kvp in data.KhrCounts)
                            {
                                _khrCounts[kvp.Key] = kvp.Value;
                            }
                        }

                        if (data.UsdCounts != null)
                        {
                            foreach (var kvp in data.UsdCounts)
                            {
                                _usdCounts[kvp.Key] = kvp.Value;
                            }
                        }

                        Debug.WriteLine($"[VaultInventoryService] Successfully loaded persisted vault counts from {ConfigFilePath}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VaultInventoryService] Warning: Failed to load persisted vault counts: {ex.Message}");
            }
        }
    }

    private void SavePersistedVault()
    {
        try
        {
            var data = new VaultInventoryData
            {
                KhrCounts = new Dictionary<int, int>(_khrCounts),
                UsdCounts = new Dictionary<int, int>(_usdCounts),
                LastUpdatedUtc = DateTime.UtcNow
            };

            string json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(ConfigFilePath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VaultInventoryService] Error: Failed to save vault counts to {ConfigFilePath}: {ex.Message}");
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
