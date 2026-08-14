using System;
using System.Threading.Tasks;
using SelfCheckoutKiosk.App;
using SelfCheckoutKiosk.App.Composition;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Domain.ValueObjects;
using SelfCheckoutKiosk.Hal.Vendor.CashRecyclerX;
using SelfCheckoutKiosk.Hal.Vendor.EpsonM30;

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

// ── LIVE CASH PAYMENT TERMINAL ───────────────────────────────────────────────
// Usage:  dotnet run --project src/SelfCheckoutKiosk.App -- --live-cash [amount]
//   e.g.  dotnet run --project src/SelfCheckoutKiosk.App -- --live-cash 5.00
//
// Fully autonomous — no ENTER prompts during payment.
// Shows every banknote inserted in real-time (amount + currency).
// Supports mixed USD + KHR notes. Prints receipt on completion.
// Press Q at any time to cancel and disarm.
// ─────────────────────────────────────────────────────────────────────────────
if (args.Contains("--live-cash"))
{
    await LiveCashTerminal.RunAsync(args);
    return;
}

// ── RAW API DIAGNOSTIC MODE ───────────────────────────────────────────────────
// Usage:  dotnet run --project src/SelfCheckoutKiosk.App -- --debug-recycler
//
// Arms the recycler shutter then DUMPS the raw JSON the vendor REST API returns
// every 250ms. Use this to discover the exact field names when a banknote is
// inserted. Press Q to stop.
// ─────────────────────────────────────────────────────────────────────────────
if (args.Contains("--debug-recycler"))
{
    await RecyclerApiDebugger.RunAsync();
    return;
}



// Direct printer test — prints a receipt immediately, no other hardware needed
if (args.Contains("--print-test"))
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
    Console.WriteLine("║           EPSON TM-m30 / EU-m30 DIRECT PRINT TEST             ║");
    Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
    Console.ResetColor();

    var installed = EpsonReceiptPrinter.GetInstalledPrinters();
    Console.WriteLine("\n  Installed Windows Printers:");
    if (installed.Count == 0)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("    (No printers returned by Windows spooler API)");
        Console.ResetColor();
    }
    else
    {
        foreach (var p in installed)
        {
            Console.WriteLine($"    • {p}");
        }
    }

    var comPorts = EpsonReceiptPrinter.GetAvailableComPorts();
    Console.WriteLine("  Active System COM Ports:");
    if (comPorts.Count == 0)
    {
        Console.WriteLine("    (No active COM ports detected)");
    }
    else
    {
        foreach (var c in comPorts)
        {
            Console.WriteLine($"    • {c}");
        }
    }

    // Read printer name from optional second arg, default to Windows queue name
    string printerPort = args.Length > 1 && !args[1].StartsWith("--") ? args[1] : "EPSON EU-m30";
    Console.WriteLine($"\n  Target Printer / Port : {printerPort}");
    Console.WriteLine("  Connecting to Epson receipt printer...");

    var printer = new EpsonReceiptPrinter(printerPort);
    try
    {
        await printer.ConnectAsync();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("  [PASS] Printer spooler connected!");
        Console.ResetColor();

        Console.WriteLine("  Sending ESC/POS raw test receipt...");
        await printer.PrintReceiptAsync(totalUsd: 5.00m, tenderedUsd: 5.00m, overpaymentKhr: 0m);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("  [PASS] Spooler accepted receipt payload! Check printer 🧾");
        Console.ResetColor();
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  [FAIL] Printer error: {ex.Message}");
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("  Troubleshooting:");
        Console.WriteLine("    1. If job shows 'Printing, Offline' in Windows Print Queue:");
        Console.WriteLine("       - Open Printers & Scanners -> Click 'EPSON EU-m30' -> Open Queue");
        Console.WriteLine("       - Click 'Printer' menu -> UNCHECK 'Use Printer Offline'");
        Console.WriteLine("    2. If connected via TM Virtual Port Driver (COM port):");
        Console.WriteLine("       - Run:  dotnet run --project src/SelfCheckoutKiosk.App -- --print-test COM3");
        Console.ResetColor();
    }
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
    Console.WriteLine("  4. 🧾 Print Test Receipt (Printer only — no other hardware needed)");
    Console.WriteLine("  5. Exit");
    Console.Write("\nSelect option (1-5): ");

    var input = Console.ReadLine()?.Trim();
    if (input == "5") break;

    if (input == "3")
    {
        await HardwareVerificationHarness.RunAsync();
        Console.WriteLine("\nPress ENTER to return to menu...");
        Console.ReadLine();
        continue;
    }

    if (input == "4")
    {
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║           EPSON TM-m30 — DIRECT RECEIPT PRINT TEST            ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        Console.ResetColor();
        Console.Write("\n  Enter printer name (default EPSON EU-m30): ");
        string printerPort = Console.ReadLine()?.Trim() is { Length: > 0 } p ? p : "EPSON EU-m30";
        Console.WriteLine($"  Connecting to printer on {printerPort}...");

        var printer = new SelfCheckoutKiosk.Hal.Vendor.EpsonM30.EpsonReceiptPrinter(printerPort);
        try
        {
            await printer.ConnectAsync();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  [PASS] Printer connected! Sending receipt...");
            Console.ResetColor();

            await printer.PrintReceiptAsync(totalUsd: 5.00m, tenderedUsd: 5.00m, overpaymentKhr: 0m);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  [PASS] Receipt sent! Check your printer 🧾");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  [FAIL] {ex.Message}");
            Console.WriteLine("  Tip: Make sure the printer is ON and paper is loaded.");
            Console.ResetColor();
        }
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

        services.Engine.OnCashPaymentConfirmed += async (_, e) =>
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n  🎉 [CONFIRMED] PAYMENT COMPLETE! Paid: ${e.TenderedUsd:F2} USD | Status: APPROVED ✓");
            Console.WriteLine($"  🔴 [DISARMED] Physical Shutter CLOSED & Green LED Light OFF!\n");
            Console.ResetColor();

            try
            {
                if (services.ReceiptPrinter is EpsonReceiptPrinter epson)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("  🧾 Printing receipt on EPSON EU-m30...");
                    Console.ResetColor();
                    await epson.ConnectAsync();
                    await epson.PrintReceiptAsync(totalUsd, e.TenderedUsd, e.OverpaymentKhr);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("  [PASS] Receipt printed! Check the printer 🧾");
                    Console.ResetColor();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ⚠ Receipt print error: {ex.Message}");
            }
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
