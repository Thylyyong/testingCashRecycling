using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace SelfCheckoutKiosk.App.Models
{
    public class BrandingConfig : INotifyPropertyChanged
    {
        private string _companyName = "CA Solution";
        private string _tagline = "Scan & Go Self-Checkout";
        private string _logoFileName = "ca.ico";
        private string _kioskId = "KIOSK-01";
        private string _storeHours = "Open until 10:00 PM";

        public string CompanyName
        {
            get => _companyName;
            set { _companyName = value; OnPropertyChanged(); }
        }

        public string Tagline
        {
            get => _tagline;
            set { _tagline = value; OnPropertyChanged(); }
        }

        public string LogoFileName
        {
            get => _logoFileName;
            set
            {
                _logoFileName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LogoUri));
            }
        }

        public string KioskId
        {
            get => _kioskId;
            set { _kioskId = value; OnPropertyChanged(); }
        }

        public string StoreHours
        {
            get => _storeHours;
            set { _storeHours = value; OnPropertyChanged(); }
        }

        public string LogoUri
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_logoFileName))
                    return "ms-appx:///Assets/Logo/ca.ico";

                if (_logoFileName.StartsWith("ms-appx://", StringComparison.OrdinalIgnoreCase) ||
                    _logoFileName.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
                    _logoFileName.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    _logoFileName.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    return _logoFileName;
                }

                if (File.Exists(_logoFileName))
                {
                    return new Uri(_logoFileName).AbsoluteUri;
                }

                return $"ms-appx:///Assets/Logo/{_logoFileName}";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
