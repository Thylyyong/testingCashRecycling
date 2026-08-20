using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SelfCheckoutKiosk.App.Models;

namespace SelfCheckoutKiosk.App.Services;

/// <summary>
/// Tracks physical banknotes deposited into the cash recycler vault during transactions.
/// Dynamically updates counts upon note ingestion and supports resetting by store attendants.
/// </summary>
public sealed class VaultInventoryService : INotifyPropertyChanged
{
    private static readonly Lazy<VaultInventoryService> _lazy = new(() => new VaultInventoryService());
    public static VaultInventoryService Instance => _lazy.Value;

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

    public IReadOnlyDictionary<int, int> KhrCounts => _khrCounts;
    public IReadOnlyDictionary<int, int> UsdCounts => _usdCounts;

    /// <summary>
    /// Records a physically deposited banknote into the vault count.
    /// </summary>
    public void RecordDeposit(string currency, int denomination, int count = 1)
    {
        if (count <= 0) return;

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

        OnPropertyChanged(nameof(KhrCounts));
        OnPropertyChanged(nameof(UsdCounts));
        VaultInventoryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Resets all note counts in the vault to zero (e.g. after cash collection/reconciliation).
    /// </summary>
    public void ResetVault()
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

        OnPropertyChanged(nameof(KhrCounts));
        OnPropertyChanged(nameof(UsdCounts));
        VaultInventoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
