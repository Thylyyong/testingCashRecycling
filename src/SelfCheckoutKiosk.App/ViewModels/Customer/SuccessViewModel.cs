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
        public LocalizationService Localizer => LocalizationService.Instance;
        private readonly IReceiptPrinterService _printerService;

        public INavigationService NavigationService { get; }
        public Payment Payment { get; }

        public PaymentMethod Method => Payment.Method;
        public bool IsCash => Method == PaymentMethod.Cash;

        public string TransactionId => Payment.TransactionId;
        public string CompletedAtText => Payment.CompletedAt.ToString("dd MMM yyyy, hh:mm tt");
        public string MethodLabel => Method switch
        {
            PaymentMethod.Cash => Localizer.GetString("CashPaymentMethod"),
            PaymentMethod.KHQR => Localizer.GetString("KHQRPaymentMethod"),
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
            lines.AppendLine("         SELF-CHECKOUT KIOSK          ");
            lines.AppendLine("     AUTHENTIC FOOD & BEVERAGES       ");
            lines.AppendLine("        Phnom Penh, Cambodia          ");
            lines.AppendLine(new string('=', 38));
            lines.AppendLine($"Order: {TransactionId,-16} Type: DINE_IN");
            lines.AppendLine($"Date : {CompletedAtText,-16} Cashier: Kiosk");
            lines.AppendLine(new string('-', 38));
            lines.AppendLine("QTY & ITEM                      AMOUNT");
            lines.AppendLine(new string('-', 38));
            lines.AppendLine($"1x Order Total                 ${Payment.TotalDueUsd,7:F2}");
            lines.AppendLine(new string('-', 38));
            lines.AppendLine($"SUBTOTAL:                      ${Payment.TotalDueUsd,7:F2}");
            lines.AppendLine(new string('-', 38));
            lines.AppendLine($"TOTAL (USD):                   ${Payment.TotalDueUsd,7:F2}");
            lines.AppendLine($"                        {Payment.TotalDueKhr,10:N0} KHR");
            lines.AppendLine($"PAID:                          ${Payment.TotalPaidUsd,7:F2}");

            if (HasChangeDue)
            {
                lines.AppendLine($"CHANGE:                        {Payment.ChangeDueKhr,10:N0} KHR");
            }

            lines.AppendLine(new string('-', 38));
            lines.AppendLine($"PAYMENT METHOD:                 {MethodLabel.ToUpperInvariant()}");
            lines.AppendLine(new string('=', 38));
            lines.AppendLine("      THANK YOU FOR YOUR VISIT!       ");
            lines.AppendLine("          Please Come Again           ");
            lines.AppendLine("         Powered by Kiosk POS         ");

            return lines.ToString();
        }

        public void ReturnHome()
        {
            NavigationService.NavigateTo(typeof(KioskBaseView), null, SlideNavigationTransitionEffect.FromRight);
        }

        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}