using System;
using System.IO;
using System.Net.Http;
using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Core.Currency;
using SelfCheckoutKiosk.Core.Engine;
using SelfCheckoutKiosk.Core.Licensing;
using SelfCheckoutKiosk.Hal.Vendor.DatalogicScanner;
using SelfCheckoutKiosk.Hal.Vendor.EpsonM30;
using SelfCheckoutKiosk.Hal.Vendor.ItlRestCashRecycler;
using SelfCheckoutKiosk.Infrastructure.Data;
using SelfCheckoutKiosk.Infrastructure.Security;
using SelfCheckoutKiosk.Infrastructure.Sync;

namespace SelfCheckoutKiosk.App.Composition;

/// <summary>
/// Resolved services handed to the presentation layer.
/// </summary>
public sealed class KioskServices
{
    public required ILLCoreLogicEngine Engine { get; init; }
    public required HardwareAppendLog HardwareAppendLog { get; init; }
    public required TailscaleSyncWorker SyncWorker { get; init; }
    public required OfflineLicenseManager LicenseManager { get; init; }

    /// <summary>
    /// Exposed concretely (not just as <see cref="ICashRecycler"/>)
    /// so the caller can dispose its background REST-status polling loop on
    /// shutdown — <c>ICashRecycler</c> itself deliberately isn't
    /// <see cref="IDisposable"/>, since not every vendor SKU needs it.
    /// </summary>
    public required ItlRestCashRecycler CashRecycler { get; init; }
}

/// <summary>
/// THE CLEAN INTEGRATION PROTOCOL (Blueprint §6) — the single seam where
/// Category 1 (Core), Category 2 (HAL), and Category 3 (UI) formally meet.
/// This is the ONLY place vendor Hal.Vendor.* assemblies are referenced.
/// </summary>
public static class CompositionRoot
{
    public static KioskServices Build()
    {
        // 1. Core, hardware-independent. None touch a device at construction.
        var calculator = new DualCurrencyCalculator();
        var lowFloatMonitor = new LowFloatMonitor();
        var hardwareIdProvider = new HardwareIdProvider();

        var licenseManager = new OfflineLicenseManager(
            publicKeySubjectPublicKeyInfo: DevLicenseKeys.PublicKeyOrNull,
            hardwareIdProvider: hardwareIdProvider);

        string hardwareAuditLogPath = Path.Combine(AppContext.BaseDirectory, "hardware-audit.log");
        var hardwareAppendLog = new HardwareAppendLog(hardwareAuditLogPath);

        // 2. Concrete HAL adapters
        var cashRecycler = new ItlRestCashRecycler(new HttpClient { BaseAddress = new Uri("http://localhost:5000/") });
        IBarcodeScanner barcodeScanner = new DatalogicBarcodeScanner();
        IReceiptPrinter receiptPrinter = new EpsonReceiptPrinter();

        // 3. KioskDbContext via a FACTORY delegate
        string databasePath = Path.Combine(AppContext.BaseDirectory, "kiosk.db");
        Func<KioskDbContext> dbContextFactory = () => new KioskDbContext(databasePath, "dev-kiosk-passphrase");
        IProductCatalog productCatalog = new EfProductCatalog(dbContextFactory);

        using (KioskDbContext seedContext = dbContextFactory())
        {
            seedContext.EnsureSchemaCreated();
            CatalogSeeder.SeedIfEmpty(seedContext);
        }

        // 4. Engine last
        ILLCoreLogicEngine engine = new LLCoreLogicEngine(
            cashRecycler, barcodeScanner, receiptPrinter,
            calculator, lowFloatMonitor, licenseManager, hardwareAppendLog, productCatalog);

        // 5. TailscaleSyncWorker
        var erpClient = new HttpErpSyncClient(new HttpClient { BaseAddress = new Uri("https://localhost/erp-placeholder/") });
        var syncWorker = new TailscaleSyncWorker(dbContextFactory, erpClient, TimeSpan.FromMinutes(2));

        return new KioskServices
        {
            Engine = engine,
            HardwareAppendLog = hardwareAppendLog,
            SyncWorker = syncWorker,
            CashRecycler = cashRecycler,
            LicenseManager = licenseManager,
        };
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

    // 2. Validate license here (resolve + LoadAndValidateAsync) — hard stop on fail.

    // 3. Concrete HAL adapters (only vendor references live here)
    services.AddSingleton<ICashRecycler, VendorXCashRecycler>();
    services.AddSingleton<IBarcodeScanner, DatalogicBarcodeScanner>();
    services.AddSingleton<IReceiptPrinter, EpsonReceiptPrinter>();

    // 4. DbContext via factory delegate, NOT a singleton
    services.AddTransient(sp => new KioskDbContext(sealedPassphrase));

    // 5. Engine last, injected with interfaces from steps 1-3
    services.AddSingleton<ILLCoreLogicEngine, LLCoreLogicEngine>();

    // 6. ViewModels + Tailscale sync worker as hosted/scoped services
    //    (depend only on ILLCoreLogicEngine + the db factory)

    using var provider = services.BuildServiceProvider();
    // 7. await provider.GetRequiredService<ILLCoreLogicEngine>().InitializeAsync();
    --------------------------------------------------------------------------- */
