using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SelfCheckoutKiosk.App.Models;

namespace SelfCheckoutKiosk.App.Services
{
    public class PaymentService : IPaymentService
    {
        private const decimal MaxOverpayKhr = 100m;

        private decimal _totalDueUsd;
        private decimal _totalPaidUsd;
        private decimal _exchangeRate = 4100m;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<PaymentAttempt> Attempts { get; } = new();

        public decimal TotalDueUsd
        {
            get => _totalDueUsd;
            private set
            {
                if (_totalDueUsd == value) return;
                _totalDueUsd = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RemainingDueUsd));
                OnPropertyChanged(nameof(IsFullyPaid));
            }
        }

        public decimal TotalPaidUsd
        {
            get => _totalPaidUsd;
            private set
            {
                if (_totalPaidUsd == value) return;
                _totalPaidUsd = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RemainingDueUsd));
                OnPropertyChanged(nameof(IsFullyPaid));
            }
        }

        public decimal ExchangeRate
        {
            get => _exchangeRate;
            private set
            {
                if (_exchangeRate == value) return;
                _exchangeRate = value;
                OnPropertyChanged();
            }
        }

        public decimal RemainingDueUsd => Math.Max(0, TotalDueUsd - TotalPaidUsd);

        public bool IsFullyPaid => TotalDueUsd > 0 && TotalPaidUsd >= TotalDueUsd;

        public bool HasAcceptedAnyPayment { get; private set; }

        public void BeginTransaction(decimal totalDueUsd, decimal exchangeRate)
        {
            ExchangeRate = exchangeRate;
            TotalDueUsd = totalDueUsd;
            TotalPaidUsd = 0;
            HasAcceptedAnyPayment = false;
            Attempts.Clear();
            OnPropertyChanged(nameof(HasAcceptedAnyPayment));
        }

        public bool TrySubmitCash(decimal amount, bool isUsd, out string reason)
        {
            // Already fully paid — different message than "note too big"
            if (IsFullyPaid)
            {
                reason = "Payment already complete.";
                RecordAttempt(PaymentAttemptResult.Rejected, reason, amount, isUsd);
                return false;
            }

            if (amount <= 0)
            {
                reason = "Invalid amount.";
                RecordAttempt(PaymentAttemptResult.Rejected, reason, amount, isUsd);
                return false;
            }

            decimal amountUsd = isUsd ? amount : amount / ExchangeRate;
            decimal maxOverpayUsd = MaxOverpayKhr / ExchangeRate;
            decimal projectedTotal = TotalPaidUsd + amountUsd;

            if (projectedTotal > TotalDueUsd + maxOverpayUsd)
            {
                reason = "Note too large — try a smaller note.";
                RecordAttempt(PaymentAttemptResult.Rejected, reason, amount, isUsd);
                return false;
            }

            TotalPaidUsd = projectedTotal;
            HasAcceptedAnyPayment = true;
            OnPropertyChanged(nameof(HasAcceptedAnyPayment));

            reason = "Accepted";
            RecordAttempt(PaymentAttemptResult.Accepted, reason, amount, isUsd);
            return true;
        }

        public Payment? ConfirmPayment()
        {
            if (!IsFullyPaid) return null;

            return new Payment
            {
                Method = PaymentMethod.Cash,
                TotalDueUsd = TotalDueUsd,
                TotalPaidUsd = TotalPaidUsd,
                ChangeDueUsd = Math.Max(0, TotalPaidUsd - TotalDueUsd),
                ExchangeRate = ExchangeRate,
                IsFullyPaid = true,
                Attempts = new List<PaymentAttempt>(Attempts)
            };
        }

        public void ResetTransaction()
        {
            TotalDueUsd = 0;
            TotalPaidUsd = 0;
            HasAcceptedAnyPayment = false;
            Attempts.Clear();
        }

        private void RecordAttempt(PaymentAttemptResult result, string reason, decimal amount, bool isUsd)
        {
            Attempts.Insert(0, new PaymentAttempt
            {
                Result = result,
                Reason = reason,
                Amount = amount,
                IsUsd = isUsd
            });
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}