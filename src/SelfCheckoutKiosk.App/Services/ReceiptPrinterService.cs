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
        Debug.WriteLine($"[PRINTER] Printing supermarket receipt for Transaction #{payment.TransactionId}");

        if (_printer != null)
        {
            Task.Run(async () =>
            {
                try
                {
                    decimal overpayKhr = Math.Max(0, (payment.TotalPaidUsd - payment.TotalDueUsd) * payment.ExchangeRate);
                    if (_printer is Hal.Vendor.EpsonM30.EpsonReceiptPrinter epson)
                    {
                        var lineItems = payment.Items?.Select(i => new Hal.Vendor.EpsonM30.ReceiptLineItem
                        {
                            Name = i.Name,
                            Sku = i.Sku,
                            Quantity = i.Quantity,
                            UnitPrice = i.UnitPrice
                        }).ToList();

                        string storeName = "SUPERMARKET EXPRESS";
                        string storeTagline = "AUTHENTIC FOOD & GROCERY";
                        string storeAddress = "Phnom Penh, Cambodia";
                        string storePhone = "+855 23 999 888";

                        try
                        {
                            var branding = MediaBrandingService.Instance.Branding;
                            if (!string.IsNullOrWhiteSpace(branding.CompanyName)) storeName = branding.CompanyName;
                            if (!string.IsNullOrWhiteSpace(branding.Tagline)) storeTagline = branding.Tagline;
                        }
                        catch { }

                        await epson.PrintReceiptAsync(
                            payment.TotalDueUsd,
                            payment.TotalPaidUsd,
                            overpayKhr,
                            payment.Method.ToString(),
                            payment.TransactionId,
                            paperWidthCols: 48,
                            exchangeRate: payment.ExchangeRate,
                            items: lineItems,
                            storeName: storeName,
                            storeTagline: storeTagline,
                            storeAddress: storeAddress,
                            storePhone: storePhone);

                        Diagnostics.DiagnosticLogger.Log($"[PRINTER] Supermarket receipt printed for Order #{payment.TransactionId}.");
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
            TotalDueUsd = 5.75m,
            TotalPaidUsd = 10.00m,
            ChangeDueUsd = 4.25m,
            ExchangeRate = 4100m,
            Method = PaymentMethod.Cash,
            IsFullyPaid = true,
            CompletedAt = DateTime.Now,
            Items = new System.Collections.Generic.List<CartItemSnapshot>
            {
                new() { Name = "Coca Cola Can 330ml", Sku = "8850024100123", Quantity = 2, UnitPrice = 0.75m },
                new() { Name = "Angkor Premium Beer 330ml", Sku = "8840001002003", Quantity = 1, UnitPrice = 1.25m },
                new() { Name = "Lay's Classic Potato Chips 50g", Sku = "0284000432109", Quantity = 2, UnitPrice = 1.50m }
            }
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
                        var lineItems = paymentToReprint.Items?.Select(i => new Hal.Vendor.EpsonM30.ReceiptLineItem
                        {
                            Name = i.Name,
                            Sku = i.Sku,
                            Quantity = i.Quantity,
                            UnitPrice = i.UnitPrice
                        }).ToList();

                        string storeName = "SUPERMARKET EXPRESS";
                        string storeTagline = "AUTHENTIC FOOD & GROCERY";
                        string storeAddress = "Phnom Penh, Cambodia";
                        string storePhone = "+855 23 999 888";

                        try
                        {
                            var branding = MediaBrandingService.Instance.Branding;
                            if (!string.IsNullOrWhiteSpace(branding.CompanyName)) storeName = branding.CompanyName;
                            if (!string.IsNullOrWhiteSpace(branding.Tagline)) storeTagline = branding.Tagline;
                        }
                        catch { }

                        await epson.PrintReceiptAsync(
                            paymentToReprint.TotalDueUsd,
                            paymentToReprint.TotalPaidUsd,
                            overpayKhr,
                            $"{paymentToReprint.Method} (DUPLICATE)",
                            paymentToReprint.TransactionId,
                            paperWidthCols: 48,
                            exchangeRate: paymentToReprint.ExchangeRate,
                            items: lineItems,
                            storeName: storeName,
                            storeTagline: storeTagline,
                            storeAddress: storeAddress,
                            storePhone: storePhone);

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