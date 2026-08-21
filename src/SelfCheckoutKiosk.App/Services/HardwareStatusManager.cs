using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SelfCheckoutKiosk.App.Services;

/// <summary>
/// Central hardware and service status manager.
/// Tracks real-time availability of Bill Acceptor, Barcode Scanner, Receipt Printer, Central Server/Cloud, and License.
/// </summary>
public sealed class HardwareStatusManager : INotifyPropertyChanged
{
    private static readonly Lazy<HardwareStatusManager> _lazy = new(() => new HardwareStatusManager());
    public static HardwareStatusManager Instance => _lazy.Value;

    private bool _isCashAvailable = false;
    private bool _isQrAvailable = true;
    private bool _isCardAvailable = false;
    private bool _isIntlQrAvailable = false;
    private bool _isMembershipAvailable = false;
    private bool _isCouponAvailable = false;
    private bool _isScannerAvailable = false;
    private bool _isPrinterAvailable = false;
    private bool _isServerOnline = false;
    private bool _isLicenseValid = true;
    private string _licenseTier = "Enterprise";
    private string _licenseExpiryText = "Valid";
    private string _cashStatusReason = "Disconnected";
    private string _qrStatusReason = "Offline";
    private string _serverStatusReason = "Offline";
    private string _scannerStatusReason = "USB POS & Keyboard Wedge Active";
    private string _printerStatusReason = "Offline / Disconnected";
    private string _printerDeviceName = "Receipt Printer";

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsCashAvailable
    {
        get => _isCashAvailable;
        private set
        {
            if (_isCashAvailable != value)
            {
                _isCashAvailable = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsCashUnavailable));
            }
        }
    }

    public bool IsCashUnavailable => !IsCashAvailable;

    public bool IsQrAvailable
    {
        get => _isQrAvailable;
        private set
        {
            if (_isQrAvailable != value)
            {
                _isQrAvailable = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsQrUnavailable));
            }
        }
    }

    public bool IsQrUnavailable => !IsQrAvailable;

    public bool IsCardAvailable
    {
        get => _isCardAvailable;
        private set
        {
            if (_isCardAvailable != value)
            {
                _isCardAvailable = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsIntlQrAvailable
    {
        get => _isIntlQrAvailable;
        private set
        {
            if (_isIntlQrAvailable != value)
            {
                _isIntlQrAvailable = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsMembershipAvailable
    {
        get => _isMembershipAvailable;
        private set
        {
            if (_isMembershipAvailable != value)
            {
                _isMembershipAvailable = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsCouponAvailable
    {
        get => _isCouponAvailable;
        private set
        {
            if (_isCouponAvailable != value)
            {
                _isCouponAvailable = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsServerOnline
    {
        get => _isServerOnline;
        private set
        {
            if (_isServerOnline != value)
            {
                _isServerOnline = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsServerOffline));
            }
        }
    }

    public bool IsServerOffline => !IsServerOnline;

    public bool IsScannerAvailable
    {
        get => _isScannerAvailable;
        private set
        {
            if (_isScannerAvailable != value)
            {
                _isScannerAvailable = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsPrinterAvailable
    {
        get => _isPrinterAvailable;
        private set
        {
            if (_isPrinterAvailable != value)
            {
                _isPrinterAvailable = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsLicenseValid
    {
        get => _isLicenseValid;
        private set
        {
            if (_isLicenseValid != value)
            {
                _isLicenseValid = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsLicenseInvalid));
            }
        }
    }

    public bool IsLicenseInvalid => !IsLicenseValid;

    public string LicenseTier
    {
        get => _licenseTier;
        private set
        {
            if (_licenseTier != value)
            {
                _licenseTier = value;
                OnPropertyChanged();
            }
        }
    }

    public string LicenseExpiryText
    {
        get => _licenseExpiryText;
        private set
        {
            if (_licenseExpiryText != value)
            {
                _licenseExpiryText = value;
                OnPropertyChanged();
            }
        }
    }

    public string CashStatusReason
    {
        get => _cashStatusReason;
        private set
        {
            if (_cashStatusReason != value)
            {
                _cashStatusReason = value;
                OnPropertyChanged();
            }
        }
    }

    public string QrStatusReason
    {
        get => _qrStatusReason;
        private set
        {
            if (_qrStatusReason != value)
            {
                _qrStatusReason = value;
                OnPropertyChanged();
            }
        }
    }

    public string ServerStatusReason
    {
        get => _serverStatusReason;
        private set
        {
            if (_serverStatusReason != value)
            {
                _serverStatusReason = value;
                OnPropertyChanged();
            }
        }
    }

    private string? _cashDevicePort;
    private string? _scannerPort;

    public string? CashDevicePort
    {
        get => _cashDevicePort;
        private set
        {
            if (_cashDevicePort != value)
            {
                _cashDevicePort = value;
                OnPropertyChanged();
            }
        }
    }

    public string? ScannerPort
    {
        get => _scannerPort;
        private set
        {
            if (_scannerPort != value)
            {
                _scannerPort = value;
                OnPropertyChanged();
            }
        }
    }

    public void SetCashAvailability(bool isAvailable, string? reason = null, string? port = null)
    {
        IsCashAvailable = isAvailable;
        CashStatusReason = reason ?? (isAvailable ? "Online" : "Unavailable");
        CashDevicePort = port;
    }

    public void SetQrAvailability(bool isAvailable, string? reason = null)
    {
        IsQrAvailable = isAvailable;
        QrStatusReason = reason ?? (isAvailable ? "Online" : "Unavailable");
    }

    public void SetCardAvailability(bool isAvailable)
    {
        IsCardAvailable = isAvailable;
    }

    public void SetIntlQrAvailability(bool isAvailable)
    {
        IsIntlQrAvailable = isAvailable;
    }

    public void SetMembershipAvailability(bool isAvailable)
    {
        IsMembershipAvailable = isAvailable;
    }

    public void SetCouponAvailability(bool isAvailable)
    {
        IsCouponAvailable = isAvailable;
    }

    public void SetServerOnline(bool isOnline, string? reason = null)
    {
        IsServerOnline = isOnline;
        ServerStatusReason = reason ?? (isOnline ? "Online" : "Offline");
    }

    public string ScannerStatusReason
    {
        get => _scannerStatusReason;
        private set
        {
            if (_scannerStatusReason != value)
            {
                _scannerStatusReason = value;
                OnPropertyChanged();
            }
        }
    }

    public void SetScannerAvailability(bool isAvailable, string? reason = null, string? port = null)
    {
        IsScannerAvailable = isAvailable;
        ScannerStatusReason = reason ?? (isAvailable ? "USB POS & Keyboard Wedge Active" : "Disconnected");
        ScannerPort = port;
    }

    public string PrinterStatusReason
    {
        get => _printerStatusReason;
        private set
        {
            if (_printerStatusReason != value)
            {
                _printerStatusReason = value;
                OnPropertyChanged();
            }
        }
    }

    public string PrinterDeviceName
    {
        get => _printerDeviceName;
        private set
        {
            if (_printerDeviceName != value)
            {
                _printerDeviceName = value;
                OnPropertyChanged();
            }
        }
    }

    public void SetPrinterAvailability(bool isAvailable, string? reason = null, string? deviceName = null)
    {
        IsPrinterAvailable = isAvailable;
        PrinterStatusReason = reason ?? (isAvailable ? "Online" : "Offline / Disconnected");
        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            PrinterDeviceName = deviceName;
        }
    }

    public void SetLicenseStatus(bool isValid, string tier = "Enterprise", string expiry = "Valid")
    {
        IsLicenseValid = isValid;
        LicenseTier = tier;
        LicenseExpiryText = expiry;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
