using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SelfCheckoutKiosk.App.Composition;
using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Domain.ValueObjects;
using SelfCheckoutKiosk.Hal.Vendor.EpsonM30;

namespace SelfCheckoutKiosk.App;

/// <summary>
/// Fully autonomous live cash payment terminal.
///
/// HOW TO RUN:
///   dotnet run --project src/SelfCheckoutKiosk.App -- --live-cash [amount_usd]
///
///   Examples:
///     dotnet run --project src/SelfCheckoutKiosk.App -- --live-cash 2.00
///     dotnet run --project src/SelfCheckoutKiosk.App -- --live-cash 5.50
///
/// BEHAVIOUR:
///   • Arms the physical cash recycler shutter immediately.
///   • Auto-detects active COM ports across the system.
///   • Prints a live alert for EVERY banknote the machine detects.
///   • Shows the note denomination, currency (USD / KHR ៛), running total, and remaining balance.
///   • Supports mixed USD + KHR notes in a single session.
///   • Automatically prints a receipt and disarms the recycler when fully paid.
///   • Rejects and physically ejects any note that would overpay by > 500 KHR.
///   • Press Q at any time to cancel the session and disarm.
/// </summary>
internal static class LiveCashTerminal
{
    private static void C(ConsoleColor c, string msg)
    {
        Console.ForegroundColor = c;
        Console.WriteLine(msg);
        Console.ResetColor();
    }

    private static void Banner(string line1, string line2 = "")
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine($"║  {line1,-61}║");
        if (!string.IsNullOrEmpty(line2))
            Console.WriteLine($"║  {line2,-61}║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        Console.ResetColor();
    }

    private static void Divider() =>
        Console.WriteLine(new string('─', 65));

    private static void LiveRow(string tag, ConsoleColor col, string msg)
    {
        Console.ForegroundColor = col;
        Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] {tag} {msg}");
        Console.ResetColor();
    }

    private static void StatusBar(decimal paidUsd, decimal dueUsd)
    {
        decimal remainUsd = Math.Max(0m, dueUsd - paidUsd);
        long    remainKhr = (long)(remainUsd * 4100m);
        long    paidKhr   = (long)(paidUsd   * 4100m);
        long    dueKhr    = (long)(dueUsd    * 4100m);

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine();
        Console.WriteLine($"  ┌─ BALANCE ────────────────────────────────────────────────┐");
        Console.WriteLine($"  │  Total Due   : ${dueUsd:F2} USD  ({dueKhr:N0} KHR)".PadRight(64) + "│");
        Console.WriteLine($"  │  Paid So Far : ${paidUsd:F2} USD  ({paidKhr:N0} KHR)".PadRight(64) + "│");

        if (remainUsd > 0m)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  │  Still Owed  : ${remainUsd:F2} USD  ({remainKhr:N0} KHR)  ← INSERT MORE NOTES".PadRight(64) + "│");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  │  Still Owed  : PAID IN FULL ✓".PadRight(64) + "│");
        }

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  └──────────────────────────────────────────────────────────┘");
        Console.ResetColor();
        Console.WriteLine();
    }

    internal static async Task RunAsync(string[] args)
    {
        Console.Clear();
        Banner(
            "SELF-CHECKOUT KIOSK — LIVE CASH PAYMENT TERMINAL",
            "Press Q to cancel & disarm at any time.");

        decimal totalUsd = 2.00m;
        int     amtIdx   = Array.IndexOf(args, "--live-cash") + 1;
        if (amtIdx > 0 && amtIdx < args.Length && decimal.TryParse(args[amtIdx], out decimal parsed) && parsed > 0m)
            totalUsd = parsed;

        long totalKhr = (long)(totalUsd * 4100m);

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine();
        Console.WriteLine($"  Order Total  : ${totalUsd:F2} USD  ({totalKhr:N0} KHR)");
        Console.WriteLine($"  Payment Mode : CASH  (USD and KHR notes accepted)");
        Console.WriteLine($"  Recycler     : http://localhost:5000  (COM Auto-Discovery Active)");
        Console.WriteLine($"  Printer      : EPSON EU-m30");
        Console.ResetColor();
        Console.WriteLine();
        Divider();

        C(ConsoleColor.Cyan, "  Initialising hardware & auto-detecting COM port...");
        KioskServices services;
        try
        {
            services = CompositionRoot.Build();
        }
        catch (Exception ex)
        {
            C(ConsoleColor.Red, $"  [FAIL] Composition root failed: {ex.Message}");
            return;
        }

        decimal paidUsd    = 0m;
        bool    confirmed  = false;
        bool    cancelled  = false;
        var     sessionDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        services.CashRecycler.OnNoteInEscrow += (_, e) =>
        {
            string currency = e.Note.Currency == CurrencyCode.Usd
                ? $"${e.Note.Amount:F2} USD"
                : $"{e.Note.Amount:N0} KHR (៛)";

            Console.WriteLine();
            LiveRow("💵 NOTE IN  ", ConsoleColor.Cyan,
                $"DETECTED: {currency}  ← physical note accepted by validator");
        };

        services.Engine.OnCashPaymentPending += (_, e) =>
        {
            paidUsd = e.TenderedUsd;
            decimal remaining = e.RemainingUsd;
            long    remKhr    = (long)e.RemainingKhr;

            LiveRow("✅ ACCEPTED  ", ConsoleColor.Green,
                $"Banknote vaulted. Shutter RE-ARMED for next note.");
            StatusBar(paidUsd, totalUsd);

            if (remaining > 0m)
                LiveRow("⏳ WAITING   ", ConsoleColor.Yellow,
                    $"Still need ${remaining:F2} USD ({remKhr:N0} KHR) — insert next note.");
        };

        services.Engine.OnCashPaymentConfirmed += async (_, e) =>
        {
            confirmed = true;
            paidUsd   = e.TenderedUsd;
            long overKhr = (long)e.OverpaymentKhr;

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  ╔══════════════════════════════════════════════════════════╗");
            Console.WriteLine("  ║  🎉  PAYMENT COMPLETE & CONFIRMED!                       ║");
            Console.WriteLine($"  ║     Total Paid   : ${e.TenderedUsd:F2} USD".PadRight(62) + "║");
            Console.WriteLine($"  ║     Change Due   : {overKhr:N0} KHR (returned as Riel)".PadRight(62) + "║");
            Console.WriteLine("  ║     Shutter CLOSED — Green LED OFF 🔴                   ║");
            Console.WriteLine("  ╚══════════════════════════════════════════════════════════╝");
            Console.ResetColor();

            try
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"\n  Printing receipt on EPSON EU-m30...");
                Console.ResetColor();

                if (services.ReceiptPrinter is EpsonReceiptPrinter epson)
                {
                    await epson.ConnectAsync();
                    await epson.PrintReceiptAsync(totalUsd, e.TenderedUsd, overKhr);
                    LiveRow("🧾 RECEIPT   ", ConsoleColor.Green, "Sent to printer! Check the paper tray.");
                }

                string auditDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "SelfCheckoutKiosk", "receipts");
                LiveRow("💾 AUDIT     ", ConsoleColor.DarkGray,
                    $"Digital copy saved to: {auditDir}");
            }
            catch (Exception ex)
            {
                LiveRow("⚠️  PRINT ERR ", ConsoleColor.Yellow, $"Receipt print failed: {ex.Message}");
            }

            sessionDone.TrySetResult(true);
        };

        services.Engine.OnCashPaymentRejected += (_, e) =>
        {
            Console.WriteLine();
            LiveRow("⛔ REJECTED  ", ConsoleColor.Red,
                $"Note EJECTED — overpay of {e.OverpaymentKhr:N0} KHR exceeds 500 KHR limit.");
            LiveRow("↩️  RETURNED  ", ConsoleColor.Yellow,
                "Note physically pushed back out of the machine slot.");
            StatusBar(paidUsd, totalUsd);
            LiveRow("⏳ WAITING   ", ConsoleColor.Yellow,
                "Shutter still OPEN — insert a smaller note.");
        };

        services.Engine.OnStateChanged += (_, e) =>
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] [STATE] {e.Previous} → {e.Current}");
            Console.ResetColor();
        };

        services.Engine.OnHardwareFault += (_, e) =>
        {
            Console.WriteLine();
            LiveRow("⚠️  FAULT     ", ConsoleColor.Red, $"[{e.Device}] {e.Message}");
            LiveRow("🔴 DISARMED  ", ConsoleColor.Red, "Engine moved to Faulted state.");
            sessionDone.TrySetResult(false);
        };

        try
        {
            C(ConsoleColor.Cyan, "  Connecting to Cash Recycler REST API (Auto-Discovering COM Port)...");
            await services.Engine.InitializeAsync();
            LiveRow("🔗 CONNECTED ", ConsoleColor.Green, "Cash recycler online & auto-verified. ✓");

            C(ConsoleColor.Cyan, $"\n  Starting ${totalUsd:F2} USD cash payment session...");
            await services.Engine.SelectPaymentMethodAsync(PaymentMethod.Cash);
            await services.Engine.BeginCashPaymentAsync(totalUsd);
        }
        catch (Exception ex)
        {
            C(ConsoleColor.Red, $"  [FAIL] Could not arm recycler: {ex.Message}");
            C(ConsoleColor.Yellow, "  Make sure CashDevice-RestAPI.exe is running and the device is powered on.");
            return;
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("  ╔══════════════════════════════════════════════════════════╗");
        Console.WriteLine("  ║  🟢  SHUTTER OPEN — MACHINE ARMED — ACCEPTING CASH       ║");
        Console.WriteLine($"  ║     Target : ${totalUsd:F2} USD  ({totalKhr:N0} KHR)".PadRight(62) + "║");
        Console.WriteLine("  ║     Insert USD ($1, $5, $10, $20) or KHR (1000, 2000…)  ║");
        Console.WriteLine("  ║     Mixed payments are fully supported.                  ║");
        Console.WriteLine("  ║     Press Q to cancel session.                           ║");
        Console.WriteLine("  ╚══════════════════════════════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine();
        StatusBar(0m, totalUsd);

        using var cts = new CancellationTokenSource();

        var keyWatcher = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true);
                    if (key.Key == ConsoleKey.Q || key.Key == ConsoleKey.Escape)
                    {
                        cancelled = true;
                        sessionDone.TrySetResult(false);
                        break;
                    }
                }
                await Task.Delay(100, cts.Token).ConfigureAwait(false);
            }
        }, cts.Token);

        await sessionDone.Task;
        await cts.CancelAsync();

        try { await keyWatcher; } catch { /* cancelled */ }

        try { await services.Engine.ResetToIdleAsync(); } catch { /* best-effort */ }

        Console.WriteLine();
        Divider();
        if (cancelled)
        {
            C(ConsoleColor.Yellow, "  ❌ Session cancelled by operator. Shutter CLOSED. Machine IDLE.");
        }
        else if (confirmed)
        {
            C(ConsoleColor.Green, "  ✓ Session complete. Machine returned to IDLE.");
        }
        else
        {
            C(ConsoleColor.Red, "  ⚠ Session ended due to hardware fault. Check device status.");
        }
        Console.WriteLine();
    }
}