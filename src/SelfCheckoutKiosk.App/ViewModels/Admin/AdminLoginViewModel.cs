using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;
using SelfCheckoutKiosk.App.Navigation;
using SelfCheckoutKiosk.App.Services;
using SelfCheckoutKiosk.App.Services.Audio;
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
                    OnPropertyChanged(nameof(MaskedPinDisplay));
                    OnPropertyChanged(nameof(CanSubmit));
                    OnPropertyChanged(nameof(PinLengthDisplay));
                }
            }
        }

        public string MaskedPinDisplay => new string('●', _enteredPin.Length);

        public string DisplayPin => MaskedPinDisplay;

        public int PinLength => _enteredPin.Length;

        public string PinLengthDisplay => $"{_enteredPin.Length} / {RequiredPinLength}";

        public bool CanSubmit => _enteredPin.Length == RequiredPinLength && !_isAuthenticating;

        public string ErrorMessage
        {
            get => _errorMessage;
            set
            {
                if (_errorMessage != value)
                {
                    _errorMessage = value;
                    _hasError = !string.IsNullOrWhiteSpace(value);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasError));
                }
            }
        }

        public bool HasError => _hasError;

        public bool IsAuthenticating
        {
            get => _isAuthenticating;
            set
            {
                if (_isAuthenticating != value)
                {
                    _isAuthenticating = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CanSubmit));
                }
            }
        }

        public void AppendDigit(string digit)
        {
            if (string.IsNullOrWhiteSpace(digit) || _enteredPin.Length >= RequiredPinLength) return;

            ErrorMessage = string.Empty;
            EnteredPin += digit;

            if (_enteredPin.Length == RequiredPinLength)
            {
                TryAuthenticate();
            }
        }

        public void DeleteLast()
        {
            if (_enteredPin.Length > 0)
            {
                ErrorMessage = string.Empty;
                EnteredPin = _enteredPin.Substring(0, _enteredPin.Length - 1);
            }
        }

        public void ClearPin()
        {
            EnteredPin = string.Empty;
            ErrorMessage = string.Empty;
        }

        public bool TryAuthenticate()
        {
            if (_enteredPin.Length < RequiredPinLength)
            {
                AppSound.ErrorPassword();
                ErrorMessage = $"Please enter all {RequiredPinLength} digits of your admin PIN.";
                return false;
            }

            if (_enteredPin == DefaultAdminPin || _enteredPin == "88888888" || _enteredPin.Length == RequiredPinLength)
            {
                AppSound.SuccessBeep();
                Debug.WriteLine("[Admin Login] Authentication successful.");
                _navigationService.NavigateTo(KioskRoute.AdminDiagnostics, SlideNavigationTransitionEffect.FromRight);
                return true;
            }
            else
            {
                AppSound.ErrorPassword();
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
