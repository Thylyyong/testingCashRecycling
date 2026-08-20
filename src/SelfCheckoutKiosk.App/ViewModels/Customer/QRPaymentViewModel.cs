using System;
using System.ComponentModel;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using SelfCheckoutKiosk.App.Models;
using SelfCheckoutKiosk.App.Services;
using SelfCheckoutKiosk.App.Views.Customer;

namespace SelfCheckoutKiosk.App.ViewModels.Customer
{
    public class QRPaymentViewModel : INotifyPropertyChanged
    {
        private readonly ICartService _cartService;
        private readonly IPaymentService _paymentService;

        private BitmapImage? _qrCodeImageSource;
        private bool _isQrLoading = true;

        public INavigationService NavigationService { get; }

        // Dual-currency properties delegating to services (matching IngestionProgressViewModel)
        public decimal TotalDueUsd => _paymentService.HasAcceptedAnyPayment 
            ? _paymentService.RemainingDueUsd 
            : (_paymentService.TotalDueUsd > 0 ? _paymentService.TotalDueUsd : _cartService.TotalUsd);

        public decimal ExchangeRate => _paymentService.ExchangeRate > 0 ? _paymentService.ExchangeRate : _cartService.ExchangeRate;
        public decimal TotalDueKhr => SelfCheckoutKiosk.Core.Currency.DualCurrencyCalculator.CalculateTotalKhr(TotalDueUsd, ExchangeRate);

        // Formatted display string matching kiosk dual-currency standard
        public string FormattedTotalDue => $"${TotalDueUsd:0.00}";

        public string TotalDueLabelText => _paymentService.HasAcceptedAnyPayment
            ? "Remaining Balance"
            : "Total Amount";

        public BitmapImage? QrCodeImageSource
        {
            get => _qrCodeImageSource;
            private set
            {
                if (_qrCodeImageSource != value)
                {
                    _qrCodeImageSource = value;
                    OnPropertyChanged(nameof(QrCodeImageSource));
                }
            }
        }

        public bool IsQrLoading
        {
            get => _isQrLoading;
            private set
            {
                if (_isQrLoading != value)
                {
                    _isQrLoading = value;
                    OnPropertyChanged(nameof(IsQrLoading));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public QRPaymentViewModel(
            INavigationService navigationService,
            ICartService cartService,
            IPaymentService paymentService)
        {
            NavigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
            _cartService = cartService ?? throw new ArgumentNullException(nameof(cartService));
            _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));

            _paymentService.PropertyChanged += OnPaymentServicePropertyChanged;
        }

        // Called from OnNavigatedTo — snapshots the cart total into the payment service if not already in an active transaction
        public void InitializeTransaction()
        {
            if (!_paymentService.HasAcceptedAnyPayment && _paymentService.TotalDueUsd <= 0)
            {
                _paymentService.BeginTransaction(_cartService.TotalUsd, _cartService.ExchangeRate, isCash: false);
            }
            GenerateQrCodePayload();
            RaiseAllChanged();
        }

        public void GenerateQrCodePayload()
        {
            IsQrLoading = true;

            try
            {
                // Mock QR Uri for testing (replace with actual payment service KHQR payload generator when ready)
                var dummyQrUri = new Uri("https://api.qrserver.com/v1/create-qr-code/?size=300x300&data=KHQR_ABA_MOCK_PAYMENT");
                QrCodeImageSource = new BitmapImage(dummyQrUri);
            }
            catch
            {
                QrCodeImageSource = null;
            }
            finally
            {
                IsQrLoading = false;
            }
        }

        // Simulates completing payment, finalizing order, clearing cart, and navigating to PaymentSuccessView
        public void SimulatePaymentSuccessAndProceed()
        {
            decimal amountToPay = _paymentService.RemainingDueUsd > 0 ? _paymentService.RemainingDueUsd : TotalDueUsd;
            _paymentService.TrySubmitCash(amountToPay, true, out _);
            var payment = _paymentService.ConfirmPayment(PaymentMethod.KHQR);

            _cartService.ClearCart();
            NavigationService.NavigateTo(typeof(SuccessView), payment, SlideNavigationTransitionEffect.FromRight);
        }

        public void NavigateBackToPaymentSelection()
        {
            NavigationService.NavigateTo(typeof(PaymentSelectionView), null, SlideNavigationTransitionEffect.FromLeft);
        }

        private void OnPaymentServicePropertyChanged(object? sender, PropertyChangedEventArgs e) => RaiseAllChanged();

        private void RaiseAllChanged()
        {
            OnPropertyChanged(nameof(TotalDueUsd));
            OnPropertyChanged(nameof(TotalDueKhr));
            OnPropertyChanged(nameof(ExchangeRate));
            OnPropertyChanged(nameof(FormattedTotalDue));
        }

        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}