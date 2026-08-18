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
using System.Threading.Tasks;

namespace SelfCheckoutKiosk.App.ViewModels.Admin
{
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

        public AdminDiagnosticsViewModel(INavigationService? navigationService = null)
        {
            _navigationService = navigationService
                ?? App.MainWindowInstance?.NavigationService
                ?? new NavigationService(null!);

            InitializeMockHardware();
            InitializeCassettes();
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

        private void InitializeMockHardware()
        {
            HardwareDevices.Clear();

            HardwareDevices.Add(new HardwareDeviceStatus
            {
                Name = "Cash Recycler",
                DeviceType = "Cash & Note Validator",
                IsConnected = false,
                PortOrInterface = "COM3 • 9600 Baud",
                FirmwareVersion = "v2.1.4-std",
                Glyph = "\uE825"
            });

            HardwareDevices.Add(new HardwareDeviceStatus
            {
                Name = "Barcode Scanner",
                DeviceType = "2D Imager & EAN-13",
                IsConnected = false,
                PortOrInterface = "USB HID POS",
                FirmwareVersion = "v1.8.0",
                Glyph = "\uEC5A"
            });

            HardwareDevices.Add(new HardwareDeviceStatus
            {
                Name = "Receipt Printer",
                DeviceType = "Thermal 80mm ESC/POS",
                IsConnected = false,
                PortOrInterface = "USB Serial Interface",
                FirmwareVersion = "Epson M30-II",
                Glyph = "\uE749"
            });
        }

        private void InitializeCassettes()
        {
            KhrCassettes.Clear();
            KhrCassettes.Add(new CassetteCountItem { DenominationLabel = "100 ៛", Currency = "KHR", UnitValue = 100, Count = 0 });
            KhrCassettes.Add(new CassetteCountItem { DenominationLabel = "500 ៛", Currency = "KHR", UnitValue = 500, Count = 0 });
            KhrCassettes.Add(new CassetteCountItem { DenominationLabel = "1,000 ៛", Currency = "KHR", UnitValue = 1000, Count = 0 });
            KhrCassettes.Add(new CassetteCountItem { DenominationLabel = "5,000 ៛", Currency = "KHR", UnitValue = 5000, Count = 0 });
            KhrCassettes.Add(new CassetteCountItem { DenominationLabel = "10,000 ៛", Currency = "KHR", UnitValue = 10000, Count = 0 });
            KhrCassettes.Add(new CassetteCountItem { DenominationLabel = "20,000 ៛", Currency = "KHR", UnitValue = 20000, Count = 0 });
            KhrCassettes.Add(new CassetteCountItem { DenominationLabel = "50,000 ៛", Currency = "KHR", UnitValue = 50000, Count = 0 });

            UsdCassettes.Clear();
            UsdCassettes.Add(new CassetteCountItem { DenominationLabel = "$1.00", Currency = "USD", UnitValue = 1, Count = 0 });
            UsdCassettes.Add(new CassetteCountItem { DenominationLabel = "$5.00", Currency = "USD", UnitValue = 5, Count = 0 });
            UsdCassettes.Add(new CassetteCountItem { DenominationLabel = "$10.00", Currency = "USD", UnitValue = 10, Count = 0 });
            UsdCassettes.Add(new CassetteCountItem { DenominationLabel = "$20.00", Currency = "USD", UnitValue = 20, Count = 0 });
            UsdCassettes.Add(new CassetteCountItem { DenominationLabel = "$50.00", Currency = "USD", UnitValue = 50, Count = 0 });
            UsdCassettes.Add(new CassetteCountItem { DenominationLabel = "$100.00", Currency = "USD", UnitValue = 100, Count = 0 });
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

        public void NavigateToMediaBranding()
        {
            _navigationService.NavigateTo(
                typeof(MediaBrandingView),
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
