using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Core.Currency;
using SelfCheckoutKiosk.Core.Engine;
using SelfCheckoutKiosk.Core.Licensing;
using SelfCheckoutKiosk.Hal.Vendor.CashRecyclerX;
using SelfCheckoutKiosk.Hal.Vendor.DatalogicScanner;
using SelfCheckoutKiosk.Hal.Vendor.EpsonM30;
using SelfCheckoutKiosk.Infrastructure.Data;
using SelfCheckoutKiosk.Infrastructure.Data.Licensing;
using SelfCheckoutKiosk.Infrastructure.Security;
using SelfCheckoutKiosk.Infrastructure.Security.Licensing;

namespace SelfCheckoutKiosk.App.Composition;

/// <summary>
/// Resolved services handed to the presentation layer.
/// </summary>
public sealed class KioskServices
{
    public required ILLCoreLogicEngine Engine { get; init; }
    public required Func<KioskDbContext> CreateDbContext { get; init; }
}

/// <summary>
/// THE CLEAN INTEGRATION PROTOCOL (Blueprint §6) — the single seam where
/// Category 1 (Core), Category 2 (HAL), and Category 3 (UI) formally meet.
/// This is the ONLY place vendor Hal.Vendor.* assemblies are referenced.
///
/// Manual composition is used deliberately: under the Native AOT mandate
/// (Directory.Build.props) it is the cleanest option — no reflection-based
/// container to fight the trimmer.
///
/// Registration only. Nothing here does I/O — Build() never throws for
/// hardware/license reasons. That happens in LLCoreLogicEngine.InitializeAsync(),
/// called separately by the caller so failure can be handled explicitly
/// (see App.xaml.cs — blocking FaultView on failure, not a silent fallback).
/// </summary>
public static class CompositionRoot
{
    // -------------------------------------------------------------------
    // SECURITY (TODO — Lead): this is a throwaway placeholder public key.
    // Its matching private key was generated once, used only to produce
    // this PEM, and discarded — it was never saved anywhere. No real
    // license token can ever verify against it, so licensing fails closed
    // (correctly) until the Lead embeds the real signing service's public
    // key here.
    // -------------------------------------------------------------------
    private const string PlaceholderLicensePublicKeyPem =
        """
        -----BEGIN PUBLIC KEY-----
        MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAyGdeRvRhKk2WSwA9wtBm
        hXL9LGjW4qh9fRRXl5JE1UURGueeS/MoMYDG2mqOx96XTkKMSH2duIed5aeuQxQE
        mMJPVOqFbpTLuING07dPtdrhYygbXv7D+A/B00ewa0mjgT6TXD+FRjcfwAzf17dX
        F6fv1YnZSNkjJKs9jR1WVvEi3t1AdGSLTFuoSsx9XQJ0VIspm84ZykrqisxH6A8X
        W7z8zbTVa+ef5jaSy78+JSdhIN1lt7UPDvFFALmtXH+v/nIG0gU/icU0QO/jFTIe
        e/uhpdC6UFEZyKQ/1OoRnjlZBhEvEuN3Q+IntZs9LJCv8Nc93qNysuSlSv+pTTvb
        8QIDAQAB
        -----END PUBLIC KEY-----
        """;

    // -------------------------------------------------------------------
    // SECURITY (TODO — Lead): this is a placeholder SQLCipher passphrase.
    // It must be replaced with a sealed, per-kiosk secret before any real
    // deployment — do not ship this constant.
    // -------------------------------------------------------------------
    private const string PlaceholderDatabaseEncryptionKey =
        "TODO-Lead-inject-sealed-passphrase";

    public static KioskServices Build()
    {
        // 1. Core, hardware-independent. None touch a device at construction.
        var calculator = new DualCurrencyCalculator();
        var lowFloatMonitor = new LowFloatMonitor();

        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SelfCheckoutKiosk", "logs", "cash_hardware.log");
        var hardwareAppendLog = new HardwareAppendLog(logPath);

        // 2. Licensing. Real Infrastructure implementations, not the
        //    fail-closed parameterless OfflineLicenseManager() — but this
        //    is still expected to fail LoadAndValidateAsync() on any kiosk
        //    without a real, signed, hardware-matched license row. That
        //    failure is intentional and must surface (see App.xaml.cs).
        var connectionFactory = new KioskDatabaseConnectionFactory();
        var dbContextFactory = new KioskDbContextFactory(connectionFactory);

        var databasePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SelfCheckoutKiosk", "data", "kiosk.db");

        KioskDbContext CreateDbContext() =>
            dbContextFactory.Create(databasePath, PlaceholderDatabaseEncryptionKey);

        var hardwareIdProvider = new HardwareIdProvider(new WindowsTpmHardwareIdentityMaterialSource());
        var signatureVerifier = new RsaLicenseSignatureVerifier(PlaceholderLicensePublicKeyPem);
        var licenseSource = new LicenseConfigurationSource(CreateDbContext());

        var licenseManager = new OfflineLicenseManager(
            licenseSource,
            signatureVerifier,
            hardwareIdProvider);

        // 3. Concrete HAL adapters — the ONLY vendor-assembly references anywhere.
        ICashRecycler cashRecycler = new VendorXCashRecycler("http://localhost:5000", apiKey: null, useRealApi: true);
        IBarcodeScanner barcodeScanner = new DatalogicBarcodeScanner();
        IReceiptPrinter receiptPrinter = new EpsonReceiptPrinter();

        // 4. KioskDbContext is registered as a factory delegate (above),
        //    never a direct singleton — EF contexts are cheap to create
        //    and shouldn't be held open for the process lifetime.

        // 5. Engine last — it receives interfaces, never concrete adapters, so it
        //    never knows which vendor SKU it got.
        ILLCoreLogicEngine engine = new LLCoreLogicEngine(
            cashRecycler, barcodeScanner, receiptPrinter,
            calculator, lowFloatMonitor, licenseManager, hardwareAppendLog);

        // 6. ViewModels + Tailscale sync worker: not registered here yet.
        //    TailscaleSyncWorker.RunOnceAsync() is still a NotImplementedException
        //    stub with no IHostedService/generic-host infrastructure in this
        //    WinUI app — wiring it as a "hosted service" needs that host added
        //    first. Deferred; not part of this phase.

        return new KioskServices
        {
            Engine = engine,
            CreateDbContext = CreateDbContext
        };
    }
}
