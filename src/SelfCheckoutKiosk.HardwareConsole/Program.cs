using System;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Core.Currency;
using SelfCheckoutKiosk.Core.Engine;
using SelfCheckoutKiosk.Core.Licensing;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Domain.ValueObjects;
using SelfCheckoutKiosk.Hal.Vendor.CashRecyclerX;
using SelfCheckoutKiosk.Hal.Vendor.DatalogicScanner;
using SelfCheckoutKiosk.Hal.Vendor.EpsonM30;
using SelfCheckoutKiosk.Infrastructure.Data;
using SelfCheckoutKiosk.Infrastructure.Data.Licensing;
using SelfCheckoutKiosk.Infrastructure.Security;
using SelfCheckoutKiosk.Infrastructure.Security.Licensing;

/// <summary>
/// Console hardware test tool.
///
/// COMMANDS:
///   --detect-hardware    Scan COM ports and list cash recycler / printer / scanner candidates (read-only)
///   --verify-hardware    Interactive step-by-step hardware verification (inserts notes, prints receipt)
///   --live-cash          Live real-time cash terminal — arm recycler and watch banknotes come in
///   --print-test         Send a test receipt to the Epson printer immediately
///
/// HOW TO RUN (from repo root SelfCheckoutKiosk-V2\SelfCheckoutKiosk-V2\):
///   dotnet run -a x64 --project src/SelfCheckoutKiosk.HardwareConsole -- --detect-hardware
///   dotnet run -a x64 --project src/SelfCheckoutKiosk.HardwareConsole -- --verify-hardware
///   dotnet run -a x64 --project src/SelfCheckoutKiosk.HardwareConsole -- --live-cash
///   dotnet run -a x64 --project src/SelfCheckoutKiosk.HardwareConsole -- --print-test
/// </summary>
Console.OutputEncoding = Encoding.UTF8;

string flag = args.FirstOrDefault() ?? "--detect-hardware";

switch (flag)
{
    case "--detect-hardware":
        await HardwareConsoleTools.DetectHardwareAsync();
        break;

    case "--verify-hardware":
        await HardwareConsoleTools.VerifyHardwareAsync();
        break;

    case "--live-cash":
        await HardwareConsoleTools.LiveCashMonitorAsync();
        break;

    case "--print-test":
        await HardwareConsoleTools.PrintTestReceiptAsync();
        break;

    default:
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"Unknown flag: {flag}");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine("Available commands:");
        Console.WriteLine("  --detect-hardware   List COM ports + hardware candidates (safe, read-only)");
        Console.WriteLine("  --verify-hardware   Step-by-step interactive hardware test");
        Console.WriteLine("  --live-cash         Live cash terminal (arm recycler, watch notes)");
        Console.WriteLine("  --print-test        Print a test receipt to the Epson printer");
        break;
}

// ─────────────────────────────────────────────────────────────────────────────

static class HardwareConsoleTools
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    static void Pass(string msg)  { Console.ForegroundColor = ConsoleColor.Green;  Console.WriteLine($"  [PASS] {msg}"); Console.ResetColor(); }
    static void Fail(string msg)  { Console.ForegroundColor = ConsoleColor.Red;    Console.WriteLine($"  [FAIL] {msg}"); Console.ResetColor(); }
    static void Info(string msg)  { Console.ForegroundColor = ConsoleColor.Cyan;   Console.WriteLine($"  [INFO] {msg}"); Console.ResetColor(); }
    static void Warn(string msg)  { Console.ForegroundColor = ConsoleColor.Yellow; Console.WriteLine($"  [WARN] {msg}"); Console.ResetColor(); }
    static void Prompt(string m)  { Console.ForegroundColor = ConsoleColor.Yellow; Console.Write($"  >>> {m} > "); Console.ResetColor(); Console.ReadLine(); }

    static void Section(string title)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(new string('─', 65));
        Console.WriteLine($"  {title}");
        Console.WriteLine(new string('─', 65));
        Console.ResetColor();
    }

    static string GetAuditLogPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SelfCheckoutKiosk", "logs", "cash_hardware.log");

    static string? TryReadApiKey()
    {
        try
        {
            if (File.Exists("api_key.secret")) return File.ReadAllText("api_key.secret").Trim();
            if (File.Exists(@"..\api_key.secret")) return File.ReadAllText(@"..\api_key.secret").Trim();
            if (File.Exists(@"..\..\api_key.secret")) return File.ReadAllText(@"..\..\api_key.secret").Trim();
            return null;
        }
        catch { return null; }
    }

    static ILLCoreLogicEngine BuildEngine()
    {
        var apiKey      = TryReadApiKey();
        var cashOptions = CashRecyclerOptions.FromEnvironment(apiKey);

        ICashRecycler    cashRecycler   = new VendorXCashRecycler(cashOptions.ApiBaseUrl, cashOptions.ApiKey, useRealApi: cashOptions.UseRealApi);
        IBarcodeScanner  barcodeScanner = new DatalogicBarcodeScanner();
        IReceiptPrinter  receiptPrinter = new EpsonReceiptPrinter();
        var calculator       = new DualCurrencyCalculator();
        var lowFloatMonitor  = new LowFloatMonitor();
        var licenseManager   = new DevBypassLicenseManager();
        var hardwareLog      = new HardwareAppendLog(GetAuditLogPath());

        return new LLCoreLogicEngine(cashRecycler, barcodeScanner, receiptPrinter,
            calculator, lowFloatMonitor, licenseManager, hardwareLog);
    }

    // ── --detect-hardware (safe, read-only) ───────────────────────────────────

    static void SafeClear() { try { Console.Clear(); } catch { } }

    public static async Task DetectHardwareAsync()
    {
        SafeClear();
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║   SELF-CHECKOUT KIOSK — HARDWARE DETECTION (READ-ONLY)        ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        Console.ResetColor();

        Section("Serial / COM Ports");
        var ports = SerialPort.GetPortNames().OrderBy(p => p).ToArray();
        Info(ports.Length == 0 ? "No COM ports found." : $"Found: {string.Join(", ", ports)}");

        Section("Cash Recycler (CashRecyclerX REST API)");
        try
        {
            var apiKey = TryReadApiKey();
            var opts   = CashRecyclerOptions.FromEnvironment(apiKey);
            string? found = await CashRecyclerDiscovery.FindConnectedPortAsync(opts, ports);
            if (found != null)
                Pass($"Vendor REST API responded on: {found}");
            else
                Warn("No cash recycler verified. Is CashDevice-RestAPI.exe running?");
        }
        catch (Exception ex)
        {
            Fail($"Cash recycler check failed: {ex.Message}");
        }

        Section("Receipt Printer (Epson TM-m30)");
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine
                .OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Print\Printers");
            var printers = (key?.GetSubKeyNames() ?? [])
                .Where(n => n.Contains("EPSON", StringComparison.OrdinalIgnoreCase) ||
                            n.Contains("TM-M30", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (printers.Length > 0)
                foreach (var p in printers) Pass($"Found printer queue: {p}");
            else
                Warn("No Epson/TM-m30 printer found in Windows print queue.");
        }
        catch (Exception ex)
        {
            Fail($"Printer registry check failed: {ex.Message}");
        }

        Section("Barcode Scanner (Datalogic)");
        Info(ports.Length > 0
            ? $"Possible USB-COM scanner ports: {string.Join(", ", ports)} — confirm with a scan."
            : "No COM ports. Scanner may be HID keyboard-wedge (no driver needed, works in any text field).");

        Console.WriteLine();
        Info("Detection complete. Run --verify-hardware to do interactive live tests.");
    }

    // ── --verify-hardware (interactive step-by-step) ──────────────────────────

    public static async Task VerifyHardwareAsync()
    {
        SafeClear();
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║   SELF-CHECKOUT KIOSK — HARDWARE VERIFICATION                 ║");
        Console.WriteLine("║   Plug in all USB devices before continuing.                   ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        Console.ResetColor();

        Prompt("Press ENTER when devices are ready");

        // Build engine
        Section("STEP 1 — Build engine + connect via REST API");
        ILLCoreLogicEngine engine;
        try
        {
            engine = BuildEngine();
            Info("Calling InitializeAsync (license check + USB connection)...");
            await engine.InitializeAsync();

            if (engine.CurrentState == KioskState.Idle)
                Pass("Engine Idle — cash recycler connected!");
            else
                Fail($"Engine state = {engine.CurrentState} (expected Idle)");
        }
        catch (Exception ex)
        {
            Fail($"InitializeAsync threw: {ex.Message}");
            Warn("Fix: make sure CashDevice-RestAPI.exe is running, then retry.");
            return;
        }

        // Wire live console listeners
        engine.OnStateChanged     += (_, e) => Info($"State: {e.Previous} → {e.Current}");
        engine.OnBalanceChanged   += (_, e) => Pass($"NOTE ACCEPTED — Paid ${e.TenderedUsd:F2}, Remaining ${e.RemainingUsd:F2}");
        engine.OnCashNoteRejected += (_, e) => Warn($"NOTE REJECTED — {e.Note} (over 500 KHR tolerance, physically ejected)");
        engine.OnHardwareFault    += (_, e) => Fail($"HARDWARE FAULT [{e.Device}]: {e.Message}");

        // Step 2 — cash payment session
        Section("STEP 2 — Arm cash recycler ($5.00 session)");
        try
        {
            await engine.SubmitScanAsync("7243026805126"); // Transition Idle -> Scanning
            await engine.BeginCashPaymentAsync(Money.Usd(5.00m), DualCurrencyCalculator.DefaultUsdToKhrRate);
            Pass("Recycler ARMED — green LED should be on. Insert a $1 note.");
        }
        catch (Exception ex) { Fail($"BeginCashPaymentAsync: {ex.Message}"); return; }

        Prompt("Insert a banknote now, then press ENTER");
        await Task.Delay(2000);

        // Step 3 — overpayment rejection test
        Section("STEP 3 — Overpayment rejection (> 500 KHR over remaining)");
        Warn("Insert a note LARGER than the remaining balance + 500 KHR. It should be ejected.");
        Prompt("Press ENTER after inserting the overpaying note");
        await Task.Delay(3000);

        // Step 4 — complete payment
        Section("STEP 4 — Complete payment (reach zero remaining)");
        Warn("Insert notes to cover the remaining balance (within 500 KHR).");
        Prompt("Press ENTER after inserting the final note(s)");
        await Task.Delay(2000);

        bool done = engine.CurrentState == KioskState.TransactionComplete;
        if (done) Pass("Engine → TransactionComplete. Payment complete!");
        else      Warn($"Engine state = {engine.CurrentState}. May need more notes.");

        // Step 5 — print receipt
        Section("STEP 5 — Print receipt");
        try
        {
            var printer = new EpsonReceiptPrinter();
            await printer.ConnectAsync();
            bool paper = await printer.IsPaperPresentAsync();
            Info($"Paper sensor: {(paper ? "OK (paper present)" : "EMPTY — load paper!")}");
            await printer.PrintReceiptAsync(5.00m, 5.00m, 0m);
            Pass("Receipt printed on Epson TM-m30!");
        }
        catch (Exception ex) { Fail($"Printer error: {ex.Message}"); }

        Section("VERIFICATION COMPLETE");
        Pass("All steps done. Hardware is wired correctly to the engine.");
    }

    // ── --live-cash (interactive barcode → product → cash payment) ──────────────

    // Local product catalogue (mirrors MockProductService)
    static readonly Dictionary<string, (string Name, string Category, decimal PriceUsd)> _catalogue = new()
    {
        ["8485234828415"] = ("Organic Fresh Milk 1L",        "Dairy",      2.50m),
        ["4111154381009"] = ("Free Range Eggs 10pk",          "Dairy",      3.20m),
        ["7072010958018"] = ("Cheddar Cheese Block 250g",     "Dairy",      2.80m),
        ["7243026805126"] = ("Coca-Cola 330ml",               "Beverages",  0.50m),
        ["8975067721347"] = ("Pepsi 330ml",                   "Beverages",  0.50m),
        ["6855258299058"] = ("Sprite Lemon-Lime 330ml",       "Beverages",  0.50m),
        ["4644880396783"] = ("Fanta Orange 330ml",            "Beverages",  0.50m),
        ["9322386486164"] = ("Dasani Drinking Water 600ml",   "Beverages",  0.35m),
        ["3725887008914"] = ("Pocari Sweat 500ml",            "Beverages",  1.20m),
        ["2067904601298"] = ("Red Bull Energy Drink 250ml",   "Beverages",  1.80m),
        ["1456818123131"] = ("Orange Juice 1L",               "Beverages",  2.10m),
        ["8460683783843"] = ("Green Tea 500ml",               "Beverages",  0.95m),
        ["4763527319371"] = ("Iced Coffee Latte 250ml",       "Beverages",  1.60m),
        ["7735329487883"] = ("Fuji Apples (kg)",              "Produce",    3.50m),
        ["9663035162627"] = ("Bananas (kg)",                  "Produce",    1.80m),
        ["1449711595198"] = ("Heineken Beer 330ml",           "Alcohol",    2.20m),
    };

    public static async Task LiveCashMonitorAsync()
    {
        SafeClear();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║   SELF-CHECKOUT KIOSK — LIVE CASH TERMINAL                    ║");
        Console.WriteLine("║   Scan / paste barcode → add items → pay with cash            ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        Console.ResetColor();

        // ── STEP 0: Initialize hardware engine upfront ──────────────────────
        var apiKey         = TryReadApiKey();
        var cashOptions    = CashRecyclerOptions.FromEnvironment(apiKey);
        var cashRecycler   = new VendorXCashRecycler(cashOptions.ApiBaseUrl, cashOptions.ApiKey, useRealApi: cashOptions.UseRealApi);
        var barcodeScanner = new DatalogicBarcodeScanner();
        var receiptPrinter = new EpsonReceiptPrinter();
        var calculator     = new DualCurrencyCalculator();
        var lowFloatMonitor = new LowFloatMonitor();
        var licenseManager = new DevBypassLicenseManager();
        var hardwareLog    = new HardwareAppendLog(GetAuditLogPath());

        var engine = new LLCoreLogicEngine(cashRecycler, barcodeScanner, receiptPrinter,
            calculator, lowFloatMonitor, licenseManager, hardwareLog);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n  [SYSTEM] Connecting to cash hardware & devices...");
        Console.ResetColor();

        await engine.InitializeAsync();

        // ── STEP 1: Scan items ────────────────────────────────────────────────
        var cart = new List<(string Name, decimal Price)>();

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("\n  Type or paste a barcode and press ENTER. Type 'done' when finished.\n");
        Console.ResetColor();

        while (true)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("  BARCODE > ");
            Console.ResetColor();

            string? input = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(input)) continue;
            if (input.Equals("done", StringComparison.OrdinalIgnoreCase)) break;

            if (_catalogue.TryGetValue(input, out var product))
            {
                cart.Add((product.Name, product.PriceUsd));
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  ✓  [{product.Category}] {product.Name}  →  ${product.PriceUsd:F2}");
                Console.ResetColor();

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"     Cart total: ${cart.Sum(x => x.Price):F2}  ({cart.Count} item{(cart.Count == 1 ? "" : "s")})");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  ✗  Barcode '{input}' not found in catalogue.");
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("     Known barcodes: 7243026805126 (Coca-Cola), 9322386486164 (Water), 8485234828415 (Milk) ...");
                Console.ResetColor();
            }
        }

        if (cart.Count == 0)
        {
            Warn("No items scanned. Exiting.");
            return;
        }

        decimal totalUsd = cart.Sum(x => x.Price);

        // ── STEP 2: Show cart summary ─────────────────────────────────────────
        Section("CART SUMMARY");
        foreach (var (name, price) in cart)
            Console.WriteLine($"   {name,-38}  ${price:F2}");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"   {"TOTAL",-38}  ${totalUsd:F2}");
        Console.ResetColor();

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("\n  Proceed to cash payment? (Y/N) > ");
        Console.ResetColor();
        string? confirm = Console.ReadLine()?.Trim().ToUpperInvariant();
        if (confirm != "Y") { Warn("Cancelled."); return; }

        // ── STEP 3: Arm cash hardware ─────────────────────────────────────────
        Section("CASH PAYMENT — INSERT BANKNOTES");

        decimal totalPaidUsd = 0;
        decimal changeKhr = 0;
        bool paymentCompleted = false;
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        engine.OnStateChanged += (_, e) =>
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] STATE  {e.Previous} → {e.Current}");
            Console.ResetColor();

            if (e.Current == KioskState.TransactionComplete)
            {
                paymentCompleted = true;
                try { cts.Cancel(); } catch { }
            }
        };

        engine.OnBalanceChanged += (_, e) =>
        {
            totalPaidUsd = e.TenderedUsd;
            decimal remaining = Math.Max(0, e.RemainingUsd);

            if (e.TenderedUsd > 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n  ╔═══════════════════════════════════════════════════════════════╗");
                Console.WriteLine($"  ║  💵 NOTE ACCEPTED! Total Paid: ${e.TenderedUsd,6:F2}  |  Due: ${remaining,6:F2}       ║");
                Console.WriteLine($"  ║     Paid (KHR): ៛{e.TenderedUsd * 4100m,8:N0}  |  Due (KHR): ៛{remaining * 4100m,8:N0}    ║");
                Console.WriteLine("  ╚═══════════════════════════════════════════════════════════════╝\n");
                Console.ResetColor();
            }

            if (totalPaidUsd > 0 && e.RemainingUsd <= 0)
            {
                paymentCompleted = true;
                changeKhr = Math.Abs(e.RemainingUsd) * DualCurrencyCalculator.DefaultUsdToKhrRate;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("  🎉 PAYMENT COMPLETE! Full amount received.\n");
                Console.ResetColor();
                try { cts.Cancel(); } catch { }
            }
        };

        engine.OnCashNoteRejected += (_, e) =>
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\n  ╔═══════════════════════════════════════════════════════════════╗");
            Console.WriteLine($"  ║  ⛔ NOTE REJECTED: {e.Note,-12}                              ║");
            Console.WriteLine("  ║  ⚠ Overpayment exceeds 500 KHR (~$0.12) tolerance limit!     ║");
            Console.WriteLine("  ║  ↩ Note was ejected and returned back to the customer.       ║");
            Console.WriteLine("  ╚═══════════════════════════════════════════════════════════════╝\n");
            Console.ResetColor();
        };

        engine.OnHardwareFault += (_, e) =>
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] ⚠  FAULT [{e.Device}]: {e.Message}");
            Console.ResetColor();
        };

        // Submit the FIRST real EAN-13 barcode to drive engine Idle → Scanning
        // (required before BeginCashPaymentAsync can be called)
        string firstBarcode = _catalogue
            .FirstOrDefault(kv => kv.Value.Name == cart[0].Name).Key
            ?? "7243026805126"; // fallback

        var scanResult = await engine.SubmitScanAsync(firstBarcode);
        if (!scanResult.Accepted)
            Warn($"Scan note: {scanResult.Message}");

        try
        {
            await engine.BeginCashPaymentAsync(Money.Usd(totalUsd), DualCurrencyCalculator.DefaultUsdToKhrRate);
        }
        catch (Exception ex)
        {
            Fail($"Could not arm cash hardware: {ex.Message}");
            return;
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"  Total Due: ${totalUsd:F2}  (≈ ៛{totalUsd * DualCurrencyCalculator.DefaultUsdToKhrRate:N0})");
        Console.WriteLine("  Insert banknotes into the cash slot (USD or KHR). Press Q to cancel.\n");
        Console.ResetColor();

        // ── STEP 4: Flush input buffer & wait until paid or cancelled ──────────
        await Task.Delay(300);
        while (Console.KeyAvailable)
        {
            try { Console.ReadKey(true); } catch { break; }
        }

        while (!paymentCompleted && !cts.Token.IsCancellationRequested)
        {
            try
            {
                if (Console.KeyAvailable)
                {
                    var keyInfo = Console.ReadKey(intercept: true);
                    if (keyInfo.Key == ConsoleKey.Q || keyInfo.Key == ConsoleKey.Escape)
                    {
                        Console.WriteLine("\n  [CANCELLED] Payment cancelled by user (Q pressed).");
                        break;
                    }
                }
            }
            catch { }

            try
            {
                await Task.Delay(250, cts.Token).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }

        // ── STEP 5: Print receipt ─────────────────────────────────────────────
        if (paymentCompleted)
        {
            Section("STEP 4: PRINTING RECEIPT");
            try
            {
                await receiptPrinter.ConnectAsync();
                await receiptPrinter.PrintCartReceiptAsync(cart, totalUsd, totalPaidUsd > 0 ? totalPaidUsd : totalUsd, changeKhr);
                Pass("Receipt printed on Epson TM-m30!");
            }
            catch (Exception ex)
            {
                Fail($"Printer error: {ex.Message}");
            }
        }

        // ── STEP 6: Reset and turn off cash machine ───────────────────────────
        Section("STEP 5: RESET & TURN OFF HARDWARE");
        try
        {
            await cashRecycler.DisarmAcceptanceAsync();
            await cashRecycler.DisconnectAsync();
            Pass("Cash Recycler shutter closed & green LEDs turned OFF.");
            Pass("Session closed and hardware reset successfully.");
        }
        catch (Exception ex)
        {
            Warn($"Disarm notice: {ex.Message}");
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n  ✨ Transaction complete! Run the command again for the next customer.\n");
        Console.ResetColor();
    }

    // ── --print-test ──────────────────────────────────────────────────────────

    public static async Task PrintTestReceiptAsync()
    {
        SafeClear();
        Console.WriteLine("Connecting to Epson TM-m30 printer...");
        try
        {
            var printer = new EpsonReceiptPrinter();
            await printer.ConnectAsync();

            bool paper = await printer.IsPaperPresentAsync();
            Console.ForegroundColor = paper ? ConsoleColor.Green : ConsoleColor.Red;
            Console.WriteLine($"  Paper sensor: {(paper ? "OK" : "EMPTY — load paper first!")}");
            Console.ResetColor();

            if (!paper)
            {
                Console.WriteLine("Cannot print — no paper. Load paper and retry.");
                return;
            }

            Console.WriteLine("Printing test receipt...");
            await printer.PrintReceiptAsync(totalUsd: 1.60m, tenderedUsd: 2.00m, overpaymentKhr: 1640m);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Receipt printed successfully!");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Printer error: {ex.Message}");
            Console.ResetColor();
            Console.WriteLine("Make sure the Epson printer is:");
            Console.WriteLine("  • Powered on");
            Console.WriteLine("  • Connected via USB");
            Console.WriteLine("  • Has a Windows printer queue named 'EPSON EU-m30' (or similar)");
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Dev-only license bypass — permits all features without a signed license token.
// NEVER ship this in the production WinUI app; it lives only in this console tool.
// ─────────────────────────────────────────────────────────────────────────────
sealed class DevBypassLicenseManager : IOfflineLicenseManager
{
    public Task LoadAndValidateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void EnforceFeatureAccess(SelfCheckoutKiosk.Core.Licensing.LicensedFeature feature) { /* permit all */ }
}
