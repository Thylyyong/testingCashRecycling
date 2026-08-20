using System;
using System.Diagnostics;
using System.Threading.Tasks;
using SelfCheckoutKiosk.App.Models;
using SelfCheckoutKiosk.Core.Abstractions;

namespace SelfCheckoutKiosk.App.Services;

public class ReceiptPrinterService : IReceiptPrinterService
{
    private readonly IReceiptPrinter? _printer;

    public ReceiptPrinterService(IReceiptPrinter? printer = null)
    {
        _printer = printer;
    }

    private Payment? _lastPayment;

    public void Print(Payment payment)
    {
        if (payment == null) return;

        _lastPayment = payment;
        Debug.WriteLine($"[PRINTER] Printing receipt for Transaction #{payment.TransactionId}");

        if (_printer != null)
        {
            Task.Run(async () =>
            {
                try
                {
                    decimal overpayKhr = Math.Max(0, (payment.TotalPaidUsd - payment.TotalDueUsd) * payment.ExchangeRate);
                    if (_printer is Hal.Vendor.EpsonM30.EpsonReceiptPrinter epson)
                    {
                        await epson.PrintReceiptAsync(
                            payment.TotalDueUsd,
                            payment.TotalPaidUsd,
                            overpayKhr,
                            payment.Method.ToString(),
                            payment.TransactionId,
                            paperWidthCols: 40,
                            exchangeRate: payment.ExchangeRate);
                        Diagnostics.DiagnosticLogger.Log($"[PRINTER] Receipt printed successfully for Order #{payment.TransactionId}.");
                    }
                }
                catch (Exception ex)
                {
                    Diagnostics.DiagnosticLogger.LogError($"[PRINTER] Physical print error: {ex.Message}", ex);
                }
            });
        }
    }

    public void ReprintLastReceipt()
    {
        Debug.WriteLine("[PRINTER] Reprint last receipt requested.");

        var paymentToReprint = _lastPayment ?? new Payment
        {
            TransactionId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            TotalDueUsd = 4.50m,
            TotalPaidUsd = 5.00m,
            ExchangeRate = 4100m,
            Method = PaymentMethod.Cash,
            IsFullyPaid = true,
            CompletedAt = DateTime.UtcNow
        };

        if (_printer != null)
        {
            Task.Run(async () =>
            {
                try
                {
                    decimal overpayKhr = Math.Max(0, (paymentToReprint.TotalPaidUsd - paymentToReprint.TotalDueUsd) * paymentToReprint.ExchangeRate);
                    if (_printer is Hal.Vendor.EpsonM30.EpsonReceiptPrinter epson)
                    {
                        await epson.PrintReceiptAsync(
                            paymentToReprint.TotalDueUsd,
                            paymentToReprint.TotalPaidUsd,
                            overpayKhr,
                            $"{paymentToReprint.Method} (DUPLICATE)",
                            paymentToReprint.TransactionId,
                            paperWidthCols: 40,
                            exchangeRate: paymentToReprint.ExchangeRate);
                        Diagnostics.DiagnosticLogger.Log($"[PRINTER] Duplicate receipt reprinted for Order #{paymentToReprint.TransactionId}.");
                    }
                }
                catch (Exception ex)
                {
                    Diagnostics.DiagnosticLogger.LogError($"[PRINTER] Reprint error: {ex.Message}", ex);
                }
            });
        }
    }
}