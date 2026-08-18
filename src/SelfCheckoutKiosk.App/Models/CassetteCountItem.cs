using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SelfCheckoutKiosk.App.Models
{
    public class CassetteCountItem : INotifyPropertyChanged
    {
        private string _denominationLabel = string.Empty;
        private string _currency = "KHR";
        private int _count = 0;
        private decimal _unitValue = 0;

        public string DenominationLabel
        {
            get => _denominationLabel;
            set { _denominationLabel = value; OnPropertyChanged(); }
        }

        public string Currency
        {
            get => _currency;
            set { _currency = value; OnPropertyChanged(); }
        }

        public int Count
        {
            get => _count;
            set { _count = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalValueText)); OnPropertyChanged(nameof(CountBadgeText)); }
        }

        public decimal UnitValue
        {
            get => _unitValue;
            set { _unitValue = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalValueText)); }
        }

        public string TotalValueText => Currency == "KHR"
            ? $"៛{(UnitValue * Count):N0}"
            : $"${(UnitValue * Count):N2}";

        public string CountBadgeText => $"{Count} notes";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
