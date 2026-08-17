using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SelfCheckoutKiosk.App.Composition;
using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Core.Catalog;
using SelfCheckoutKiosk.Domain.Entities;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Domain.ValueObjects;
using SelfCheckoutKiosk.Hal.Vendor.DatalogicScanner;
using SelfCheckoutKiosk.Hal.Vendor.EpsonM30;
using SelfCheckoutKiosk.Core.Currency;

namespace SelfCheckoutKiosk.App;

/// <summary>
/// Fully autonomous live scan & cash payment terminal.
///
/// FEATURES:
///   • Barcode Scanner integration (Datalogic / USB HID) with product catalog lookup.
///   • Auto-sets product price and starts cash payment session.
///   • Cash Recycler note acceptance with live banknote alerts.
///   • Itemized receipt printing on Epson EU-m30 upon completion.
/// </summary>
internal static class LiveCashTerminal
{
    private static readonly ProductCatalog _catalog = new();

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
            "SELF-CHECKOUT KIOSK — SCAN & LIVE CASH TERMINAL",
            "Hardware: Scanner + Recycler + Printer");

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

        // Connect Barcode Scanner
        try
        {
            await services.BarcodeScanner.ConnectAsync();
        }
        catch { }

        Product selectedProduct = new Product
        {
            Ean13 = "000000000000",
            Description = "Custom Item",
            UsdPrice = 2.00m
        };

        int amtIdx = Array.IndexOf(args, "--live-cash") + 1;
        if (amtIdx > 0 && amtIdx < args.Length && decimal.TryParse(args[amtIdx], out decimal cmdAmt) && cmdAmt > 0m)
        {
            selectedProduct = new Product
            {
                Ean13 = "CUSTOM",
                Description = $"Custom Amount (${cmdAmt:F2})",
                UsdPrice = cmdAmt
            };
        }
        else
        {
            // Prompt user: Scan barcode or enter price
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("\n  SELECT PRODUCT INPUT MODE:");
            Console.WriteLine("    [1] 📷 Scan / Enter Barcode (e.g. 8886037000185 for Coke $0.75)");
            Console.WriteLine("    [2] 💵 Enter Custom USD Price directly (e.g. 2.00)");
            Console.WriteLine("    [3] 📦 Select from Sample Inventory Catalog");
            Console.ResetColor();
            Console.Write("\n  Select option (default 1): ");

            string opt = Console.ReadLine()?.Trim() ?? "1";

            if (opt == "2")
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write("\n  🛒 Enter Product Price in USD [Default: 2.00]: ");
                Console.ResetColor();
                string? input = Console.ReadLine()?.Trim();
                if (!decimal.TryParse(input, out decimal customPrice) || customPrice <= 0m)
                    customPrice = 2.00m;

                selectedProduct = new Product
                {
                    Ean13 = "MANUAL",
                    Description = $"Custom Item (${customPrice:F2})",
                    UsdPrice = customPrice
                };
            }
            else if (opt == "3")
            {
                Console.WriteLine("\n  Available Products in Catalog:");
                var products = _catalog.GetAllProducts();
                int idx = 1;
                var list = new System.Collections.Generic.List<Product>(products);
                foreach (var p in list)
                {
                    Console.WriteLine($"    [{idx}] {p.Description,-32} | ${p.UsdPrice:F2} USD | Barcode: {p.Ean13}");
                    idx++;
                }
                Console.Write($"\n  Select product [1-{list.Count}] (default 1): ");
                string? selStr = Console.ReadLine()?.Trim();
                if (int.TryParse(selStr, out int sel) && sel >= 1 && sel <= list.Count)
                    selectedProduct = list[sel - 1];
                else
                    selectedProduct = list[0];
            }
            else
            {
                // Option 1: Scan Barcode or 2D QR Code
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("\n  📷 [SCANNER LISTENING] Scan any Barcode or QR Code now!");
                Console.WriteLine("     • Aim your physical scanner at a 1D barcode or 2D QR code (Auto-triggers!)");
                Console.WriteLine("     • Or type an EAN-13 code (e.g. 8886037000185) & press ENTER");
                Console.ResetColor();
                Console.Write("\n  Scanning... > ");

                var scanTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

                EventHandler<BarcodeScannedEventArgs> onScan = (_, e) =>
                {
                    scanTcs.TrySetResult(e.RawBarcode);
                };
                services.BarcodeScanner.OnBarcodeScanned += onScan;

                var keyTask = Task.Run(async () =>
                {
                    var sb = new System.Text.StringBuilder();
                    while (!scanTcs.Task.IsCompleted)
                    {
                        if (Console.KeyAvailable)
                        {
                            var key = Console.ReadKey(intercept: false);
                            if (key.Key == ConsoleKey.Enter)
                            {
                                if (sb.Length > 0)
                                    scanTcs.TrySetResult(sb.ToString().Trim());
                                else
                                    scanTcs.TrySetResult("8886037000185"); // Default Coke
                                break;
                            }
                            else if (key.Key == ConsoleKey.Backspace && sb.Length > 0)
                            {
                                sb.Length--;
                            }
                            else if (!char.IsControl(key.KeyChar))
                            {
                                sb.Append(key.KeyChar);
                            }
                        }
                        await Task.Delay(15);
                    }
                });

                string barcodeInput = await scanTcs.Task;
                services.BarcodeScanner.OnBarcodeScanned -= onScan;

                selectedProduct = _catalog.Lookup(barcodeInput);
            }
        }

        decimal totalUsd = selectedProduct.UsdPrice;
        long totalKhr = (long)(totalUsd * 4100m);

        Console.Clear();
        Banner(
            "SELF-CHECKOUT KIOSK — SCAN & LIVE CASH TERMINAL",
            "Press Q to cancel & disarm at any time.");

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine();
        Console.WriteLine($"  📦 Scanned Item : {selectedProduct.Description}");
        Console.WriteLine($"  🏷️  Barcode      : {selectedProduct.Ean13}");
        Console.WriteLine($"  💲 Price Due    : ${totalUsd:F2} USD  ({totalKhr:N0} KHR)");
        Console.WriteLine($"  💰 Payment Mode : CASH (USD & KHR Banknotes Accepted)");
        Console.WriteLine($"  🧾 Receipt      : Auto-print on EPSON EU-m30");
        Console.ResetColor();
        Console.WriteLine();
        Divider();

        decimal paidUsd    = 0m;
        bool    confirmed  = false;
        bool    cancelled  = false;
        var     sessionDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        services.Engine.OnBalanceChanged += (_, e) =>
        {
            paidUsd = e.TenderedUsd;
            decimal remaining = e.RemainingUsd;
            long    remKhr    = (long)(remaining * DualCurrencyCalculator.DefaultUsdToKhrRate);

            LiveRow("✅ ACCEPTED  ", ConsoleColor.Green,
                "Banknote vaulted. Shutter RE-ARMED for next note.");
            StatusBar(paidUsd, totalUsd);

            if (remaining > 0m)
                LiveRow("⏳ WAITING   ", ConsoleColor.Yellow,
                    $"Still need ${remaining:F2} USD ({remKhr:N0} KHR) — insert next note.");
        };

        services.Engine.OnCashNoteRejected += (_, e) =>
        {
            decimal noteKhr = e.Note.Currency == CurrencyCode.Khr ? e.Note.Amount : e.Note.Amount * DualCurrencyCalculator.DefaultUsdToKhrRate;
            decimal overpayKhr = noteKhr - e.RemainingKhr.Amount;

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  ╔════════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("  ║  ⛔  OVERPAYMENT REJECTED: NOTE RETURNED TO CUSTOMER                ║");
            Console.WriteLine("  ║      Machine accepts exact payment or max 500 KHR (~$0.12) over.   ║");
            Console.WriteLine($"  ║      Overpay Amount : {overpayKhr:N0} KHR (Exceeds 500 KHR tolerance)      ".PadRight(71) + "║");
            Console.WriteLine("  ║                                                                    ║");
            Console.WriteLine("  ║  👉 Please pull your banknote back out of the validator slot!      ║");
            Console.WriteLine("  ║  👉 Insert smaller or exact banknotes (e.g. 100៛, 500៛, 1,000៛, $1)║");
            Console.WriteLine("  ╚════════════════════════════════════════════════════════════════════╝");
            Console.ResetColor();

            StatusBar(paidUsd, totalUsd);
            LiveRow("⏳ WAITING   ", ConsoleColor.Yellow,
                "Intake shutter still OPEN — waiting for correct banknote insertion.");
        };

        services.Engine.OnStateChanged += async (_, e) =>
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] [STATE] {e.Previous} → {e.Current}");
            Console.ResetColor();

            if (e.Current == KioskState.TransactionComplete && !confirmed)
            {
                confirmed = true;
                decimal finalTenderedUsd = paidUsd;
                decimal changeUsd = finalTenderedUsd - totalUsd;
                long overKhr = (long)(changeUsd * DualCurrencyCalculator.DefaultUsdToKhrRate);

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("  ╔══════════════════════════════════════════════════════════╗");
                Console.WriteLine("  ║  🎉  PAYMENT COMPLETE & CONFIRMED!                       ║");
                Console.WriteLine($"  ║     Item Paid    : {selectedProduct.Description}".PadRight(62) + "║");
                Console.WriteLine($"  ║     Total Paid   : ${finalTenderedUsd:F2} USD".PadRight(62) + "║");
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
                        await epson.PrintItemReceiptAsync(
                            selectedProduct.Description,
                            selectedProduct.Ean13,
                            totalUsd,
                            finalTenderedUsd,
                            overKhr);
                        LiveRow("🧾 RECEIPT   ", ConsoleColor.Green, "Itemized receipt printed! Check the printer.");
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
            }
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
            C(ConsoleColor.Cyan, "  Connecting hardware & arming recycler for product...");
            await services.Engine.InitializeAsync();
            LiveRow("🔗 CONNECTED ", ConsoleColor.Green, "Cash recycler online & auto-verified. ✓");

            C(ConsoleColor.Cyan, $"\n  Starting Cash Session for {selectedProduct.Description} (${totalUsd:F2} USD)...");
            await services.Engine.SelectPaymentMethodAsync(PaymentMethod.Cash);
            await services.Engine.BeginCashPaymentAsync(Money.Usd(totalUsd), DualCurrencyCalculator.DefaultUsdToKhrRate);
        }
        catch (Exception ex)
        {
            C(ConsoleColor.Red, $"  [FAIL] Could not arm recycler: {ex.Message}");
            C(ConsoleColor.Yellow, "  Make sure CashDevice-RestAPI.exe is running and device is powered on.");
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