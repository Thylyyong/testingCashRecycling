using SelfCheckoutKiosk.App.Models;
using System.Diagnostics;

namespace SelfCheckoutKiosk.App.Services
{
    public class ReceiptPrinterService : IReceiptPrinterService
    {
        public void Print(Payment payment)
        {
            // TODO: integrate with actual receipt printer hardware/driver.
            Debug.WriteLine($"[PRINTER] (stub) Would print receipt for Transaction #{payment.TransactionId}");
        }
    }
}