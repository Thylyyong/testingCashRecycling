using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SelfCheckoutKiosk.App.Models
{
    public class HardwareDeviceStatus : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _deviceType = string.Empty;
        private bool _isConnected = false;
        private string _portOrInterface = "USB / Serial";
        private string _firmwareVersion = "v1.0";
        private string _glyph = "\uE770";

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public string DeviceType
        {
            get => _deviceType;
            set { _deviceType = value; OnPropertyChanged(); }
        }

        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                _isConnected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusColor));
            }
        }

        public string PortOrInterface
        {
            get => _portOrInterface;
            set { _portOrInterface = value; OnPropertyChanged(); }
        }

        public string FirmwareVersion
        {
            get => _firmwareVersion;
            set { _firmwareVersion = value; OnPropertyChanged(); }
        }

        public string Glyph
        {
            get => _glyph;
            set { _glyph = value; OnPropertyChanged(); }
        }

        public string StatusText => IsConnected ? "Connected" : "Disconnected";

        public string StatusColor => IsConnected ? "#059669" : "#DC2626";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
