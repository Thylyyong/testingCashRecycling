using Microsoft.UI.Xaml.Media.Animation;
using SelfCheckoutKiosk.App.Models;
using SelfCheckoutKiosk.App.Services;
using SelfCheckoutKiosk.App.Views.Customer;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace SelfCheckoutKiosk.App.ViewModels.Customer
{
    public class IngestionProgressViewModel : INotifyPropertyChanged
    {
        private readonly ICartService _cartService;
        private readonly IPaymentService _paymentService;

        public INavigationService NavigationService { get; }

        public ObservableCollection<PaymentAttempt> Attempts => _paymentService.Attempts;

        public decimal TotalDueUsd => _paymentService.TotalDueUsd;
        public decimal TotalDueKhr => TotalDueUsd * ExchangeRate;
        public decimal TotalPaidUsd => _paymentService.TotalPaidUsd;
        public decimal TotalPaidKhr => TotalPaidUsd * ExchangeRate;
        public decimal RemainingDueUsd => _paymentService.RemainingDueUsd;
        public decimal RemainingDueKhr => RemainingDueUsd * ExchangeRate;
        public decimal ExchangeRate => _paymentService.ExchangeRate;

        public bool IsFullyPaid => _paymentService.IsFullyPaid;
        public bool CanNavigateBack => !_paymentService.HasAcceptedAnyPayment;

        public string FormattedTotalDue => $"${TotalDueUsd:0.00}  (≈ ៛{TotalDueKhr:N0})";
        public string FormattedTotalPaid => $"${TotalPaidUsd:0.00}  (≈ ៛{TotalPaidKhr:N0})";
        public string FormattedRemaining => $"${RemainingDueUsd:0.00}  (≈ ៛{RemainingDueKhr:N0})";

        public event PropertyChangedEventHandler? PropertyChanged;

        public IngestionProgressViewModel(
            INavigationService navigationService,
            ICartService cartService,
            IPaymentService paymentService)
        {
            NavigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
            _cartService = cartService ?? throw new ArgumentNullException(nameof(cartService));
            _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));

            _paymentService.PropertyChanged += OnPaymentServicePropertyChanged;
        }

        // Called from OnNavigatedTo — snapshots the cart total into the payment service
        public void InitializeTransaction()
        {
            _paymentService.BeginTransaction(_cartService.TotalUsd, _cartService.ExchangeRate);
            RaiseAllChanged();
        }

        public bool SubmitCash(decimal amount, bool isUsd, out string reason)
        {
            return _paymentService.TrySubmitCash(amount, isUsd, out reason);
        }

        public void ConfirmPaymentAndProceed()
        {
            var payment = _paymentService.ConfirmPayment(PaymentMethod.Cash);
            if (payment == null) return;

            _cartService.ClearCart();
            NavigationService.NavigateTo(typeof(SuccessView), payment, SlideNavigationTransitionEffect.FromRight);
        }

        public void NavigateBackToPaymentSelection()
        {
            if (!CanNavigateBack) return;
            NavigationService.NavigateTo(typeof(PaymentSelectionView), null, SlideNavigationTransitionEffect.FromLeft);
        }

        private void OnPaymentServicePropertyChanged(object? sender, PropertyChangedEventArgs e) => RaiseAllChanged();

        private void RaiseAllChanged()
        {
            OnPropertyChanged(nameof(TotalDueUsd));
            OnPropertyChanged(nameof(TotalDueKhr));
            OnPropertyChanged(nameof(TotalPaidUsd));
            OnPropertyChanged(nameof(TotalPaidKhr));
            OnPropertyChanged(nameof(RemainingDueUsd));
            OnPropertyChanged(nameof(RemainingDueKhr));
            OnPropertyChanged(nameof(IsFullyPaid));
            OnPropertyChanged(nameof(CanNavigateBack));
            OnPropertyChanged(nameof(FormattedTotalDue));
            OnPropertyChanged(nameof(FormattedTotalPaid));
            OnPropertyChanged(nameof(FormattedRemaining));
        }

        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}