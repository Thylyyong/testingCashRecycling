using Microsoft.UI.Xaml.Media.Animation;
using SelfCheckoutKiosk.App.Models;
using SelfCheckoutKiosk.App.Services;
using SelfCheckoutKiosk.App.Views.Customer;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace SelfCheckoutKiosk.App.ViewModels.Customer
{
    public class SuccessViewModel : INotifyPropertyChanged
    {
        private readonly IReceiptPrinterService _printerService;

        public INavigationService NavigationService { get; }
        public Payment Payment { get; }

        public PaymentMethod Method => Payment.Method;
        public bool IsCash => Method == PaymentMethod.Cash;

        public string TransactionId => Payment.TransactionId;
        public string CompletedAtText => Payment.CompletedAt.ToString("dd MMM yyyy, hh:mm tt");
        public string MethodLabel => Method switch
        {
            PaymentMethod.Cash => "Cash Payment",
            _ => Method.ToString()
        };

        public string FormattedTotalDue => $"${Payment.TotalDueUsd:0.00}  (≈ ៛{Payment.TotalDueKhr:N0})";
        public string FormattedTotalPaid => $"${Payment.TotalPaidUsd:0.00}  (≈ ៛{Payment.TotalPaidKhr:N0})";
        public string FormattedChangeDue => $"${Payment.ChangeDueUsd:0.00}  (≈ ៛{Payment.ChangeDueKhr:N0})";

        public bool HasChangeDue => Payment.ChangeDueUsd > 0;

        public int AcceptedAttemptCount => Payment.Attempts.Count(a => a.Result == PaymentAttemptResult.Accepted);
        public int RejectedAttemptCount => Payment.Attempts.Count(a => a.Result == PaymentAttemptResult.Rejected);

        // Line items for the printed/virtual receipt — accepted notes only, in the order they were inserted
        public List<PaymentAttempt> AcceptedAttempts =>
            Payment.Attempts
                .Where(a => a.Result == PaymentAttemptResult.Accepted)
                .OrderBy(a => a.Timestamp)
                .ToList();

        public event PropertyChangedEventHandler? PropertyChanged;

        public SuccessViewModel(INavigationService navigationService, IReceiptPrinterService printerService, Payment payment)
        {
            NavigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
            _printerService = printerService ?? throw new ArgumentNullException(nameof(printerService));
            Payment = payment ?? throw new ArgumentNullException(nameof(payment));
        }

        public void PrintReceipt()
        {
            _printerService.Print(Payment);
        }

        // Formatted specifically for authentic thermal paper receipts
        public string BuildVirtualReceiptText()
        {
            var lines = new System.Text.StringBuilder();
            lines.AppendLine("      SELF-CHECKOUT KIOSK       ");
            lines.AppendLine("       OFFICIAL RECEIPT        ");
            lines.AppendLine(new string('=', 32));
            lines.AppendLine($"Txn ID:   {TransactionId}");
            lines.AppendLine($"Date:     {CompletedAtText}");
            lines.AppendLine(new string('-', 32));
            lines.AppendLine($"Method:   {MethodLabel}");
            lines.AppendLine($"Due:      {FormattedTotalDue}");
            lines.AppendLine($"Paid:     {FormattedTotalPaid}");

            if (HasChangeDue)
                lines.AppendLine($"Change:   {FormattedChangeDue}");

            lines.AppendLine(new string('-', 32));
            lines.AppendLine("ACCEPTED TENDER:");

            foreach (var attempt in AcceptedAttempts)
            {
                lines.AppendLine($" [{attempt.Timestamp:HH:mm:ss}]  {attempt.DisplayAmount,16}");
            }

            lines.AppendLine(new string('=', 32));
            lines.AppendLine("  Thank you for shopping with us! ");

            return lines.ToString();
        }

        public void ReturnHome()
        {
            NavigationService.NavigateTo(typeof(KioskBaseView2), null, SlideNavigationTransitionEffect.FromLeft);
        }

        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}