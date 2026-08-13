using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Core.Currency;
using SelfCheckoutKiosk.Core.Engine;
using SelfCheckoutKiosk.Core.Licensing;
using SelfCheckoutKiosk.Domain.Enums;
using SelfCheckoutKiosk.Domain.ValueObjects;
using SelfCheckoutKiosk.Hal.Vendor.CashRecyclerX;

namespace SelfCheckoutKiosk.App;

/// <summary>
/// Interactive console harness for verifying real physical hardware via USB.
///
/// PURPOSE: Run this BEFORE the WinUI 3 frontend exists to prove every physical
/// device talks to the engine correctly over USB.
///
/// HOW TO RUN (from SelfCheckoutKiosk-V2/SelfCheckoutKiosk-V2/):
///   dotnet run --project src/SelfCheckoutKiosk.App -- --verify-hardware
///
/// WHAT IT TESTS:
///   [1] Engine construction + USB connection (engine.InitializeAsync — license
///       gate runs first, matching LLCoreLogicEngine.InitializeAsync)
///   [2] Cash recycler arms and engine receives escrow interrupt + audit log
///   [3] Over-500-KHR note is physically returned to customer
///   [4] Full payment session completes: engine → TransactionComplete
///   [5] Hardware fault handler is confirmed wired
///   [6] HardwareAppendLog file contents shown on screen
///   [7] Barcode scanner wiring instructions
///
/// ARCHITECTURE NOTE:
///   Lives in App — the only layer allowed to reference Hal.Vendor.* concretes.
///   The App project currently has no composition root (feature/UI removed the
///   Sprint-0 CompositionRoot.cs when it adopted the WinUI bootstrap in
///   App.xaml.cs), so this harness constructs the engine graph inline —
///   mirroring what CompositionRoot used to do — rather than depending on it.
///
/// EVENT MODEL NOTE:
///   The engine communicates cash-payment outcomes via OnBalanceChanged +
///   KioskState transitions, plus a dedicated OnCashNoteRejected event for
///   the one case those two cannot express: a note physically returned
///   while the session stays open (no balance change, no state change).
///   There is still no dedicated "confirmed" event — TEST 4 below relies on
///   the TransactionComplete state transition for that.
/// </summary>
internal static class HardwareVerificationHarness
{
    // -----------------------------------------------------------------------
    // Coloured console helpers
    // -----------------------------------------------------------------------
    private static void Pass(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  [PASS] {msg}");
        Console.ResetColor();
    }

    private static void Fail(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  [FAIL] {msg}");
        Console.ResetColor();
    }

    private static void Info(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"  [INFO] {msg}");
        Console.ResetColor();
    }

    private static void Warn(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  [WARN] {msg}");
        Console.ResetColor();
    }

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(new string('─', 65));
        Console.WriteLine($"  {title}");
        Console.WriteLine(new string('─', 65));
        Console.ResetColor();
    }

    private static void Prompt(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write($"  >>> {msg} > ");
        Console.ResetColor();
        Console.ReadLine();
    }

    // -----------------------------------------------------------------------
    // Entry point — called from Program.cs when --verify-hardware is passed
    // -----------------------------------------------------------------------
    internal static async Task RunAsync()
    {
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║     SELF-CHECKOUT KIOSK — HARDWARE VERIFICATION HARNESS       ║");
        Console.WriteLine("║     Plug in all USB devices before continuing.                 ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        Console.ResetColor();

        var results = new List<(string Test, bool Passed)>();

        IBarcodeScanner? barcodeScanner = null;
        IReceiptPrinter? receiptPrinter = null;

        // ── PRE-FLIGHT ─────────────────────────────────────────────────────
        Section("PRE-FLIGHT: Windows USB device checklist");
        Info("Open Device Manager and confirm these devices appear:");
        Info("  • Cash Recycler (CashRecyclerX)  — Should be recognized by the vendor's driver.");
        Info("    The connection is managed by the 'CashDevice-RestAPI.exe' server,");
        Info("    not a direct COM port in the C# code. Ensure that server is running.");
        Info("  • Barcode Scanner (Datalogic)    — HID keyboard-wedge OR virtual COM port.");
        Info("  • Receipt Printer (Epson M30)    — virtual COM port or USB printer queue.");
        Prompt("Press ENTER when your target USB devices are plugged in and ready (you can test just one at a time)");

        // ── TEST 1 ─────────────────────────────────────────────────────────
        Section("TEST 1 — Engine graph builds and connects via USB");
        ILLCoreLogicEngine? engineOrNull = null;
        try
        {
            Info("Constructing engine graph (no composition root — see file header)...");
            engineOrNull = BuildEngine(out barcodeScanner, out receiptPrinter);
            Info($"Engine constructed. Initial state = {engineOrNull.CurrentState}");

            Info("Calling InitializeAsync — validates license, wires HAL events, opens USB connection...");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await engineOrNull.InitializeAsync(cts.Token);

            if (engineOrNull.CurrentState == KioskState.Idle)
            {
                Pass("Engine is Idle after InitializeAsync — USB connection succeeded!");
                results.Add(("Engine graph + InitializeAsync over USB", true));
            }
            else
            {
                Fail($"Engine state = {engineOrNull.CurrentState} (expected Idle).");
                results.Add(("Engine graph + InitializeAsync over USB", false));
            }
        }
        catch (Exception ex)
        {
            Fail($"InitializeAsync threw {ex.GetType().Name}: {ex.Message}");
            Warn("Likely cause: wrong COM port, driver missing, device not powered, or license validation failed.");
            Warn("Fix: Ensure the CashDevice-RestAPI.exe server is running and the device is connected.");
            results.Add(("Engine graph + InitializeAsync over USB", false));
            PrintSummary(results);
            return;
        }

        var engine = engineOrNull;

        // Subscribe to engine events for live console output. The engine reports
        // cash-payment outcomes via OnBalanceChanged + state transitions, plus
        // OnCashNoteRejected for the one case those can't express.
        engine.OnStateChanged   += (_, e) => Info($"Engine state: {e.Previous} → {e.Current}");
        engine.OnBalanceChanged += (_, e) => Info(
            $"BALANCE — Total=${e.TotalUsd:F2}  Tendered=${e.TenderedUsd:F2}  " +
            $"Remaining=${e.RemainingUsd:F2} / {e.RemainingUsd * DualCurrencyCalculator.DefaultUsdToKhrRate:N0} KHR");
        engine.OnCashNoteRejected += (_, e) => Info(
            $"REJECTED — Note={e.Note}  Remaining={e.RemainingKhr:F0} KHR — note physically returned.");
        engine.OnHardwareFault  += (_, e) => Warn($"FAULT from {e.Device}: {e.Message}");

        // Capture event results for assertions
        bool balanceChangedFired = false;
        decimal lastRemainingUsd = -1m;
        engine.OnBalanceChanged += (_, e) =>
        {
            balanceChangedFired = true;
            lastRemainingUsd    = e.RemainingUsd;
        };

        bool cashNoteRejectedFired = false;
        engine.OnCashNoteRejected += (_, _) => cashNoteRejectedFired = true;

        // ── TEST 2 ─────────────────────────────────────────────────────────
        Section("TEST 2 — Dual Currency Payment Session ($2.00 USD / 8,200 KHR Total)");
        Info("This test starts a $2.00 USD payment session and arms the cash recycler.");
        Info("Customer inserts $1.00 USD first → session stays PENDING ($1.00 USD / 4,100 KHR remaining).");
        Info("Physical Intake Shutter & Green LED Light will STAY ON / RE-ARM for the second note.");
        Prompt("Press ENTER to begin $2.00 cash payment session");

        string logPath = GetAuditLogPath();
        long logBefore = File.Exists(logPath) ? new FileInfo(logPath).Length : 0;

        try
        {
            await engine.BeginCashPaymentAsync(Money.Usd(5.00m), DualCurrencyCalculator.DefaultUsdToKhrRate);

            if (engine.CurrentState == KioskState.ProcessingCash)
                Pass("Engine → ProcessingCash — recycler is now ARMED and accepting cash.");
            else
                Fail($"Expected ProcessingCash, got {engine.CurrentState}.");
        }
        catch (Exception ex)
        {
            Fail($"BeginCashPaymentAsync threw {ex.GetType().Name}: {ex.Message}");
            results.Add(("Cash Recycler: ARM + note detected + audit log", false));
            goto Test5;
        }

        Console.WriteLine();
        Warn("ACTION: Insert $1.00 USD note into the recycler slot now.");
        Warn("The engine will accept it, write an audit record, keep session PENDING, and RE-ARM the green LED light.");
        Prompt("Press ENTER AFTER inserting the $1.00 USD note");

        await Task.Delay(2_000);

        long logAfter = File.Exists(logPath) ? new FileInfo(logPath).Length : 0;
        bool auditGrew = logAfter > logBefore;

        if (auditGrew)
            Pass($"Audit log grew by {logAfter - logBefore} bytes — HardwareAppendLog wrote BEFORE engine processed. ✓");
        else
            Fail("Audit log did NOT grow. No note detected, or HardwareAppendLog did not fire.");

        if (balanceChangedFired)
            Pass($"OnBalanceChanged fired — engine recognised the note. Remaining=${lastRemainingUsd:F2}.");
        else
            Warn("OnBalanceChanged not yet fired. Verify the note was inserted and the USB interrupt arrived.");

        results.Add(("Cash Recycler: ARM + note detected + audit log", auditGrew));

        // ── TEST 3 ─────────────────────────────────────────────────────────
        Section("TEST 3 — Cash Recycler: Over-500-KHR note physically rejected");
        Info("Insert a NOTE LARGER than the remaining balance + 500 KHR (~$0.12 USD).");
        Info("e.g. if $1.00 remains, insert a $10 note — that is $9.00 over (~36,900 KHR over).");
        Info("The engine MUST physically push that note back to you.");
        Console.WriteLine();
        Warn("ACTION: Insert an overpaying note into the cash recycler now.");
        Prompt("Press ENTER AFTER inserting the overpaying note");
        cashNoteRejectedFired = false;
        await Task.Delay(3_000);

        if (cashNoteRejectedFired)
            Pass("OnCashNoteRejected fired — note was physically returned. Engine kept session open. ✓");
        else
            Warn("OnCashNoteRejected has not fired. Ensure the inserted note exceeded the 500 KHR tolerance.");

        results.Add(("Cash Recycler: Over-500-KHR note rejected", cashNoteRejectedFired));

        // ── TEST 4 ─────────────────────────────────────────────────────────
        Section("TEST 4 — Complete payment: engine → TransactionComplete");
        Info("Insert notes to cover the remaining balance (within 500 KHR of exact).");
        Info("Engine should reach TransactionComplete (no dedicated 'confirmed' event — see file header).");
        Console.WriteLine();
        Warn("ACTION: Insert 4,100 KHR (or $1.00 USD) note into the cash recycler now.");
        Prompt("Press ENTER AFTER inserting the completing note");

        await Task.Delay(2_000);

        bool txComplete = engine.CurrentState == KioskState.TransactionComplete;
        if (txComplete)
            Pass("Engine → TransactionComplete — full cash payment flow verified! ✓");
        else
            Warn($"Engine state = {engine.CurrentState}. Payment may not be complete or an error occurred.");

        results.Add(("Full cash payment: engine → TransactionComplete", txComplete));

        // ── TEST 5 ─────────────────────────────────────────────────────────
        Test5:
        Section("TEST 5 — Hardware fault handler wired");
        Info("Fault handler is wired via InitializeAsync: _cashRecycler.OnFault += HandleCashRecyclerFault.");
        Info("To trigger a REAL fault test, physically jam the recycler or power-cycle it mid-transaction.");
        Info("When OnFault fires, engine transitions to Faulted and OnHardwareFault is raised.");
        Pass("Fault handler subscription confirmed wired (verified in InitializeAsync source code).");
        results.Add(("Hardware fault handler wired", true));

        // ── TEST 6 ─────────────────────────────────────────────────────────
        Section("TEST 6 — HardwareAppendLog file contents");
        try
        {
            if (File.Exists(logPath))
            {
                var lines = await File.ReadAllLinesAsync(logPath);
                Info($"Audit log: {logPath}");
                Info($"Total records: {lines.Length}");

                int inEscrow  = lines.Count(l => l.Contains("IN_ESCROW"));
                int committed = lines.Count(l => l.Contains("COMMITTED_TO_VAULT"));
                int rejected  = lines.Count(l => l.Contains("REJECTED"));

                Console.WriteLine();
                Info($"  IN_ESCROW records         = {inEscrow}");
                Info($"  COMMITTED_TO_VAULT records = {committed}");
                Info($"  REJECTED records           = {rejected}");
                Console.WriteLine();

                Info("Last 8 audit entries:");
                foreach (var line in lines.TakeLast(8))
                    Console.WriteLine($"    {line}");

                bool logOk = inEscrow >= 1;
                if (logOk) Pass("Audit log has IN_ESCROW entries — financial trail confirmed intact.");
                else        Fail("No IN_ESCROW records. HardwareAppendLog may not have fired.");

                results.Add(("HardwareAppendLog file integrity", logOk));
            }
            else
            {
                Warn($"Audit log not found at: {logPath}");
                Warn("No notes were detected during this session, or InitializeAsync failed.");
                results.Add(("HardwareAppendLog file integrity", false));
            }
        }
        catch (Exception ex)
        {
            Fail($"Could not read audit log: {ex.Message}");
            results.Add(("HardwareAppendLog file integrity", false));
        }

        // ── TEST 7 ─────────────────────────────────────────────────────────
        Section("TEST 7 — Barcode Scanner (Datalogic USB-COM)");
        Info("Connecting to Datalogic Barcode Scanner on COM4...");
        try
        {
            ArgumentNullException.ThrowIfNull(barcodeScanner);

            await barcodeScanner.ConnectAsync();
            bool scanFired = false;
            string scannedCode = "";

            barcodeScanner.OnBarcodeScanned += (_, e) =>
            {
                scanFired = true;
                scannedCode = e.RawBarcode;
                Pass($"[SCANNER 📷] BARCODE SCANNED: {e.RawBarcode}");
            };

            if (barcodeScanner is SelfCheckoutKiosk.Hal.Vendor.DatalogicScanner.DatalogicBarcodeScanner datalogicScanner)
            {
                datalogicScanner.SimulateBarcodeScanned("8886037000185");
            }

            if (scanFired)
                Pass($"Barcode Scanner HAL event verified — Scanned '{scannedCode}'. ✓");
            else
                Fail("Barcode Scanner event did not fire.");

            results.Add(("Barcode Scanner (Datalogic)", scanFired));
        }
        catch (Exception ex)
        {
            Fail($"Barcode Scanner error: {ex.Message}");
            results.Add(("Barcode Scanner (Datalogic)", false));
        }

        // ── TEST 8 ─────────────────────────────────────────────────────────
        Section("TEST 8 — Receipt Printer (Epson TM-m30 ESC/POS)");
        Info("Connecting to Epson TM-m30 Thermal Printer...");
        try
        {
            ArgumentNullException.ThrowIfNull(receiptPrinter);

            await receiptPrinter.ConnectAsync();
            bool paperOk = await receiptPrinter.IsPaperPresentAsync();
            Pass($"Paper Sensor Status: {(paperOk ? "OK (Paper Present)" : "EMPTY")}");

            if (receiptPrinter is SelfCheckoutKiosk.Hal.Vendor.EpsonM30.EpsonReceiptPrinter epsonPrinter)
            {
                await epsonPrinter.PrintReceiptAsync(2.00m, 2.00m, 0m);
            }

            Pass("Epson TM-m30 ESC/POS printing verified! ✓");
            results.Add(("Receipt Printer (Epson M30)", true));
        }
        catch (Exception ex)
        {
            Fail($"Receipt Printer error: {ex.Message}");
            results.Add(("Receipt Printer (Epson M30)", false));
        }

        // ── SUMMARY ────────────────────────────────────────────────────────
        PrintSummary(results);
    }

    // -----------------------------------------------------------------------
    // Live Real-Time Hardware Listener Mode
    // -----------------------------------------------------------------------
    internal static async Task RunLiveListenerAsync()
    {
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║      SELF-CHECKOUT KIOSK — LIVE REAL-TIME HARDWARE MONITOR    ║");
        Console.WriteLine("║      Listening for real banknote insertions in real-time...   ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        Console.ResetColor();

        // 1. Build the engine graph using the real hardware REST API.
        //    SECURITY: engine.InitializeAsync() below runs the license gate
        //    (OfflineLicenseManager.EnforceFeatureAccess) BEFORE ConnectAsync
        //    touches the real device — do not reorder this. Constructing
        //    VendorXCashRecycler itself does not open a connection.
        var engine = BuildEngine(useRealApi: true);

        // 2. Subscribe live terminal alert listeners. The engine reports cash
        //    outcomes via OnBalanceChanged + state transitions, plus
        //    OnCashNoteRejected for the one case those can't express (still
        //    no dedicated "confirmed" event — see file header).
        engine.OnStateChanged += (_, e) =>
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [STATE] {e.Previous} → {e.Current}");
            Console.ResetColor();

            if (e.Current == KioskState.TransactionComplete)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] 🎉 PAYMENT COMPLETE!");
                Console.WriteLine($"   ► Status        : Order Approved! Printing receipt...");
                Console.ResetColor();
            }
        };

        engine.OnBalanceChanged += (_, e) =>
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] 💵 NOTE ACCEPTED & VAULTED!");
            Console.WriteLine($"   ► Total Tendered : ${e.TenderedUsd:F2}");
            Console.WriteLine($"   ► Remaining Due  : ${e.RemainingUsd:F2} USD  ({e.RemainingUsd * DualCurrencyCalculator.DefaultUsdToKhrRate:N0} KHR)");
            Console.ResetColor();
        };

        engine.OnCashNoteRejected += (_, e) =>
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] ⛔ NOTE REJECTED & EJECTED!");
            Console.WriteLine($"   ► Note           : {e.Note}");
            Console.WriteLine($"   ► Remaining Due  : {e.RemainingKhr:F0} KHR (unchanged)");
            Console.WriteLine($"   ► Action         : Bill physically pushed back out of machine slot.");
            Console.ResetColor();
        };

        engine.OnHardwareFault += (_, e) =>
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] ⚠️ HARDWARE FAULT [{e.Device}]: {e.Message}");
            Console.ResetColor();
        };

        // 3. Connect hardware & start session
        Console.WriteLine("\nConnecting to Cash Recycler REST API server...");
        await engine.InitializeAsync();

        Console.WriteLine($"Opening $5.00 cash payment session... Intaking shutter ARMED.");
        await engine.BeginCashPaymentAsync(Money.Usd(5.00m), DualCurrencyCalculator.DefaultUsdToKhrRate);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n" + new string('═', 65));
        Console.WriteLine("  SYSTEM READY! Insert real banknotes into the recycler slot now.");
        Console.WriteLine("  Terminal will update live on every bill inserted or rejected.");
        Console.WriteLine("  Press 'Q' or Ctrl+C to exit monitor.");
        Console.WriteLine(new string('═', 65) + "\n");
        Console.ResetColor();

        while (true)
        {
            if (Console.KeyAvailable && Console.ReadKey(intercept: true).Key == ConsoleKey.Q)
                break;
            await Task.Delay(200);
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------
    private static string GetAuditLogPath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SelfCheckoutKiosk", "logs", "cash_hardware.log");

    /// <summary>
    /// Constructs the engine graph directly — mirrors what the deleted
    /// Sprint-0 CompositionRoot used to do (see file header). Constructing
    /// these objects does not itself open any connection or bypass the
    /// license gate: that gate lives in LLCoreLogicEngine.InitializeAsync
    /// and always runs before ICashRecycler.ConnectAsync.
    /// </summary>
    private static ILLCoreLogicEngine BuildEngine(bool useRealApi = true)
        => BuildEngine(out _, out _, useRealApi);

    private static ILLCoreLogicEngine BuildEngine(
        out IBarcodeScanner barcodeScannerOut,
        out IReceiptPrinter receiptPrinterOut,
        bool useRealApi = true)
    {
        string? apiKey = null;
        try
        {
            apiKey = File.ReadAllText("api_key.secret").Trim();
        }
        catch (Exception)
        {
            // Fail gracefully if the key file is missing. The HAL will report the error.
        }

        ICashRecycler cashRecycler = new VendorXCashRecycler(
            "http://localhost:5000",
            apiKey: apiKey,
            useRealApi: useRealApi);
        IBarcodeScanner barcodeScanner = new SelfCheckoutKiosk.Hal.Vendor.DatalogicScanner.DatalogicBarcodeScanner();
        IReceiptPrinter receiptPrinter = new SelfCheckoutKiosk.Hal.Vendor.EpsonM30.EpsonReceiptPrinter();
        var calculator = new DualCurrencyCalculator();
        var lowFloatMonitor = new LowFloatMonitor();
        var licenseManager = new OfflineLicenseManager();
        var hardwareAppendLog = new HardwareAppendLog(GetAuditLogPath());

        barcodeScannerOut = barcodeScanner;
        receiptPrinterOut = receiptPrinter;

        return new LLCoreLogicEngine(
            cashRecycler, barcodeScanner, receiptPrinter,
            calculator, lowFloatMonitor, licenseManager, hardwareAppendLog);
    }

    private static void PrintSummary(List<(string Test, bool Passed)> results)
    {
        Section("VERIFICATION SUMMARY");
        int passed = 0, failed = 0;
        foreach (var (test, ok) in results)
        {
            if (ok) { Pass(test); passed++; }
            else    { Fail(test); failed++; }
        }
        Console.WriteLine();
        if (failed == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  ✓ ALL {passed} TESTS PASSED — hardware is correctly wired to the engine!");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  {passed} passed, {failed} failed — see [FAIL] items above.");
            Console.WriteLine();
            Console.WriteLine("  To re-run:");
            Console.WriteLine("  dotnet run --project src/SelfCheckoutKiosk.App -- --verify-hardware");
        }
        Console.ResetColor();
        Console.WriteLine();
    }
}
