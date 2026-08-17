using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SelfCheckoutKiosk.App.Models
{
    public class CartItem : INotifyPropertyChanged
    {
        public event EventHandler? ItemUpdated;
        private int _quantity = 1;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Sku { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public decimal UnitPrice { get; set; }

        public int Quantity
        {
            get => _quantity;
            set
            {
                if (_quantity != value)
                {
                    _quantity = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(LineTotal));

                    ItemUpdated?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public decimal LineTotal => UnitPrice * Quantity;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}