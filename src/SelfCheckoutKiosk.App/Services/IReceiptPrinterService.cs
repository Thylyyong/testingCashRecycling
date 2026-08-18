using SelfCheckoutKiosk.App.Models;
using System.Threading.Tasks;

namespace SelfCheckoutKiosk.App.Services
{
    public interface IReceiptPrinterService
    {
        void Print(Payment payment);
        void ReprintLastReceipt();
    }
}
