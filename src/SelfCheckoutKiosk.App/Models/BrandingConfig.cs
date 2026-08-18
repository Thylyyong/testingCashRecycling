using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SelfCheckoutKiosk.App.Models
{
    public class BrandingConfig : INotifyPropertyChanged
    {
        private string _companyName = "CA Solution";
        private string _tagline = "Skip the line. Scan & Go. Self-Checkout";
        private string _logoFileName = "branding-logo.png";
        private string _kioskId = "KIOSK-PHNOM-PENH-01";

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
            set { _logoFileName = value; OnPropertyChanged(); }
        }

        public string KioskId
        {
            get => _kioskId;
            set { _kioskId = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
