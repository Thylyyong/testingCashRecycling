using SelfCheckoutKiosk.App.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SelfCheckoutKiosk.App.Services
{
    public interface IReceiptPrinterService
    {
        // Intentionally empty for now — will call into actual hardware/print API later
        void Print(Payment payment);
    }
}
