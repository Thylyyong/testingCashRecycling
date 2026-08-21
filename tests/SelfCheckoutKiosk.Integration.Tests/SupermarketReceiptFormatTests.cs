using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SelfCheckoutKiosk.Hal.Vendor.EpsonM30;
using Xunit;

namespace SelfCheckoutKiosk.Integration.Tests;

public class SupermarketReceiptFormatTests
{
    [Fact]
    public async Task PrintReceiptAsync_SupermarketTemplate_FormatsCorrectlyWithoutLineOverflow()
    {
        var printer = new EpsonReceiptPrinter("SPOOL:EPSON EU-m30");

        var items = new List<ReceiptLineItem>
        {
            new() { Name = "Coca Cola Can 330ml", Sku = "8850024100123", Quantity = 2, UnitPrice = 0.75m },
            new() { Name = "Angkor Premium Beer 330ml", Sku = "8840001002003", Quantity = 1, UnitPrice = 1.25m },
            new() { Name = "Lay's Classic Potato Chips 50g", Sku = "0284000432109", Quantity = 2, UnitPrice = 1.50m }
        };

        // Should format cleanly without exceptions
        await printer.PrintReceiptAsync(
            totalUsd: 5.75m,
            tenderedUsd: 10.00m,
            overpaymentKhr: 17500m,
            paymentMethod: "CASH",
            transactionId: "TXN-TEST-1234",
            paperWidthCols: 48,
            exchangeRate: 4100m,
            items: items,
            storeName: "SUPERMARKET EXPRESS",
            storeTagline: "AUTHENTIC FOOD & GROCERY",
            storeAddress: "Phnom Penh, Cambodia",
            storePhone: "+855 23 999 888",
            cashierName: "Kiosk #01"
        );
    }
}
