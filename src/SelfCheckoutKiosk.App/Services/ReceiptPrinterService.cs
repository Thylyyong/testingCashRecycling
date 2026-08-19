using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SelfCheckoutKiosk.App.Models;
using SelfCheckoutKiosk.Hal.Vendor.EpsonM30;

namespace SelfCheckoutKiosk.App.Services
{
    public class ReceiptPrinterService : IReceiptPrinterService
    {
        public void Print(Payment payment)
        {
            if (payment == null) return;

            decimal overpayUsd = Math.Max(0m, payment.TotalPaidUsd - payment.TotalDueUsd);
            decimal overpayKhr = Math.Round(overpayUsd * payment.ExchangeRate, 0);

            _ = Task.Run(async () =>
            {
                try
                {
                    var halPrinter = App.Services?.ReceiptPrinter as EpsonReceiptPrinter 
                        ?? new EpsonReceiptPrinter("EPSON EU-m30");

                    await halPrinter.ConnectAsync();

                    string methodLabel = payment.Method switch
                    {
                        PaymentMethod.KHQR => "KHQR Digital (Bakong / ABA)",
                        PaymentMethod.Card => "Credit/Debit Card",
                        PaymentMethod.InternationalQR => "International QR",
                        _ => "Cash (USD/KHR)"
                    };

                    var sb = new StringBuilder();
                    sb.Append("\x1B\x21\x30").AppendLine("SELF-CHECKOUT KIOSK").Append("\x1B\x21\x00");
                    sb.AppendLine("Phnom Penh, Cambodia");
                    sb.AppendLine("========================================");
                    sb.AppendLine($"Date   : {payment.CompletedAt:yyyy-MM-dd  HH:mm:ss}");
                    sb.AppendLine($"Receipt: {payment.TransactionId}");
                    sb.AppendLine($"Method : {methodLabel}");
                    sb.AppendLine("----------------------------------------");
                    sb.AppendLine($"Due    : ${payment.TotalDueUsd:F2} USD  ({payment.TotalDueKhr:N0} KHR)");
                    sb.AppendLine($"Paid   : ${payment.TotalPaidUsd:F2} USD");
                    if (overpayKhr > 0 && overpayKhr <= 500m)
                    {
                        sb.AppendLine($"Change : ៛{overpayKhr:N0} KHR (within 500 KHR tolerance)");
                    }
                    sb.AppendLine("----------------------------------------");

                    var acceptedNotes = payment.Attempts
                        .Where(a => a.Result == PaymentAttemptResult.Accepted)
                        .ToList();

                    if (acceptedNotes.Count > 0)
                    {
                        sb.AppendLine("Accepted Payments:");
                        foreach (var attempt in acceptedNotes)
                        {
                            sb.AppendLine($"  {attempt.DisplayAmount} [{attempt.Timestamp:HH:mm:ss}]");
                        }
                        sb.AppendLine("----------------------------------------");
                    }

                    sb.AppendLine("      THANK YOU FOR SHOPPING!");
                    sb.AppendLine("========================================");
                    sb.AppendLine("\n\n\n"); // Feed paper before cut

                    byte[] escPosInit = { 0x1B, 0x40 };
                    byte[] escPosAlignLeft = { 0x1B, 0x61, 0x00 };
                    byte[] payload = Encoding.UTF8.GetBytes(sb.ToString());

                    byte[] fullBytes = escPosInit
                        .Concat(escPosAlignLeft)
                        .Concat(payload)
                        .ToArray();

                    await halPrinter.PrintRawAsync(fullBytes);
                    Debug.WriteLine($"[PRINTER] Printed receipt successfully for Transaction #{payment.TransactionId}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PRINTER ERROR] Failed to print receipt: {ex.Message}");
                }
            });
        }
    }
}