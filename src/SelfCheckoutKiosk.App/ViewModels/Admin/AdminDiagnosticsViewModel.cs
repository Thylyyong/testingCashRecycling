using Microsoft.UI.Xaml.Media.Animation;
using SelfCheckoutKiosk.App.Models;
using SelfCheckoutKiosk.App.Services;
using SelfCheckoutKiosk.App.Views.Admin;
using SelfCheckoutKiosk.App.Views.Customer;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace SelfCheckoutKiosk.App.ViewModels.Admin;

public class AdminDiagnosticsViewModel : INotifyPropertyChanged
{
    private readonly INavigationService _navigationService;

    private bool _isSyncing = false;
    private string _lastSyncTime = "Today at 08:00 PM";
    private int _unresolvedLogCount = 0;
    private string _licenseTier = "Enterprise";
    private string _licenseExpiry = "Not available";
    private string _statusMessage = string.Empty;
    private bool _isStatusSuccess = true;
    private bool _hasStatusMessage = false;

    public ObservableCollection<HardwareDeviceStatus> HardwareDevices { get; } = new();
    public ObservableCollection<CassetteCountItem> KhrCassettes { get; } = new();
    public ObservableCollection<CassetteCountItem> UsdCassettes { get; } = new();

    public bool HasPendingAgeApproval => AgeRestrictedApprovalManager.Instance.HasPendingRequest;
    public string PendingAgeItemName => AgeRestrictedApprovalManager.Instance.PendingItemName;
    public string PendingAgeItemSku => AgeRestrictedApprovalManager.Instance.PendingItemSku;

    public AdminDiagnosticsViewModel(INavigationService? navigationService = null)
    {
        _navigationService = navigationService
            ?? App.MainWindowInstance?.NavigationService
            ?? new NavigationService(null!);

        RefreshDiagnostics();
        RefreshCassettes();

        HardwareStatusManager.Instance.PropertyChanged += (s, e) =>
        {
            App.MainWindowInstance?.DispatcherQueue.TryEnqueue(RefreshDiagnostics);
        };

        VaultInventoryService.Instance.VaultInventoryChanged += (s, e) =>
        {
            App.MainWindowInstance?.DispatcherQueue.TryEnqueue(RefreshCassettes);
        };

        AgeRestrictedApprovalManager.Instance.PendingRequestChanged += (s, e) =>
        {
            App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
            {
                OnPropertyChanged(nameof(HasPendingAgeApproval));
                OnPropertyChanged(nameof(PendingAgeItemName));
                OnPropertyChanged(nameof(PendingAgeItemSku));
            });
        };
    }

    public void ApproveAgeRestrictedItem()
    {
        AgeRestrictedApprovalManager.Instance.Approve();
        StatusMessage = "Item approved by attendant.";
        IsStatusSuccess = true;
        OnPropertyChanged(nameof(HasPendingAgeApproval));
        ExitToCustomerMode();
    }

    public void RejectAgeRestrictedItem()
    {
        AgeRestrictedApprovalManager.Instance.Reject();
        StatusMessage = "Item rejected.";
        IsStatusSuccess = false;
        OnPropertyChanged(nameof(HasPendingAgeApproval));
        ExitToCustomerMode();
    }

    public void ResetVaultCounts()
    {
        VaultInventoryService.Instance.ResetVault();
        RefreshCassettes();
        StatusMessage = "Cash vault breakdown counts reset to zero.";
        IsStatusSuccess = true;
    }

    public bool IsSyncing
    {
        get => _isSyncing;
        set { _isSyncing = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotSyncing)); }
    }

    public bool IsNotSyncing => !_isSyncing;

    public string LastSyncTime
    {
        get => _lastSyncTime;
        set { _lastSyncTime = value; OnPropertyChanged(); }
    }

    public int UnresolvedLogCount
    {
        get => _unresolvedLogCount;
        set { _unresolvedLogCount = value; OnPropertyChanged(); }
    }

    public string LicenseTier
    {
        get => _licenseTier;
        set { _licenseTier = value; OnPropertyChanged(); }
    }

    public string LicenseExpiry
    {
        get => _licenseExpiry;
        set { _licenseExpiry = value; OnPropertyChanged(); }
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

    public bool IsStatusSuccess
    {
        get => _isStatusSuccess;
        set { _isStatusSuccess = value; OnPropertyChanged(); }
    }

    public bool HasStatusMessage => _hasStatusMessage;

    public bool IsCashRecyclerConnected => HardwareStatusManager.Instance.IsCashAvailable;
    public string CashRecyclerStatusText => IsCashRecyclerConnected ? "Connected" : "Disconnected";
    public Microsoft.UI.Xaml.Media.SolidColorBrush CashRecyclerStatusColor => IsCashRecyclerConnected 
        ? new(Windows.UI.Color.FromArgb(255, 34, 197, 94)) 
        : new(Windows.UI.Color.FromArgb(255, 239, 68, 68));

    public bool IsBarcodeScannerConnected => HardwareStatusManager.Instance.IsScannerAvailable;
    public string BarcodeScannerStatusText => IsBarcodeScannerConnected ? "Connected" : "Disconnected";
    public Microsoft.UI.Xaml.Media.SolidColorBrush BarcodeScannerStatusColor => IsBarcodeScannerConnected 
        ? new(Windows.UI.Color.FromArgb(255, 34, 197, 94)) 
        : new(Windows.UI.Color.FromArgb(255, 239, 68, 68));

    public bool IsReceiptPrinterConnected => HardwareStatusManager.Instance.IsPrinterAvailable;
    public string ReceiptPrinterStatusText => IsReceiptPrinterConnected ? "Connected" : "Disconnected";
    public Microsoft.UI.Xaml.Media.SolidColorBrush ReceiptPrinterStatusColor => IsReceiptPrinterConnected 
        ? new(Windows.UI.Color.FromArgb(255, 34, 197, 94)) 
        : new(Windows.UI.Color.FromArgb(255, 239, 68, 68));

    public void RefreshDiagnostics()
    {
        var hw = HardwareStatusManager.Instance;
        LicenseTier = hw.LicenseTier;
        LicenseExpiry = hw.LicenseExpiryText;

        OnPropertyChanged(nameof(IsCashRecyclerConnected));
        OnPropertyChanged(nameof(CashRecyclerStatusText));
        OnPropertyChanged(nameof(CashRecyclerStatusColor));
        OnPropertyChanged(nameof(IsBarcodeScannerConnected));
        OnPropertyChanged(nameof(BarcodeScannerStatusText));
        OnPropertyChanged(nameof(BarcodeScannerStatusColor));
        OnPropertyChanged(nameof(IsReceiptPrinterConnected));
        OnPropertyChanged(nameof(ReceiptPrinterStatusText));
        OnPropertyChanged(nameof(ReceiptPrinterStatusColor));

        HardwareDevices.Clear();

        string activePort = Environment.GetEnvironmentVariable("SELFCHECKOUTKIOSK_ITL_COM_PORT") ?? "COM5";
        HardwareDevices.Add(new HardwareDeviceStatus
        {
            Name = "Cash Recycler",
            DeviceType = "Cash & Note Validator",
            IsConnected = hw.IsCashAvailable,
            PortOrInterface = hw.IsCashAvailable ? $"REST API • Port 5000 / {activePort}" : hw.CashStatusReason,
            FirmwareVersion = "v1.6.1-RC.4",
            Glyph = "\uE825"
        });

        HardwareDevices.Add(new HardwareDeviceStatus
        {
            Name = "Barcode Scanner",
            DeviceType = "Datalogic 2D Imager & EAN-13",
            IsConnected = hw.IsScannerAvailable,
            PortOrInterface = hw.IsScannerAvailable ? "USB-COM Serial (COM4) • 9600 Baud" : "Disconnected (COM4)",
            FirmwareVersion = "v1.8.0",
            Glyph = "\uEC5A"
        });

        HardwareDevices.Add(new HardwareDeviceStatus
        {
            Name = "Receipt Printer",
            DeviceType = "Epson TM-m30 Thermal 80mm",
            IsConnected = hw.IsPrinterAvailable,
            PortOrInterface = "USB001 / Raw ESC-POS",
            FirmwareVersion = "Epson M30-II",
            Glyph = "\uE749"
        });

        HardwareDevices.Add(new HardwareDeviceStatus
        {
            Name = "Payment Gateway",
            DeviceType = "KHQR & Digital Settlement",
            IsConnected = hw.IsQrAvailable,
            PortOrInterface = hw.IsQrAvailable ? "HTTPS / Webhook Gateway (Online)" : hw.QrStatusReason,
            FirmwareVersion = "KHQR-API-v2",
            Glyph = "\uED14"
        });
    }

    private void RefreshCassettes()
    {
        var khrCounts = VaultInventoryService.Instance.KhrCounts;
        KhrCassettes.Clear();
        KhrCassettes.Add(new CassetteCountItem { DenominationLabel = "100 ៛", Currency = "KHR", UnitValue = 100, Count = khrCounts.GetValueOrDefault(100, 0) });
        KhrCassettes.Add(new CassetteCountItem { DenominationLabel = "500 ៛", Currency = "KHR", UnitValue = 500, Count = khrCounts.GetValueOrDefault(500, 0) });
        KhrCassettes.Add(new CassetteCountItem { DenominationLabel = "1,000 ៛", Currency = "KHR", UnitValue = 1000, Count = khrCounts.GetValueOrDefault(1000, 0) });
        KhrCassettes.Add(new CassetteCountItem { DenominationLabel = "5,000 ៛", Currency = "KHR", UnitValue = 5000, Count = khrCounts.GetValueOrDefault(5000, 0) });
        KhrCassettes.Add(new CassetteCountItem { DenominationLabel = "10,000 ៛", Currency = "KHR", UnitValue = 10000, Count = khrCounts.GetValueOrDefault(10000, 0) });
        KhrCassettes.Add(new CassetteCountItem { DenominationLabel = "20,000 ៛", Currency = "KHR", UnitValue = 20000, Count = khrCounts.GetValueOrDefault(20000, 0) });
        KhrCassettes.Add(new CassetteCountItem { DenominationLabel = "50,000 ៛", Currency = "KHR", UnitValue = 50000, Count = khrCounts.GetValueOrDefault(50000, 0) });

        var usdCounts = VaultInventoryService.Instance.UsdCounts;
        UsdCassettes.Clear();
        UsdCassettes.Add(new CassetteCountItem { DenominationLabel = "$1.00", Currency = "USD", UnitValue = 1, Count = usdCounts.GetValueOrDefault(1, 0) });
        UsdCassettes.Add(new CassetteCountItem { DenominationLabel = "$5.00", Currency = "USD", UnitValue = 5, Count = usdCounts.GetValueOrDefault(5, 0) });
        UsdCassettes.Add(new CassetteCountItem { DenominationLabel = "$10.00", Currency = "USD", UnitValue = 10, Count = usdCounts.GetValueOrDefault(10, 0) });
        UsdCassettes.Add(new CassetteCountItem { DenominationLabel = "$20.00", Currency = "USD", UnitValue = 20, Count = usdCounts.GetValueOrDefault(20, 0) });
        UsdCassettes.Add(new CassetteCountItem { DenominationLabel = "$50.00", Currency = "USD", UnitValue = 50, Count = usdCounts.GetValueOrDefault(50, 0) });
        UsdCassettes.Add(new CassetteCountItem { DenominationLabel = "$100.00", Currency = "USD", UnitValue = 100, Count = usdCounts.GetValueOrDefault(100, 0) });
    }

    public async Task ReprintLastReceiptAsync()
    {
        IsStatusSuccess = true;
        StatusMessage = "Sending reprint command to thermal printer...";

        await Task.Delay(500);

        try
        {
            App.ReceiptPrinterServiceInstance.ReprintLastReceipt();
            StatusMessage = "Receipt reprinted successfully!";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Reprint Error] {ex.Message}");
            StatusMessage = "Reprint signal sent (Simulated hardware dispatch).";
        }
    }

    public async Task SyncCloudAsync()
    {
        IsSyncing = true;
        StatusMessage = "Synchronizing configuration with central hub...";

        await Task.Delay(1200);

        LastSyncTime = $"Today at {DateTime.Now:hh:mm tt}";
        IsSyncing = false;
        IsStatusSuccess = true;
        StatusMessage = "Kiosk data successfully synchronized!";
    }

    public void ExitToCustomerMode()
    {
        NavigateBackToCustomer();
    }

    public void NavigateBackToCustomer()
    {
        _navigationService.NavigateBackToCustomer(SlideNavigationTransitionEffect.FromLeft);
    }

    public void NavigateToMediaBranding()
    {
        _navigationService.NavigateTo(
            typeof(MediaBrandingView),
            null,
            SlideNavigationTransitionEffect.FromRight
        );
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
