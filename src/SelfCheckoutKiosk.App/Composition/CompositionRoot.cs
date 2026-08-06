using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Core.Currency;
using SelfCheckoutKiosk.Core.Engine;
using SelfCheckoutKiosk.Core.Licensing;
using SelfCheckoutKiosk.Hal.Vendor.CashRecyclerX;
using SelfCheckoutKiosk.Hal.Vendor.DatalogicScanner;
using SelfCheckoutKiosk.Hal.Vendor.EpsonM30;

namespace SelfCheckoutKiosk.App.Composition;

/// <summary>Resolved services handed to the presentation layer.</summary>
public sealed class KioskServices
{
    public required ILLCoreLogicEngine Engine { get; init; }
}

/// <summary>
/// THE CLEAN INTEGRATION PROTOCOL (Blueprint §6) — the single seam where
/// Category 1 (Core), Category 2 (HAL), and Category 3 (UI) formally meet.
/// This is the ONLY place vendor Hal.Vendor.* assemblies are referenced.
///
/// Manual composition is used deliberately: under a strict Native AOT mandate
/// it is the cleanest option (no reflection-based container to fight the
/// trimmer). The equivalent Microsoft.Extensions.DependencyInjection wiring is
/// documented at the bottom of this file for teams that prefer container DI.
/// </summary>
public static class CompositionRoot
{
    public static KioskServices Build()
    {
        // 1. Core, hardware-independent. None touch a device at construction.
        var calculator      = new DualCurrencyCalculator();
        var lowFloatMonitor = new LowFloatMonitor();
        var licenseManager  = new OfflineLicenseManager();

        // 2. TODO(Lead): validate the license BEFORE any hardware is created.
        //    await licenseManager.LoadAndValidateAsync();  // failure = hard stop.

        // 3. Hardware append log — must be on the BitLocker-protected OS volume
        //    (Blueprint §5). Path is injected here, not hard-coded in HardwareAppendLog.
        //    TODO(Lead/Ops): confirm final log directory after OS hardening.
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SelfCheckoutKiosk", "logs", "cash_hardware.log");
        var hardwareAppendLog = new HardwareAppendLog(logPath);

        // 4. Concrete HAL adapters — the ONLY vendor-assembly references anywhere.
        ICashRecycler   cashRecycler   = new VendorXCashRecycler();
        IBarcodeScanner barcodeScanner = new DatalogicBarcodeScanner();
        IReceiptPrinter receiptPrinter = new EpsonReceiptPrinter();

        // 5. TODO(Back-End): construct KioskDbContext via a FACTORY delegate
        //    (EF contexts are cheap; do not hold one open for process lifetime).

        // 6. Engine last — it receives interfaces, never concrete adapters, so it
        //    never knows which vendor SKU it got.
        ILLCoreLogicEngine engine = new LLCoreLogicEngine(
            cashRecycler, barcodeScanner, receiptPrinter,
            calculator, lowFloatMonitor, licenseManager, hardwareAppendLog);

        // 7. TODO(UI): construct ViewModels + Tailscale sync worker, each
        //    depending ONLY on the engine (+ db factory) — never on a HAL type.

        return new KioskServices { Engine = engine };
    }
}

/*  ---------------------------------------------------------------------------
    EQUIVALENT Microsoft.Extensions.DependencyInjection WIRING (Blueprint §6)
    Add package: Microsoft.Extensions.DependencyInjection, then:

    var services = new ServiceCollection();

    // 1. Core singletons
    services.AddSingleton<DualCurrencyCalculator>();
    services.AddSingleton<LowFloatMonitor>();
    services.AddSingleton<OfflineLicenseManager>();
    services.AddSingleton(sp => new HardwareAppendLog(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SelfCheckoutKiosk", "logs", "cash_hardware.log")));

    // 2. Validate license here (resolve + LoadAndValidateAsync) — hard stop on fail.

    // 3. Concrete HAL adapters (only vendor references live here)
    services.AddSingleton<ICashRecycler, VendorXCashRecycler>();
    services.AddSingleton<IBarcodeScanner, DatalogicBarcodeScanner>();
    services.AddSingleton<IReceiptPrinter, EpsonReceiptPrinter>();

    // 4. DbContext via factory delegate, NOT a singleton
    services.AddTransient(sp => new KioskDbContext(sealedPassphrase));

    // 5. Engine last, injected with interfaces from steps 1-4
    services.AddSingleton<ILLCoreLogicEngine, LLCoreLogicEngine>();

    // 6. ViewModels + Tailscale sync worker as hosted/scoped services
    //    (depend only on ILLCoreLogicEngine + the db factory)

    using var provider = services.BuildServiceProvider();
    // 7. await provider.GetRequiredService<ILLCoreLogicEngine>().InitializeAsync();
    --------------------------------------------------------------------------- */
