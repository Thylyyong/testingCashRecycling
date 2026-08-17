using System.Collections.ObjectModel;
using System.ComponentModel;
using SelfCheckoutKiosk.App.Models;

namespace SelfCheckoutKiosk.App.Services
{
    public interface IPaymentService : INotifyPropertyChanged
    {
        ObservableCollection<PaymentAttempt> Attempts { get; }

        decimal TotalDueUsd { get; }
        decimal TotalPaidUsd { get; }
        decimal RemainingDueUsd { get; }
        decimal ExchangeRate { get; }

        bool IsFullyPaid { get; }
        bool HasAcceptedAnyPayment { get; }

        void BeginTransaction(decimal totalDueUsd, decimal exchangeRate);
        bool TrySubmitCash(decimal amount, bool isUsd, out string reason);
        Payment? ConfirmPayment();
        void ResetTransaction();
    }
}