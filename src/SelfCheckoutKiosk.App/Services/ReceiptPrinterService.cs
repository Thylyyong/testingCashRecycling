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

    public void Print(Payment payment)
    {
        if (payment == null) return;

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
                            payment.TransactionId);
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
    }
}