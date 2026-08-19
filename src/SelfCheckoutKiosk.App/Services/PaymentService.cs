using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using SelfCheckoutKiosk.App.Models;
using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Domain.ValueObjects;
using PaymentMethod = SelfCheckoutKiosk.App.Models.PaymentMethod;

namespace SelfCheckoutKiosk.App.Services
{
    public class PaymentService : IPaymentService
    {
        public LocalizationService Localizer => LocalizationService.Instance;
        private const decimal MaxOverpayKhr = 500m; // Up to 500 KHR overpayment tolerance

        private decimal _totalDueUsd;
        private decimal _totalPaidUsd;
        private decimal _exchangeRate = 4100m;
        private bool _isHardwareArmed = false;

        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<Payment>? PaymentConfirmed;

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

        public void BeginTransaction(decimal totalDueUsd, decimal exchangeRate, bool armCashHardware = false)
        {
            ExchangeRate = exchangeRate;
            TotalDueUsd = totalDueUsd;
            TotalPaidUsd = 0;
            HasAcceptedAnyPayment = false;
            Attempts.Clear();
            OnPropertyChanged(nameof(HasAcceptedAnyPayment));

            if (armCashHardware)
            {
                ArmCashHardware();
            }
            else
            {
                DisarmCashHardware();
            }
        }

        public void ArmCashHardware()
        {
            if (_isHardwareArmed) return;

            if (App.Services?.CashRecycler != null)
            {
                _isHardwareArmed = true;
                App.Services.CashRecycler.OnNoteInEscrow -= OnHardwareNoteInEscrow;
                App.Services.CashRecycler.OnNoteInEscrow += OnHardwareNoteInEscrow;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await App.Services.CashRecycler.ConnectAsync();
                        await App.Services.CashRecycler.ArmAcceptanceAsync();
                        Debug.WriteLine("[CASH] Cash recycler armed and ready for physical notes on COM8.");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[CASH ERROR] Could not arm cash recycler: {ex.Message}");
                    }
                });
            }
        }

        public void DisarmCashHardware()
        {
            if (!_isHardwareArmed) return;
            _isHardwareArmed = false;

            if (App.Services?.CashRecycler != null)
            {
                App.Services.CashRecycler.OnNoteInEscrow -= OnHardwareNoteInEscrow;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await App.Services.CashRecycler.DisarmAcceptanceAsync();
                        Debug.WriteLine("[CASH] Cash recycler disarmed (Intake shutter closed & LED off).");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[CASH ERROR] Could not disarm cash recycler: {ex.Message}");
                    }
                });
            }
        }

        private void OnHardwareNoteInEscrow(object? sender, NoteInEscrowEventArgs e)
        {
            bool isUsd = e.Note.Currency == CurrencyCode.Usd;
            decimal noteAmt = e.Note.Amount;
            decimal noteAmtUsd = isUsd ? noteAmt : noteAmt / ExchangeRate;
            decimal maxOverpayUsd = MaxOverpayKhr / ExchangeRate;
            decimal projectedTotal = TotalPaidUsd + noteAmtUsd;

            if (projectedTotal > TotalDueUsd + maxOverpayUsd)
            {
                // Physically reject the banknote back to the customer
                _ = App.Services?.CashRecycler?.RejectEscrowedNoteAsync();

                string reason = isUsd
                    ? $"Note ${noteAmt:0.00} exceeds overpayment limit (max +500 KHR). Please insert a smaller note."
                    : $"Note ៛{noteAmt:N0} exceeds overpayment limit (max +500 KHR). Please insert a smaller note.";

                RunOnUIThread(() =>
                {
                    RecordAttempt(PaymentAttemptResult.Rejected, reason, noteAmt, isUsd);
                });
            }
            else
            {
                // Banknote is accepted and vaulted
                RunOnUIThread(() =>
                {
                    TotalPaidUsd = projectedTotal;
                    HasAcceptedAnyPayment = true;
                    OnPropertyChanged(nameof(HasAcceptedAnyPayment));

                    string reason = Localizer.GetString("Accepted") ?? "Accepted";
                    RecordAttempt(PaymentAttemptResult.Accepted, reason, noteAmt, isUsd);

                    if (IsFullyPaid)
                    {
                        DisarmCashHardware();
                        var payment = ConfirmPayment(PaymentMethod.Cash);
                        if (payment != null)
                        {
                            PaymentConfirmed?.Invoke(this, payment);
                        }
                    }
                });
            }
        }

        private void RunOnUIThread(Action action)
        {
            var queue = App.MainWindowInstance?.DispatcherQueue;
            if (queue != null && !queue.HasThreadAccess)
            {
                queue.TryEnqueue(() => action());
            }
            else
            {
                action();
            }
        }

        public bool TrySubmitCash(decimal amount, bool isUsd, out string reason)
        {
            if (IsFullyPaid)
            {
                reason = Localizer.GetString("PaymentAlreadyComplete") ?? "Payment already complete";
                RecordAttempt(PaymentAttemptResult.Rejected, reason, amount, isUsd);
                return false;
            }

            if (amount <= 0)
            {
                reason = Localizer.GetString("InvalidAmount") ?? "Invalid amount";
                RecordAttempt(PaymentAttemptResult.Rejected, reason, amount, isUsd);
                return false;
            }

            decimal amountUsd = isUsd ? amount : amount / ExchangeRate;
            decimal maxOverpayUsd = MaxOverpayKhr / ExchangeRate;
            decimal projectedTotal = TotalPaidUsd + amountUsd;

            if (projectedTotal > TotalDueUsd + maxOverpayUsd)
            {
                reason = isUsd
                    ? $"Note ${amount:0.00} would overpay by more than 500 KHR. Please use a smaller note."
                    : $"Note ៛{amount:N0} would overpay by more than 500 KHR. Please use a smaller note.";
                RecordAttempt(PaymentAttemptResult.Rejected, reason, amount, isUsd);
                return false;
            }

            TotalPaidUsd = projectedTotal;
            HasAcceptedAnyPayment = true;
            OnPropertyChanged(nameof(HasAcceptedAnyPayment));

            reason = Localizer.GetString("Accepted") ?? "Accepted";
            RecordAttempt(PaymentAttemptResult.Accepted, reason, amount, isUsd);
            return true;
        }

        public Payment? ConfirmPayment(PaymentMethod method = PaymentMethod.Cash)
        {
            if (!IsFullyPaid) return null;

            DisarmCashHardware();

            return new Payment
            {
                Method = method,
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
            DisarmCashHardware();
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