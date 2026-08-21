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

        // Formatted specifically for authentic 80mm (79.5mm) thermal paper receipts (48 columns)
        public string BuildVirtualReceiptText()
        {
            const int width = 48;

            var lines = new System.Text.StringBuilder();
            string divider = new string('-', width);
            string doubleDivider = new string('=', width);

            string storeName = "SUPERMARKET EXPRESS";
            string storeTagline = "AUTHENTIC FOOD & GROCERY";
            string storeAddress = "Phnom Penh, Cambodia";

            try
            {
                var branding = MediaBrandingService.Instance.Branding;
                if (!string.IsNullOrWhiteSpace(branding.CompanyName)) storeName = branding.CompanyName;
                if (!string.IsNullOrWhiteSpace(branding.Tagline)) storeTagline = branding.Tagline;
            }
            catch { }

            lines.AppendLine(FormatCenter(storeName, width));
            lines.AppendLine(FormatCenter(storeTagline, width));
            lines.AppendLine(FormatCenter(storeAddress, width));
            lines.AppendLine(doubleDivider);

            lines.AppendLine(FormatRow($"Receipt: {TransactionId}", "Type: KIOSK_POS", width));
            lines.AppendLine(FormatRow($"Date: {Payment.CompletedAt:yyyy-MM-dd HH:mm}", "Cashier: Kiosk #01", width));
            lines.AppendLine(divider);

            lines.AppendLine(FormatRow("QTY & ITEM", "AMOUNT", width));
            lines.AppendLine(divider);

            int totalItems = 0;
            int totalPcs = 0;

            if (Payment.Items != null && Payment.Items.Count > 0)
            {
                foreach (var item in Payment.Items)
                {
                    totalItems++;
                    totalPcs += Math.Max(1, item.Quantity);

                    string name = item.Name.Trim();
                    if (name.Length > width) name = name.Substring(0, width - 1);
                    lines.AppendLine(name);

                    string skuPrefix = !string.IsNullOrWhiteSpace(item.Sku) ? $"{item.Sku} " : "";
                    string calcStr = $"{skuPrefix}{item.Quantity}x ${item.UnitPrice:F2}";
                    string lineTotalStr = $"${item.LineTotal:F2}";
                    lines.AppendLine("  " + FormatRow(calcStr, lineTotalStr, width - 2));
                }
            }
            else
            {
                totalItems = 1;
                totalPcs = 1;
                lines.AppendLine("1x Grocery Basket Total");
                lines.AppendLine("  " + FormatRow($"1x ${Payment.TotalDueUsd:F2}", $"${Payment.TotalDueUsd:F2}", width - 2));
            }

            lines.AppendLine(divider);
            lines.AppendLine(FormatRow($"TOTAL ITEMS: {totalItems}", $"({totalPcs} pcs)", width));
            lines.AppendLine(divider);

            lines.AppendLine(FormatRow("TOTAL DUE (USD):", $"${Payment.TotalDueUsd:F2}", width));
            lines.AppendLine(FormatRow("TOTAL DUE (KHR):", $"{Payment.TotalDueKhr:N0} KHR", width));
            lines.AppendLine(divider);

            decimal paidAmount = Payment.TotalPaidUsd > 0 ? Payment.TotalPaidUsd : Payment.TotalDueUsd;
            lines.AppendLine(FormatRow($"PAID ({MethodLabel.ToUpperInvariant()}):", $"${paidAmount:F2}", width));

            lines.AppendLine(doubleDivider);
            lines.AppendLine(FormatRow("Payment Method:", MethodLabel.ToUpperInvariant(), width));
            lines.AppendLine(FormatRow("Exchange Rate:", $"1 USD = {Payment.ExchangeRate:N0} KHR", width));
            lines.AppendLine(FormatRow("Payment Status:", "PAID & COMPLETED", width));
            lines.AppendLine(divider);

            lines.AppendLine(FormatCenter("THANK YOU FOR SHOPPING WITH US!", width));
            lines.AppendLine(FormatCenter("PLEASE KEEP THIS RECEIPT", width));
            lines.AppendLine(FormatCenter("HAVE A WONDERFUL DAY!", width));
            lines.AppendLine(doubleDivider);

            return lines.ToString();
        }

        private static string FormatRow(string left, string right, int width = 46)
        {
            left ??= string.Empty;
            right ??= string.Empty;

            if (left.Length + right.Length >= width)
            {
                int maxLeft = Math.Max(1, width - right.Length - 1);
                if (left.Length > maxLeft)
                {
                    left = left.Substring(0, maxLeft);
                }
            }

            int spaces = Math.Max(1, width - left.Length - right.Length);
            return left + new string(' ', spaces) + right;
        }

        private static string FormatCenter(string text, int width = 46)
        {
            text ??= string.Empty;
            if (text.Length >= width) return text.Substring(0, width);
            int leftPad = Math.Max(0, (width - text.Length) / 2);
            return new string(' ', leftPad) + text;
        }

        public void ReturnHome()
        {
            NavigationService.NavigateTo(typeof(KioskBaseView), null, SlideNavigationTransitionEffect.FromRight);
        }

        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}