using System;
using System.Threading.Tasks;
using SelfCheckoutKiosk.App;
using SelfCheckoutKiosk.App.Composition;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Domain.ValueObjects;
using SelfCheckoutKiosk.Hal.Vendor.CashRecyclerX;

if (args.Contains("--verify-hardware"))
{
    await HardwareVerificationHarness.RunAsync();
    return;
}

if (args.Contains("--live") || args.Contains("--live-hardware"))
{
    await HardwareVerificationHarness.RunLiveListenerAsync();
    return;
}

// Default Interactive Kiosk Demo Menu
while (true)
{
    Console.Clear();
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
    Console.WriteLine("║        SELF-CHECKOUT KIOSK — INTERACTIVE PAYMENT SYSTEM       ║");
    Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
    Console.ResetColor();
    Console.WriteLine("\nChoose Payment Option:");
    Console.WriteLine("  1. Pay with CASH (Opens intake shutter & Green Light ON 🟢)");
    Console.WriteLine("  2. Pay with QR CODE / KHQR (Machine STAYS CLOSED & Light OFF 🔴)");
    Console.WriteLine("  3. Run 7-Test Hardware Verification Suite");
    Console.WriteLine("  4. Exit");
    Console.Write("\nSelect option (1-4): ");

    var input = Console.ReadLine()?.Trim();
    if (input == "4") break;

    if (input == "3")
    {
        await HardwareVerificationHarness.RunAsync();
        Console.WriteLine("\nPress ENTER to return to menu...");
        Console.ReadLine();
        continue;
    }

    var services = CompositionRoot.Build();
    await services.Engine.InitializeAsync();

    if (input == "1")
    {
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("             CASH PAYMENT SESSION — LIVE MONITOR               ");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.Write("Enter product total in USD (default 2.00): ");
        string? amountStr = Console.ReadLine()?.Trim();
        if (!decimal.TryParse(amountStr, out decimal totalUsd) || totalUsd <= 0m) totalUsd = 2.00m;

        // Wire live event alerts for banknote insertions & status
        services.CashRecycler.OnNoteInEscrow += (_, e) =>
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            string val = e.Note.Currency == CurrencyCode.Usd ? $"${e.Note.Amount:F2} USD" : $"{e.Note.Amount:N0} KHR (៛)";
            Console.WriteLine($"\n  💵 [ALERT] BANKNOTE DETECTED: {val}");
            Console.ResetColor();
        };

        services.Engine.OnCashPaymentPending += (_, e) =>
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  🟢 [PENDING] Paid: ${e.TenderedUsd:F2} USD | Remaining Due: ${e.RemainingUsd:F2} USD ({e.RemainingKhr:N0} KHR) | Shutter: OPEN 🟢");
            Console.ResetColor();
        };

        services.Engine.OnCashPaymentConfirmed += (_, e) =>
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n  🎉 [CONFIRMED] PAYMENT COMPLETE! Paid: ${e.TenderedUsd:F2} USD | Status: APPROVED ✓");
            Console.WriteLine($"  🔴 [DISARMED] Physical Shutter CLOSED & Green LED Light OFF!\n");
            Console.ResetColor();
        };

        services.Engine.OnCashPaymentRejected += (_, e) =>
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n  ⛔ [REJECTED] Note Ejected! Overpayment of {e.OverpaymentKhr:N0} KHR exceeds 500 KHR limit.");
            Console.ResetColor();
        };

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\nStarting Cash Session for ${totalUsd:F2} USD...");
        Console.ResetColor();

        // 1. Select CASH method -> Arms recycler -> Shutter OPENS & Green Light TURNS ON 🟢
        await services.Engine.SelectPaymentMethodAsync(PaymentMethod.Cash);
        await services.Engine.BeginCashPaymentAsync(totalUsd);

        Console.ForegroundColor = ConsoleColor.Cyan;
        long totalKhr = (long)(totalUsd * 4100m);
        Console.WriteLine($"\n[HARDWARE ARMED 🟢] Active Payment Session — Target Total: ${totalUsd:F2} USD ({totalKhr:N0} KHR)");
        Console.WriteLine("Feed physical USD ($) or KHR (៛) banknotes into the validator slot now.");
        Console.WriteLine("The machine will auto-scan notes and update balance in real-time.");
        Console.WriteLine("(Press ENTER anytime to cancel session)");
        Console.ResetColor();

        // Autonomous loop: wait for physical notes inserted into hardware validator slot
        while (services.Engine.CurrentState == KioskState.ProcessingCash)
        {
            await Task.Delay(100);
            if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Enter)
                break;
        }

        // 2. Disarm -> Shutter CLOSES & Green Light TURNS OFF 🔴
        await services.Engine.ResetToIdleAsync();
        Console.WriteLine("\n[IDLE 🔴] Payment session complete / closed. Shutter CLOSED & Light OFF.");
        await Task.Delay(2000);
    }
    else if (input == "2")
    {
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("--- QR CODE / KHQR PAYMENT SELECTED ---");
        Console.ResetColor();

        // 1. Select QR method -> Disarms cash recycler -> Shutter STAYS CLOSED & Green Light STAYS OFF 🔴
        await services.Engine.SelectPaymentMethodAsync(PaymentMethod.KhqrDigital);

        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine("\n[HARDWARE OFF 🔴] Cash recycler slot is CLOSED and Light is OFF.");
        Console.WriteLine("Customer is scanning KHQR Digital code on screen...");
        Console.ResetColor();

        await Task.Delay(3000);
        await services.Engine.ResetToIdleAsync();
        Console.WriteLine("\n[IDLE 🔴] QR payment complete. Returning to Home Page...");
        await Task.Delay(2000);
    }
}
