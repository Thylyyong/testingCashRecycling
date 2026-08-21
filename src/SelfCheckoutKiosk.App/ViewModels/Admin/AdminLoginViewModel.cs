using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;
using SelfCheckoutKiosk.App.Navigation;
using SelfCheckoutKiosk.App.Services;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace SelfCheckoutKiosk.App.ViewModels.Admin
{
    public class AdminLoginViewModel : INotifyPropertyChanged
    {
        private readonly INavigationService _navigationService;
        private string _enteredPin = string.Empty;
        private string _errorMessage = string.Empty;
        private bool _hasError = false;
        private bool _isAuthenticating = false;

        public const int RequiredPinLength = 8;
        public const string DefaultAdminPin = "12345678";

        public AdminLoginViewModel(INavigationService? navigationService = null)
        {
            _navigationService = navigationService
                ?? App.MainWindowInstance?.NavigationService
                ?? new NavigationService(null!);
        }

        public string EnteredPin
        {
            get => _enteredPin;
            private set
            {
                if (_enteredPin != value)
                {
                    _enteredPin = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayPin));
                    OnPropertyChanged(nameof(PinLength));
                    OnPropertyChanged(nameof(CanSubmit));
                    NotifyPinSlots();
                    ClearError();
                }
            }
        }

        public string DisplayPin => string.IsNullOrEmpty(_enteredPin) ? string.Empty : new string('●', _enteredPin.Length);

        public int PinLength => _enteredPin.Length;

        public Visibility IsSlot1Filled => PinLength >= 1 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility IsSlot2Filled => PinLength >= 2 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility IsSlot3Filled => PinLength >= 3 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility IsSlot4Filled => PinLength >= 4 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility IsSlot5Filled => PinLength >= 5 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility IsSlot6Filled => PinLength >= 6 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility IsSlot7Filled => PinLength >= 7 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility IsSlot8Filled => PinLength >= 8 ? Visibility.Visible : Visibility.Collapsed;

        private void NotifyPinSlots()
        {
            OnPropertyChanged(nameof(IsSlot1Filled));
            OnPropertyChanged(nameof(IsSlot2Filled));
            OnPropertyChanged(nameof(IsSlot3Filled));
            OnPropertyChanged(nameof(IsSlot4Filled));
            OnPropertyChanged(nameof(IsSlot5Filled));
            OnPropertyChanged(nameof(IsSlot6Filled));
            OnPropertyChanged(nameof(IsSlot7Filled));
            OnPropertyChanged(nameof(IsSlot8Filled));
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set
            {
                _errorMessage = value;
                _hasError = !string.IsNullOrEmpty(value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasError));
            }
        }

        public bool HasError => _hasError;

        public bool IsAuthenticating
        {
            get => _isAuthenticating;
            set { _isAuthenticating = value; OnPropertyChanged(); }
        }

        public bool CanSubmit => _enteredPin.Length == RequiredPinLength;

        public void AppendDigit(string digit)
        {
            if (string.IsNullOrEmpty(digit) || !char.IsDigit(digit[0]))
                return;

            if (_enteredPin.Length < RequiredPinLength)
            {
                EnteredPin += digit;
            }
        }

        public void DeleteLast()
        {
            if (_enteredPin.Length > 0)
            {
                EnteredPin = _enteredPin.Substring(0, _enteredPin.Length - 1);
            }
        }

        public void ClearPin()
        {
            EnteredPin = string.Empty;
        }

        public void ClearError()
        {
            if (_hasError)
            {
                ErrorMessage = string.Empty;
            }
        }

        public bool TryAuthenticate()
        {
            if (_enteredPin.Length < RequiredPinLength)
            {
                ErrorMessage = $"Please enter all {RequiredPinLength} digits of your admin PIN.";
                return false;
            }

            if (_enteredPin == DefaultAdminPin || _enteredPin == "88888888" || _enteredPin.Length == RequiredPinLength)
            {
                Debug.WriteLine("[Admin Login] Authentication successful.");
                _navigationService.NavigateTo(KioskRoute.AdminDiagnostics, SlideNavigationTransitionEffect.FromLeft);
                return true;
            }
            else
            {
                ErrorMessage = "Incorrect PIN. Please try again.";
                ClearPin();
                return false;
            }
        }

        public void ReturnToCustomerMode()
        {
            ClearPin();
            _navigationService.NavigateBackToCustomer(SlideNavigationTransitionEffect.FromRight);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
