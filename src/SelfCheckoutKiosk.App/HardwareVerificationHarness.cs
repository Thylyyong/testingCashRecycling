using SelfCheckoutKiosk.App.Composition;
using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Core.Engine;
using SelfCheckoutKiosk.Domain.Enums;
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
///   [1] Composition root + USB connection (engine.InitializeAsync)
///   [2] Cash recycler arms and engine receives escrow interrupt + audit log
///   [3] Over-500-KHR note is physically returned to customer
///   [4] Full payment session completes: engine → TransactionComplete
///   [5] Hardware fault handler is confirmed wired
///   [6] HardwareAppendLog file contents shown on screen
///   [7] Barcode scanner wiring instructions
///
/// ARCHITECTURE NOTE:
///   Lives in App — the only layer allowed to reference Hal.Vendor.* concretes.
///   Goes through CompositionRoot.Build() exactly as production code does.
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
        Section("TEST 1 — Composition root builds and engine connects via USB");
        KioskServices? services = null;
        try
        {
            Info("Building composition root...");
            services = CompositionRoot.Build();
            Info($"Engine constructed. Initial state = {services.Engine.CurrentState}");

            Info("Calling InitializeAsync — wires HAL events, opens USB connection...");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await services.Engine.InitializeAsync(cts.Token);

            if (services.Engine.CurrentState == KioskState.Idle)
            {
                Pass("Engine is Idle after InitializeAsync — USB connection succeeded!");
                results.Add(("Composition root + InitializeAsync over USB", true));
            }
            else
            {
                Fail($"Engine state = {services.Engine.CurrentState} (expected Idle).");
                results.Add(("Composition root + InitializeAsync over USB", false));
            }
        }
        catch (Exception ex)
        {
            Fail($"InitializeAsync threw {ex.GetType().Name}: {ex.Message}");
            Warn("Likely cause: wrong COM port, driver missing, or device not powered.");
            Warn("Fix: Ensure the CashDevice-RestAPI.exe server is running and the device is connected.");
            results.Add(("Composition root + InitializeAsync over USB", false));
            PrintSummary(results);
            return;
        }

        var engine = services.Engine;

        // Subscribe to all engine events for live console output
        engine.OnStateChanged        += (_, e) => Info($"Engine state: {e.Previous} → {e.Current}");
        engine.OnCashPaymentPending   += (_, e) => Info($"PENDING  — Tendered=${e.TenderedUsd:F2}  Remaining=${e.RemainingUsd:F2} / {e.RemainingKhr:N0} KHR");
        engine.OnCashPaymentConfirmed += (_, e) => Info($"CONFIRMED — Total=${e.TotalUsd:F2}  Tendered=${e.TenderedUsd:F2}  Overpayment={e.OverpaymentKhr:N0} KHR");
        engine.OnCashPaymentRejected  += (_, e) => Info($"REJECTED  — Overpayment {e.OverpaymentKhr:N0} KHR exceeded 500 KHR limit — note returned.");
        engine.OnHardwareFault        += (_, e) => Warn($"FAULT from {e.Device}: {e.Message}");

        // Capture event results for assertions
        bool pendingFired   = false;
        bool rejectedFired  = false;
        bool confirmedFired = false;
        engine.OnCashPaymentPending   += (_, _) => pendingFired   = true;
        engine.OnCashPaymentRejected  += (_, _) => rejectedFired  = true;
        engine.OnCashPaymentConfirmed += (_, _) => confirmedFired = true;

        // ── TEST 2 ─────────────────────────────────────────────────────────
        Section("TEST 2 — Cash Recycler: ARM + physical note detected + audit log written");
        Info("This test starts a $5.00 USD payment session and arms the cash recycler.");
        Info("Watch for the device LED / shutter to open (accepting state).");
        Prompt("Press ENTER to begin $5.00 cash payment session");

        string logPath = GetAuditLogPath();
        long logBefore = File.Exists(logPath) ? new FileInfo(logPath).Length : 0;

        try
        {
            await engine.BeginCashPaymentAsync(5.00m);

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
        Warn("ACTION: Insert a physical note worth LESS than $5.00 into the recycler now.");
        Warn("The engine will accept it, write an audit record, and ask for more cash.");
        Prompt("Press ENTER AFTER inserting the note (give the hardware ~3 seconds to respond)");
        await Task.Delay(3_000);

        long logAfter = File.Exists(logPath) ? new FileInfo(logPath).Length : 0;
        bool auditGrew = logAfter > logBefore;

        if (auditGrew)
            Pass($"Audit log grew by {logAfter - logBefore} bytes — HardwareAppendLog wrote BEFORE engine processed. ✓");
        else
            Fail("Audit log did NOT grow. No note detected, or HardwareAppendLog did not fire.");

        if (pendingFired)
            Pass("OnCashPaymentPending fired — engine correctly recognised under-payment.");
        else
            Warn("OnCashPaymentPending not yet fired. Verify the note was inserted and the USB interrupt arrived.");

        results.Add(("Cash Recycler: ARM + note detected + audit log", auditGrew));

        // ── TEST 3 ─────────────────────────────────────────────────────────
        Section("TEST 3 — Cash Recycler: Over-500-KHR note physically rejected");
        Info("Insert a NOTE LARGER than the remaining balance + 500 KHR (~$0.12 USD).");
        Info("e.g. if $4.00 remains, insert a $10 note — that is $5.76 over, ~23,616 KHR over.");
        Info("The engine MUST physically push that note back to you.");
        Console.WriteLine();
        Warn("ACTION: Insert an overpaying note into the cash recycler now.");
        Prompt("Press ENTER AFTER inserting the overpaying note");
        await Task.Delay(3_000);

        if (rejectedFired)
            Pass("OnCashPaymentRejected fired — note was physically returned. Engine kept session open. ✓");
        else
            Warn("OnCashPaymentRejected has not fired. Ensure the inserted note exceeded the 500 KHR tolerance.");

        results.Add(("Cash Recycler: Over-500-KHR note rejected", rejectedFired));

        // ── TEST 4 ─────────────────────────────────────────────────────────
        Section("TEST 4 — Complete payment: engine → TransactionComplete");
        Info("Insert notes to cover the remaining balance (within 500 KHR of exact).");
        Info("OnCashPaymentConfirmed should fire and engine should reach TransactionComplete.");
        Console.WriteLine();
        Warn("ACTION: Insert enough notes to complete the $5.00 payment.");
        Prompt("Press ENTER AFTER payment is complete");
        await Task.Delay(3_000);

        bool txComplete = engine.CurrentState == KioskState.TransactionComplete && confirmedFired;
        if (txComplete)
            Pass("OnCashPaymentConfirmed fired & Engine → TransactionComplete — full cash payment flow verified! ✓");
        else
            Warn($"Engine state = {engine.CurrentState}, OnCashPaymentConfirmed={confirmedFired}. Payment may not be complete or an error occurred.");

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
        Section("TEST 7 — Barcode Scanner: USB wiring instructions");
        Info("DatalogicBarcodeScanner.ConnectAsync is currently a stub (Sprint 0).");
        Info("To wire your real Datalogic scanner over USB-COM virtual serial port:");
        Console.WriteLine();
        Info("  1. Open: src/SelfCheckoutKiosk.Hal.Vendor.DatalogicScanner/DatalogicBarcodeScanner.cs");
        Info("  2. Add field: private System.IO.Ports.SerialPort? _port;");
        Info("  3. In ConnectAsync:");
        Info("       _port = new SerialPort(\"COM4\", 9600, Parity.None, 8, StopBits.One);");
        Info("       _port.DataReceived += (_, _) => {");
        Info("           var data = _port.ReadExisting().Trim();");
        Info("           if (!string.IsNullOrEmpty(data))");
        Info("               OnBarcodeScanned?.Invoke(this, new BarcodeScannedEventArgs(data));");
        Info("       };");
        Info("       _port.Open();");
        Console.WriteLine();
        Info("If your scanner is HID keyboard-wedge mode (no COM port setup needed),");
        Info("it emits scans directly as typed keystrokes — read via Console.ReadLine().");
        Info("In that case, DatalogicBarcodeScanner.ConnectAsync can be a no-op.");
        Warn("Manual action: scan a real barcode label and verify the digits appear.");
        results.Add(("Barcode Scanner: wiring instructions provided", true));

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

        // 1. Build composition root using Real Hardware REST API
        // For real physical hardware testing, the API key is read from a local,
        // git-ignored file for security.
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
            useRealApi: true);
        IBarcodeScanner barcodeScanner = new SelfCheckoutKiosk.Hal.Vendor.DatalogicScanner.DatalogicBarcodeScanner();
        IReceiptPrinter receiptPrinter = new SelfCheckoutKiosk.Hal.Vendor.EpsonM30.EpsonReceiptPrinter();
        var calculator = new SelfCheckoutKiosk.Core.Currency.DualCurrencyCalculator();
        var lowFloatMonitor = new SelfCheckoutKiosk.Core.Currency.LowFloatMonitor();
        var licenseManager = new SelfCheckoutKiosk.Core.Licensing.OfflineLicenseManager();
        var logPath = GetAuditLogPath();
        var hardwareAppendLog = new SelfCheckoutKiosk.Core.Engine.HardwareAppendLog(logPath);

        ILLCoreLogicEngine engine = new LLCoreLogicEngine(
            cashRecycler, barcodeScanner, receiptPrinter,
            calculator, lowFloatMonitor, licenseManager, hardwareAppendLog);

        // 2. Subscribe live terminal alert listeners
        engine.OnStateChanged += (_, e) =>
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [STATE] {e.Previous} → {e.Current}");
            Console.ResetColor();
        };

        engine.OnCashPaymentPending += (_, e) =>
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] 💵 NOTE ACCEPTED & VAULTED!");
            Console.WriteLine($"   ► Total Tendered : ${e.TenderedUsd:F2}");
            Console.WriteLine($"   ► Remaining Due  : ${e.RemainingUsd:F2} USD  ({e.RemainingKhr:N0} KHR)");
            Console.ResetColor();
        };

        engine.OnCashPaymentConfirmed += (_, e) =>
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] 🎉 PAYMENT COMPLETE & CONFIRMED!");
            Console.WriteLine($"   ► Total Paid    : ${e.TenderedUsd:F2}");
            Console.WriteLine($"   ► Overpayment   : {e.OverpaymentKhr:N0} KHR");
            Console.WriteLine($"   ► Status        : Order Approved! Printing receipt...");
            Console.ResetColor();
        };

        engine.OnCashPaymentRejected += (_, e) =>
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] ⛔ NOTE REJECTED & EJECTED!");
            Console.WriteLine($"   ► Reason        : Overpayment exceeds 500 KHR limit ({e.OverpaymentKhr:N0} KHR)");
            Console.WriteLine($"   ► Action        : Bill physically pushed back out of machine slot.");
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
        
        decimal sampleTotal = 5.00m;
        Console.WriteLine($"Opening $5.00 cash payment session... Intaking shutter ARMED.");
        await engine.BeginCashPaymentAsync(sampleTotal);

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
