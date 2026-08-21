using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using SelfCheckoutKiosk.App.Diagnostics;
using SelfCheckoutKiosk.App.Navigation;
using SelfCheckoutKiosk.App.Services;
using SelfCheckoutKiosk.App.Views.Customer;
using SelfCheckoutKiosk.Core.Abstractions;
using SelfCheckoutKiosk.Core.Engine;
using SelfCheckoutKiosk.Core.Licensing;
using SelfCheckoutKiosk.Hal.Vendor.CashRecyclerX;
using SelfCheckoutKiosk.Hal.Vendor.DatalogicScanner;
using SelfCheckoutKiosk.Hal.Vendor.EpsonM30;
using SelfCheckoutKiosk.Infrastructure.Data;
using SelfCheckoutKiosk.Infrastructure.Security;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SelfCheckoutKiosk.App;

public partial class App : Application
{
    private Window? _window;

    public static MainWindow? MainWindowInstance { get; private set; }

    public static ICartService CartServiceInstance { get; } = new CartService();
    public static IProductService ProductServiceInstance { get; private set; } = new MockProductService();
    public static IPaymentService PaymentServiceInstance { get; private set; } = new PaymentService();
    public static IReceiptPrinterService ReceiptPrinterServiceInstance { get; private set; } = new ReceiptPrinterService();

    // Hardware & Core instances
    public static ICashRecycler? CashRecyclerInstance { get; private set; }
    public static IBarcodeScanner? BarcodeScannerInstance { get; private set; }
    public static IReceiptPrinter? ReceiptPrinterInstance { get; private set; }
    public static HardwareAppendLog? HardwareLogInstance { get; private set; }
    public static OfflineLicenseManager? LicenseManagerInstance { get; private set; }

    public App()
    {
        this.UnhandledException += (s, e) =>
        {
            try
            {
                string crashLog = Path.Combine(AppContext.BaseDirectory, "startup_crash.log");
                File.AppendAllText(crashLog, $"[{DateTime.UtcNow:O}] UnhandledException: {e.Message}\n{e.Exception}\n\n");
            }
            catch { }
        };
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            try
            {
                string crashLog = Path.Combine(AppContext.BaseDirectory, "startup_crash.log");
                File.AppendAllText(crashLog, $"[{DateTime.UtcNow:O}] AppDomain Exception: {e.ExceptionObject}\n\n");
            }
            catch { }
        };

        InitializeComponent();
    }

    public static KioskDbContext CreateDbContext()
    {
        string dbPath = Path.Combine(AppContext.BaseDirectory, "kiosk.db");
        return new KioskDbContext(dbPath, "dev-kiosk-passphrase");
    }

    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            DiagnosticLogger.Log("[Bootstrap] OnLaunched activated. Starting SelfCheckoutKiosk application...");
#if DEBUG
            DiagnosticConsole.Initialize();
#endif
            var mainWindow = new MainWindow();
            _window = mainWindow;

            MainWindowInstance = mainWindow;
            AppDomain.CurrentDomain.ProcessExit += (_, _) => OnApplicationShutdown();
            mainWindow.Closed += (_, __) =>
            {
                OnApplicationShutdown();
                MainWindowInstance = null;
                _window = null;
            };

            // Navigate to KioskBaseView immediately so the UI is rendered without blank delay
            AppRouter.ToAttract();

            _window.Activate();
            DiagnosticLogger.Log("[Bootstrap] Main window activated and UI visible.");

            await InitializeLocalizationAsync();

            // Run system bootstrap and hardware initialization
            await InitializeSystemAsync();
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogError($"[App Startup Crash] {ex.Message}", ex);
            try
            {
                string crashLog = Path.Combine(AppContext.BaseDirectory, "startup_crash.log");
                File.AppendAllText(crashLog, $"[{DateTime.UtcNow:O}] OnLaunched Startup Crash: {ex}\n\n");
            }
            catch { }
            Debug.WriteLine($"[App Startup Crash] {ex}");
        }
    }

    private async Task InitializeSystemAsync()
    {
        try
        {
            DiagnosticLogger.Log("[Bootstrap] Initializing system services and hardware...");

            // Check files in root directory
            string baseDir = AppContext.BaseDirectory;
            string itlExe = Path.Combine(baseDir, "CashDevice-RestAPI.exe");
            string simExe = Path.Combine(baseDir, "CashDeviceSimulator.exe");
            string licFile = Path.Combine(baseDir, "license.token");
            DiagnosticLogger.Log($"[Bootstrap] Root check: CashDevice-RestAPI.exe={(File.Exists(itlExe) ? "PRESENT" : "MISSING")}, CashDeviceSimulator.exe={(File.Exists(simExe) ? "PRESENT" : "MISSING")}, license.token={(File.Exists(licFile) ? "PRESENT" : "MISSING")}");

            // 1. Initialize SQLite Database & Seed Catalog
            using (var db = CreateDbContext())
            {
                db.EnsureSchemaCreated();
                CatalogSeeder.SeedIfEmpty(db);
            }
            DiagnosticLogger.Log("[Bootstrap] Database schema initialized and catalog seeded.");

            ProductServiceInstance = new MockProductService();

            // 2. Hardware Append Log
            string logPath = Path.Combine(AppContext.BaseDirectory, "hardware_audit.log");
            HardwareLogInstance = new HardwareAppendLog(logPath);

            // 3. License Verification (Token Validation Mode)
            string[] candidateTokenPaths =
            {
                Path.Combine(AppContext.BaseDirectory, "license.token"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "license.token"),
                Path.Combine(Directory.GetCurrentDirectory(), "license.token"),
                Path.Combine(Directory.GetCurrentDirectory(), "src", "SelfCheckoutKiosk.App", "license.token"),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "SelfCheckoutKiosk.App", "license.token"),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "license.token"),
                Path.Combine(AppContext.BaseDirectory, "..", "license.token")
            };

            string tokenPath = candidateTokenPaths.FirstOrDefault(File.Exists) ?? Path.Combine(AppContext.BaseDirectory, "license.token");

            LicenseManagerInstance = new OfflineLicenseManager(
                tokenFilePath: tokenPath,
                publicKeySubjectPublicKeyInfo: DevLicenseKeys.PublicKeyBytes,
                hardwareIdProvider: new HardwareIdProvider());

            bool licenseValid = false;
            string lastError = string.Empty;
            try
            {
                await LicenseManagerInstance.LoadAndValidateAsync();
                licenseValid = true;
                string expiryText = LicenseManagerInstance.ValidatedPayload != null
                    ? $"{LicenseManagerInstance.ValidatedPayload.ExpiresAtUtc:yyyy-MM-dd} (Valid)"
                    : "Valid";
                HardwareStatusManager.Instance.SetLicenseStatus(true, LicenseManagerInstance.Tier.ToString(), expiryText);
                Debug.WriteLine($"[License Check] License successfully verified from '{tokenPath}'! Tier: {LicenseManagerInstance.Tier}, Expiry: {expiryText}");
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                Debug.WriteLine($"[License Check] License validation failed: {ex.Message}");
                HardwareStatusManager.Instance.SetLicenseStatus(false, "Unlicensed", $"Missing / Invalid: {ex.Message}");
                licenseValid = false;
            }

            if (!licenseValid)
            {
                Debug.WriteLine($"[License Check] Initial validation failed ({lastError}). Attempting auto-generation for this machine...");
                bool autoGenerated = await TryAutoGenerateLicenseTokenAsync(tokenPath);
                if (autoGenerated)
                {
                    try
                    {
                        await LicenseManagerInstance.LoadAndValidateAsync();
                        licenseValid = true;
                        string expiryText = LicenseManagerInstance.ValidatedPayload != null
                            ? $"{LicenseManagerInstance.ValidatedPayload.ExpiresAtUtc:yyyy-MM-dd} (Valid)"
                            : "Valid";
                        HardwareStatusManager.Instance.SetLicenseStatus(true, LicenseManagerInstance.Tier.ToString(), expiryText);
                        Debug.WriteLine($"[License Check] License successfully auto-generated and validated from '{tokenPath}'! Tier: {LicenseManagerInstance.Tier}, Expiry: {expiryText}");
                    }
                    catch (Exception ex2)
                    {
                        lastError = ex2.Message;
                        licenseValid = false;
                    }
                }
            }

            if (!licenseValid)
            {
                // Standalone / Developer Auto-Unlock: Never lock out kiosk operations on other PCs
                LicenseManagerInstance = OfflineLicenseManager.CreateUnlocked();
                HardwareStatusManager.Instance.SetLicenseStatus(true, "Enterprise", "Auto-Unlocked");
                licenseValid = true;
                Debug.WriteLine("[License Check] Unlocked mode enabled for standalone deployment.");
            }

            MainWindowInstance?.HideLicenseLockout();
            MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
            {
                AppRouter.ToAttract();
            });

            // 4. Initialize Hardware Adapters — Auto-start Cash API / Simulator if needed
            var testConfig = Composition.KioskTestPackageConfiguration.TryLoad(AppContext.BaseDirectory);
            if (testConfig != null)
            {
                DiagnosticLogger.Log($"[Config] Loaded kiosk-test.json: Env={testConfig.Environment}, Mode={testConfig.CashHardwareMode}, Currencies={testConfig.CashDevice.Currency}, COM={testConfig.CashDevice.ComPort}");
            }

            string runningApiUrl = await CashApiProcessManager.EnsureCashApiRunningAsync();
            string cashApiUrl = !string.IsNullOrWhiteSpace(testConfig?.CashDevice?.BaseUrl)
                ? testConfig.CashDevice.BaseUrl
                : runningApiUrl;
            string cashComPortEnv = Environment.GetEnvironmentVariable("SELFCHECKOUTKIOSK_ITL_COM_PORT") ?? testConfig?.CashDevice?.ComPort ?? "AUTO";
            string cashUsername = Environment.GetEnvironmentVariable("SELFCHECKOUTKIOSK_ITL_USERNAME") ?? testConfig?.CashDevice?.Username ?? "admin";
            string cashPassword = Environment.GetEnvironmentVariable("SELFCHECKOUTKIOSK_ITL_PASSWORD") ?? testConfig?.CashDevice?.Password ?? "password";
            string cashCurrency = Environment.GetEnvironmentVariable("SELFCHECKOUTKIOSK_ITL_CURRENCY") ?? testConfig?.CashDevice?.Currency ?? "USD,KHR";
            int pollIntervalMs = testConfig?.CashDevice?.PollIntervalMilliseconds ?? 200;
            int requestTimeoutMs = testConfig?.CashDevice?.RequestTimeoutMilliseconds ?? 5000;
            int maxPollFailures = testConfig?.CashDevice?.MaximumPollFailures ?? 3;

            // Build the list of COM ports to scan
            string[] comPortsToScan;
            if (!string.IsNullOrWhiteSpace(cashComPortEnv) && cashComPortEnv != "AUTO")
            {
                // Explicit port specified — use only that
                comPortsToScan = new[] { cashComPortEnv };
                DiagnosticLogger.Log($"[Hardware Init] Using explicit COM port: {cashComPortEnv}");
            }
            else
            {
                // AUTO: prioritize real USB serial devices (e.g. COM5, COM6) and filter out Bluetooth serial links (COM3, COM4)
                comPortsToScan = GetPrioritizedComPorts();
                DiagnosticLogger.Log($"[Hardware Init] AUTO COM scan — candidates: {string.Join(", ", comPortsToScan)}");
            }

            // Try each COM port until one connects successfully
            string? connectedPort = null;
            VendorXCashRecycler? connectedRecycler = null;

            foreach (var candidatePort in comPortsToScan)
            {
                DiagnosticLogger.Log($"[Hardware Init] Trying COM port: {candidatePort} (Currencies: {cashCurrency})...");
                var cashOptions = new CashRecyclerXOptions
                {
                    BaseUrl = cashApiUrl,
                    Username = cashUsername,
                    Password = cashPassword,
                    ComPort = candidatePort,
                    Currency = cashCurrency,
                    SspAddress = testConfig?.CashDevice?.SspAddress ?? 0,
                    PollInterval = TimeSpan.FromMilliseconds(pollIntervalMs),
                    RequestTimeout = TimeSpan.FromMilliseconds(requestTimeoutMs),
                    MaximumConsecutivePollFailures = maxPollFailures
                };

                var cashHttpClient = new System.Net.Http.HttpClient { Timeout = cashOptions.RequestTimeout };
                var recycler = new VendorXCashRecycler(cashHttpClient, cashOptions);

                try
                {
                    await recycler.ConnectAsync();
                    await recycler.DisarmAcceptanceAsync();
                    connectedPort = candidatePort;
                    connectedRecycler = recycler;
                    DiagnosticLogger.Log($"[Hardware Init] ✅ Cash Recycler CONNECTED on {candidatePort} (Currencies: {cashCurrency})!");
                    break;
                }
                catch (Exception ex)
                {
                    DiagnosticLogger.Log($"[Hardware Init] ❌ {candidatePort} failed: {ex.Message}");
                    // Clean up and try next port
                    cashHttpClient.Dispose();
                }
            }

            // If initial connect failed (e.g. 400 Failed to open device because REST host holds a stale session from a previous run),
            // restart the Cash API process cleanly and retry once!
            if (connectedRecycler == null && comPortsToScan.Length > 0)
            {
                DiagnosticLogger.Log("[Hardware Init] Port probe failed. Restarting Cash API process to clear any stale port locks...");
                try
                {
                    cashApiUrl = await CashApiProcessManager.RestartCashApiAsync();
                    foreach (var candidatePort in comPortsToScan)
                    {
                        DiagnosticLogger.Log($"[Hardware Init] Retrying COM port: {candidatePort} after API restart...");
                        var cashOptions = new CashRecyclerXOptions
                        {
                            BaseUrl = cashApiUrl,
                            Username = cashUsername,
                            Password = cashPassword,
                            ComPort = candidatePort,
                            Currency = cashCurrency,
                            SspAddress = testConfig?.CashDevice?.SspAddress ?? 0,
                            PollInterval = TimeSpan.FromMilliseconds(pollIntervalMs),
                            RequestTimeout = TimeSpan.FromMilliseconds(requestTimeoutMs),
                            MaximumConsecutivePollFailures = maxPollFailures
                        };

                        var cashHttpClient = new System.Net.Http.HttpClient { Timeout = cashOptions.RequestTimeout };
                        var recycler = new VendorXCashRecycler(cashHttpClient, cashOptions);

                        try
                        {
                            await recycler.ConnectAsync();
                            await recycler.DisarmAcceptanceAsync();
                            connectedPort = candidatePort;
                            connectedRecycler = recycler;
                            DiagnosticLogger.Log($"[Hardware Init] ✅ Cash Recycler CONNECTED on {candidatePort} (Currencies: {cashCurrency})!");
                            break;
                        }
                        catch (Exception ex)
                        {
                            DiagnosticLogger.Log($"[Hardware Init] ❌ Retry {candidatePort} failed: {ex.Message}");
                            cashHttpClient.Dispose();
                        }
                    }
                }
                catch (Exception ex)
                {
                    DiagnosticLogger.LogError($"[Hardware Init] Cash API restart error: {ex.Message}", ex);
                }
            }

            if (connectedRecycler != null)
            {
                CashRecyclerInstance = connectedRecycler;
                HardwareStatusManager.Instance.SetCashAvailability(true, $"Physical Cash Recycler Online ({connectedPort})");
            }
            else
            {
                // No port worked — create a fallback instance on COM5 so rest of app has a valid reference
                DiagnosticLogger.Log("[Hardware Init] No COM port responded. Cash recycler offline.");
                var fallbackOptions = new CashRecyclerXOptions
                {
                    BaseUrl = cashApiUrl,
                    Username = cashUsername,
                    Password = cashPassword,
                    ComPort = "COM5",
                    Currency = cashCurrency,
                    SspAddress = 0,
                    PollInterval = TimeSpan.FromMilliseconds(pollIntervalMs),
                    RequestTimeout = TimeSpan.FromMilliseconds(requestTimeoutMs),
                    MaximumConsecutivePollFailures = maxPollFailures
                };
                var fallbackClient = new System.Net.Http.HttpClient { Timeout = fallbackOptions.RequestTimeout };
                CashRecyclerInstance = new VendorXCashRecycler(fallbackClient, fallbackOptions);
                HardwareStatusManager.Instance.SetCashAvailability(false, "Offline / No Cash Machine Connected (scanned all COM ports)");
            }

            // Start continuous background health monitor for real-time plug/unplug detection
            StartContinuousHardwareMonitor();

            // Central Server / Cloud Connectivity Check
            string serverUrl = Environment.GetEnvironmentVariable("SELFCHECKOUTKIOSK_CENTRAL_SERVER_URL") ?? "http://localhost:5000/api/health";
            bool isServerReachable = false;
            try
            {
                using var testClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
                var response = await testClient.GetAsync(serverUrl);
                isServerReachable = response.IsSuccessStatusCode;
            }
            catch
            {
                isServerReachable = false;
            }

            HardwareStatusManager.Instance.SetServerOnline(isServerReachable, isServerReachable ? "Online (Central Server)" : "Offline (Local DB Only)");
            HardwareStatusManager.Instance.SetQrAvailability(true, "Online");

            // Initialize scanner (multi-mode: auto-probe ALL COM serial ports + global USB-HID wedge fallback)
            await ProbeAndConnectBarcodeScannerAsync(connectedPort);

            // Initialize receipt printer (multi-vendor auto-discovery or configured device)
            string printerNameEnv = Environment.GetEnvironmentVariable("SELFCHECKOUTKIOSK_PRINTER_NAME")
                ?? testConfig?.PrinterDevice?.Name
                ?? "AUTO";
            string printerPortEnv = Environment.GetEnvironmentVariable("SELFCHECKOUTKIOSK_PRINTER_PORT")
                ?? testConfig?.PrinterDevice?.Port
                ?? "AUTO";

            var bestPrinter = PrinterDiscovery.FindBestReceiptPrinter(printerNameEnv, printerPortEnv);
            string targetSpecifier = bestPrinter != null ? bestPrinter.Name : printerNameEnv;

            var epsonPrinter = new EpsonReceiptPrinter(targetSpecifier);
            ReceiptPrinterInstance = epsonPrinter;
            try
            {
                await ReceiptPrinterInstance.ConnectAsync();
                string statusDesc = epsonPrinter.IsConnected
                    ? $"{epsonPrinter.PrinterName} ({epsonPrinter.PortName}) • RAW ESC/POS"
                    : (bestPrinter != null ? $"Printer '{bestPrinter.Name}' unreachable" : "No POS thermal printer detected");

                HardwareStatusManager.Instance.SetPrinterAvailability(
                    epsonPrinter.IsConnected,
                    statusDesc,
                    epsonPrinter.PrinterName);

                DiagnosticLogger.Log($"[Printer Init] Receipt Printer {(epsonPrinter.IsConnected ? "CONNECTED" : "OFFLINE")}: {statusDesc}");
            }
            catch (Exception pEx)
            {
                HardwareStatusManager.Instance.SetPrinterAvailability(false, $"Initialization error: {pEx.Message}", "Receipt Printer");
                DiagnosticLogger.LogError($"[Printer Init] Receipt printer connect exception: {pEx.Message}", pEx);
            }

            // 5. Connect Payment and Printer services to real hardware and db factory
            PaymentServiceInstance = new PaymentService(
                () => CreateDbContext(),
                CashRecyclerInstance,
                HardwareLogInstance
            );

            ReceiptPrinterServiceInstance = new ReceiptPrinterService(ReceiptPrinterInstance);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[System Init Error] {ex}");
        }
    }

    private static async Task<bool> TryAutoGenerateLicenseTokenAsync(string targetTokenPath)
    {
        try
        {
            string baseDir = AppContext.BaseDirectory;
            string currentDir = Directory.GetCurrentDirectory();

            string[] candidateGenerators =
            {
                Path.Combine(baseDir, "..", "LicenseGenerator", "GenerateLicense.exe"),
                Path.Combine(baseDir, "LicenseGenerator", "GenerateLicense.exe"),
                Path.Combine(baseDir, "..", "GenerateLicense.exe"),
                Path.Combine(baseDir, "GenerateLicense.exe"),
                Path.Combine(currentDir, "dist", "SelfCheckoutKiosk-Portable", "LicenseGenerator", "GenerateLicense.exe"),
                Path.Combine(currentDir, "LicenseGenerator", "GenerateLicense.exe"),
                Path.Combine(currentDir, "tools", "DevLicenseTokenGenerator", "bin", "Release", "net10.0", "win-x64", "GenerateLicense.exe"),
                Path.Combine(currentDir, "tools", "DevLicenseTokenGenerator", "bin", "Release", "net10.0", "GenerateLicense.exe"),
                Path.Combine(currentDir, "tools", "DevLicenseTokenGenerator", "bin", "Debug", "net10.0", "GenerateLicense.exe"),
                Path.Combine(baseDir, "..", "..", "..", "..", "tools", "DevLicenseTokenGenerator", "bin", "Release", "net10.0", "win-x64", "GenerateLicense.exe"),
                Path.Combine(baseDir, "..", "..", "..", "..", "tools", "DevLicenseTokenGenerator", "bin", "Debug", "net10.0", "GenerateLicense.exe")
            };

            string? generatorExe = candidateGenerators.FirstOrDefault(File.Exists);
            if (!string.IsNullOrEmpty(generatorExe))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = generatorExe,
                    Arguments = $"--silent --output \"{targetTokenPath}\"",
                    WorkingDirectory = Path.GetDirectoryName(generatorExe) ?? baseDir,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    await proc.WaitForExitAsync();
                    return File.Exists(targetTokenPath);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[License Auto-Gen Error] {ex.Message}");
        }

        return false;
    }

    private static string? LoadApiKey()
    {
        string? envKey = Environment.GetEnvironmentVariable("SELFCHECKOUTKIOSK_ITL_API_KEY");
        if (!string.IsNullOrWhiteSpace(envKey)) return envKey.Trim();

        string[] candidatePaths =
        {
            Path.Combine(AppContext.BaseDirectory, "api_key.secret"),
            Path.Combine(Directory.GetCurrentDirectory(), "api_key.secret"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "SelfCheckoutKiosk.App", "api_key.secret"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "SelfCheckoutKiosk.App", "api_key.secret")
        };

        foreach (var path in candidatePaths)
        {
            try
            {
                if (File.Exists(path))
                {
                    string key = File.ReadAllText(path).Trim();
                    if (!string.IsNullOrEmpty(key)) return key;
                }
            }
            catch { }
        }

        return null;
    }

    private static void StartContinuousHardwareMonitor()
    {
        Task.Run(async () =>
        {
            int consecutiveMonitorFails = 0;
            while (true)
            {
                await Task.Delay(2000);
                try
                {
                    // Do NOT attempt reconnection or restart Cash API if a customer transaction is actively in progress
                    // (e.g. paying cash, or split payment transferring to KHQR). Reconnect only after the transaction completes or resets.
                    bool isTransactionActive = (PaymentServiceInstance != null && PaymentServiceInstance.TotalDueUsd > 0 && !PaymentServiceInstance.IsFullyPaid);
                    if (isTransactionActive)
                    {
                        continue;
                    }

                    if (CashRecyclerInstance is VendorXCashRecycler recycler)
                    {
                        if (!recycler.IsConnected)
                        {
                            consecutiveMonitorFails++;
                            if (consecutiveMonitorFails >= 3)
                            {
                                DiagnosticLogger.Log("[Hardware Monitor] Repeated probe failures. Restarting Cash API process to clear stale port locks...");
                                try
                                {
                                    await CashApiProcessManager.RestartCashApiAsync();
                                }
                                catch { }
                                consecutiveMonitorFails = 0;
                            }

                            var candidatePorts = GetPrioritizedComPorts();
                            string runningApiUrl = await CashApiProcessManager.EnsureCashApiRunningAsync();
                            var currentTestConfig = Composition.KioskTestPackageConfiguration.TryLoad(AppContext.BaseDirectory);
                            string cashApiUrl = !string.IsNullOrWhiteSpace(currentTestConfig?.CashDevice?.BaseUrl)
                                ? currentTestConfig.CashDevice.BaseUrl
                                : runningApiUrl;
                            string cashUsername = Environment.GetEnvironmentVariable("SELFCHECKOUTKIOSK_ITL_USERNAME") ?? currentTestConfig?.CashDevice?.Username ?? "admin";
                            string cashPassword = Environment.GetEnvironmentVariable("SELFCHECKOUTKIOSK_ITL_PASSWORD") ?? currentTestConfig?.CashDevice?.Password ?? "password";
                            string cashCurrency = Environment.GetEnvironmentVariable("SELFCHECKOUTKIOSK_ITL_CURRENCY") ?? currentTestConfig?.CashDevice?.Currency ?? "USD,KHR";

                            bool reconnected = false;
                            foreach (var port in candidatePorts)
                            {
                                try
                                {
                                    var newOptions = new CashRecyclerXOptions
                                    {
                                        BaseUrl = cashApiUrl,
                                        Username = cashUsername,
                                        Password = cashPassword,
                                        ComPort = port,
                                        Currency = cashCurrency,
                                        SspAddress = currentTestConfig?.CashDevice?.SspAddress ?? 0,
                                        PollInterval = TimeSpan.FromMilliseconds(currentTestConfig?.CashDevice?.PollIntervalMilliseconds ?? 200),
                                        RequestTimeout = TimeSpan.FromMilliseconds(currentTestConfig?.CashDevice?.RequestTimeoutMilliseconds ?? 5000)
                                    };
                                    var newClient = new System.Net.Http.HttpClient { Timeout = newOptions.RequestTimeout };
                                    var newRecycler = new VendorXCashRecycler(newClient, newOptions);

                                    await newRecycler.ConnectAsync();

                                    bool onCashPage = false;
                                    MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
                                    {
                                        var content = MainWindowInstance?.MainRootFrame?.Content;
                                        onCashPage = content is IngestionProgressView;
                                    });

                                    if (onCashPage)
                                    {
                                        await newRecycler.ArmAcceptanceAsync();
                                        DiagnosticLogger.Log($"[Hardware Monitor] Reconnected on {port} and armed acceptor for active cash session.");
                                    }
                                    else
                                    {
                                        await newRecycler.DisarmAcceptanceAsync();
                                        DiagnosticLogger.Log($"[Hardware Monitor] Reconnected on {port} (disarmed).");
                                    }

                                    CashRecyclerInstance = newRecycler;
                                    PaymentServiceInstance?.AttachCashRecycler(newRecycler);
                                    HardwareStatusManager.Instance.SetCashAvailability(true, $"Physical Cash Recycler Online ({port})", port);
                                    reconnected = true;
                                    break;
                                }
                                catch (Exception connEx)
                                {
                                    DiagnosticLogger.Log($"[Hardware Monitor] Port {port} probe failed: {connEx.Message}");
                                }
                            }

                            if (!reconnected)
                            {
                                HardwareStatusManager.Instance.SetCashAvailability(false, "Offline / No Cash Machine Connected", null);
                            }
                        }
                    }

                    // Barcode Scanner Real-time Continuous COM Probe & Status Check
                    string? currentCashPort = (CashRecyclerInstance is VendorXCashRecycler cr && cr.IsConnected)
                        ? HardwareStatusManager.Instance.CashDevicePort
                        : null;

                    await ProbeAndConnectBarcodeScannerAsync(currentCashPort);

                    // Receipt Printer Real-time Health Probe
                    if (ReceiptPrinterInstance is EpsonReceiptPrinter epsonPrinter)
                    {
                        var pStatus = epsonPrinter.CheckHealth();
                        if (pStatus.IsOnline != HardwareStatusManager.Instance.IsPrinterAvailable)
                        {
                            string desc = pStatus.IsOnline
                                ? $"{epsonPrinter.PrinterName} ({epsonPrinter.PortName}) • RAW ESC/POS"
                                : pStatus.Reason;

                            HardwareStatusManager.Instance.SetPrinterAvailability(
                                pStatus.IsOnline,
                                desc,
                                epsonPrinter.PrinterName);

                            DiagnosticLogger.Log($"[Hardware Monitor] Printer state changed -> {(pStatus.IsOnline ? "ONLINE" : "OFFLINE")} ({desc})");
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (HardwareStatusManager.Instance.IsCashAvailable)
                    {
                        HardwareStatusManager.Instance.SetCashAvailability(false, "Cash Machine Offline / Disconnected", null);
                        DiagnosticLogger.LogError($"[Hardware Monitor] Cash machine connection lost: {ex.Message}", ex);
                    }
                }
            }
        });
    }

    public static async Task<bool> ProbeAndConnectBarcodeScannerAsync(string? excludePort = null)
    {
        string? scannerComPortEnv = Environment.GetEnvironmentVariable("SELFCHECKOUTKIOSK_SCANNER_COM_PORT");

        // If explicitly configured for HID wedge only
        if (string.Equals(scannerComPortEnv, "HID", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(scannerComPortEnv, "WEDGE", StringComparison.OrdinalIgnoreCase))
        {
            HardwareStatusManager.Instance.SetScannerAvailability(true, "USB Barcode Scanner Ready (HID Wedge)", "USB-HID");
            return true;
        }

        // If already connected on an open serial COM port and that port is not claimed by cash recycler
        if (BarcodeScannerInstance is DatalogicBarcodeScanner activeScanner && activeScanner.IsConnected)
        {
            if (!string.Equals(activeScanner.ComPort, excludePort, StringComparison.OrdinalIgnoreCase))
            {
                HardwareStatusManager.Instance.SetScannerAvailability(true, $"Serial Scanner Online ({activeScanner.ComPort})", activeScanner.ComPort);
                return true;
            }
            else
            {
                // Port conflict with cash recycler — disconnect scanner from this port
                try { await activeScanner.DisconnectAsync(); } catch { }
            }
        }

        var allSystemPorts = GetPrioritizedComPorts();
        var candidatePorts = !string.IsNullOrWhiteSpace(scannerComPortEnv) && scannerComPortEnv != "AUTO"
            ? new[] { scannerComPortEnv }
            : allSystemPorts.Where(p => !string.Equals(p, excludePort, StringComparison.OrdinalIgnoreCase)).ToArray();

        foreach (var sPort in candidatePorts)
        {
            try
            {
                var scanner = new DatalogicBarcodeScanner(sPort, 9600);
                await scanner.ConnectAsync();
                if (scanner.IsConnected)
                {
                    if (BarcodeScannerInstance != null && !ReferenceEquals(BarcodeScannerInstance, scanner))
                    {
                        try { await BarcodeScannerInstance.DisconnectAsync(); } catch { }
                    }

                    BarcodeScannerInstance = scanner;
                    BarcodeScannerInstance.OnBarcodeScanned += HandleBarcodeScanned;
                    HardwareStatusManager.Instance.SetScannerAvailability(true, $"Serial Scanner Online ({sPort})", sPort);
                    DiagnosticLogger.Log($"[Scanner Monitor] ✅ Serial Barcode Scanner CONNECTED on {sPort}!");
                    return true;
                }
            }
            catch
            {
                // Port in use, busy, or not a serial scanner — continue probing next port
            }
        }

        // Fallback: USB-HID keyboard wedge is always active globally via MainWindow
        HardwareStatusManager.Instance.SetScannerAvailability(true, "USB Barcode Scanner Ready (HID Wedge)", "USB-HID");
        return true;
    }

    private static string? _lastScannedBarcode;
    private static DateTimeOffset _lastScannedTime = DateTimeOffset.MinValue;
    private static readonly object _scanSyncLock = new();

    public static void DispatchWedgeBarcode(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode)) return;
        HandleBarcodeScannedInternal(barcode);
    }

    private static void HandleBarcodeScanned(object? sender, BarcodeScannedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.RawBarcode)) return;
        HandleBarcodeScannedInternal(e.RawBarcode);
    }

    private static void HandleBarcodeScannedInternal(string rawBarcode)
    {
        if (string.IsNullOrWhiteSpace(rawBarcode)) return;
        string barcode = rawBarcode.Trim();
        if (barcode.Length < 3 || barcode.Length > 64 || barcode.Any(char.IsControl))
            return;

        int questionMarks = barcode.Count(c => c == '?');
        if (questionMarks > 0 && (double)questionMarks / barcode.Length > 0.15)
            return;

        lock (_scanSyncLock)
        {
            var now = DateTimeOffset.UtcNow;
            if (string.Equals(_lastScannedBarcode, barcode, StringComparison.OrdinalIgnoreCase) &&
                (now - _lastScannedTime).TotalMilliseconds < 400)
            {
                Debug.WriteLine($"[SCANNER DEBOUNCE] Ignored rapid duplicate barcode read: {barcode}");
                return;
            }
            _lastScannedBarcode = barcode;
            _lastScannedTime = now;
        }

        MainWindowInstance?.DispatcherQueue.TryEnqueue(async () =>
        {
            var currentContent = MainWindowInstance?.MainRootFrame?.Content;

            if (currentContent is Views.Customer.CartView cartView)
            {
                await cartView.ProcessScannedBarcodeAsync(barcode);
            }
            else if (currentContent is Views.Customer.KioskBaseView)
            {
                var product = ProductServiceInstance.GetProductBySku(barcode);
                if (product != null)
                {
                    CartServiceInstance.AddItem(product.Name, product.Sku, product.Price, 1);
                    Debug.WriteLine($"[SCANNER] Added {product.Name} to cart via barcode scan from Idle.");
                    AppRouter.ToCart();
                }
                else
                {
                    Debug.WriteLine($"[SCANNER] No product found for barcode: {barcode}");
                }
            }
            else if (currentContent is Views.Admin.AdminLoginView adminLogin)
            {
                adminLogin.TryAuthenticateWithBarcode(barcode);
            }
            else
            {
                Debug.WriteLine($"[SCANNER] Scan received on view {currentContent?.GetType().Name} - ignored.");
            }
        });
    }

    private async Task InitializeLocalizationAsync()
    {
        string savedLang = "en";

        try
        {
            if (AppInstanceIsPackaged())
            {
                savedLang = Windows.Storage.ApplicationData.Current.LocalSettings.Values["AppLanguage"] as string ?? "en";
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App Settings Warning] Could not read LocalSettings: {ex.Message}");
        }

        try
        {
            await LocalizationService.Instance.SetLanguageAsync(savedLang);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Localization Failed] {ex}");
        }
    }

    private bool AppInstanceIsPackaged()
    {
        try
        {
            return Windows.ApplicationModel.Package.Current != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Enumerates system COM ports, prioritizing real USB serial devices (USBSER, FTDI, Prolific)
    /// and filtering out virtual Bluetooth serial links (BthModem).
    /// </summary>
    private static string[] GetPrioritizedComPorts()
    {
        var allPorts = new System.Collections.Generic.List<string>();
        var usbPorts = new System.Collections.Generic.List<string>();
        var bluetoothPorts = new System.Collections.Generic.List<string>();

        try
        {
            // 1. Check SerialPort.GetPortNames()
            foreach (var p in System.IO.Ports.SerialPort.GetPortNames())
            {
                if (!string.IsNullOrWhiteSpace(p) && !allPorts.Contains(p, StringComparer.OrdinalIgnoreCase))
                {
                    allPorts.Add(p);
                }
            }

            // 2. Check Registry SERIALCOMM
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
            if (key != null)
            {
                foreach (var valueName in key.GetValueNames())
                {
                    var portName = key.GetValue(valueName)?.ToString();
                    if (string.IsNullOrWhiteSpace(portName)) continue;

                    if (valueName.Contains("Bth", StringComparison.OrdinalIgnoreCase) ||
                        valueName.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase))
                    {
                        bluetoothPorts.Add(portName);
                    }
                    else
                    {
                        usbPorts.Add(portName);
                    }

                    if (!allPorts.Contains(portName, StringComparer.OrdinalIgnoreCase))
                    {
                        allPorts.Add(portName);
                    }
                }
            }
        }
        catch { }

        var prioritized = new System.Collections.Generic.List<string>();

        // Prioritize known typical cash recycler USB ports (COM5, COM6, COM7, COM8, COM3, COM4)
        string[] priorityOrder = { "COM5", "COM6", "COM7", "COM8", "COM3", "COM4", "COM9", "COM10", "COM1", "COM2" };
        foreach (var port in priorityOrder)
        {
            if (allPorts.Contains(port, StringComparer.OrdinalIgnoreCase) && !prioritized.Contains(port, StringComparer.OrdinalIgnoreCase))
            {
                prioritized.Add(port);
            }
        }

        // Add any remaining non-bluetooth ports
        foreach (var port in allPorts)
        {
            if (!bluetoothPorts.Contains(port, StringComparer.OrdinalIgnoreCase) && !prioritized.Contains(port, StringComparer.OrdinalIgnoreCase))
            {
                prioritized.Add(port);
            }
        }

        // Default fallbacks if no ports reported in registry (e.g. non-admin sandbox)
        if (prioritized.Count == 0)
        {
            prioritized.AddRange(new[] { "COM5", "COM6", "COM7", "COM8", "COM3", "COM4", "COM1", "COM2" });
        }

        return prioritized.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>
    /// Safely releases hardware adapters and returns any physical banknote in escrow on shutdown.
    /// </summary>
    private static void OnApplicationShutdown()
    {
        try
        {
            if (CashRecyclerInstance is VendorXCashRecycler recycler)
            {
                try
                {
                    recycler.RejectEscrowedNoteAsync().GetAwaiter().GetResult();
                }
                catch { }

                try
                {
                    recycler.DisarmAcceptanceAsync().GetAwaiter().GetResult();
                }
                catch { }

                try
                {
                    recycler.DisconnectAsync().GetAwaiter().GetResult();
                }
                catch { }
            }

            CashApiProcessManager.Shutdown();
        }
        catch { }
    }

    /// <summary>
    /// Displays a standardized modal alert dialog requesting staff assistance (matching design system specs).
    /// </summary>
    public static async Task<bool> ShowStaffAssistanceAlertAsync(string? title = null, string? message = null)
    {
        if (MainWindowInstance == null) return false;

        var tcs = new TaskCompletionSource<bool>();

        MainWindowInstance.DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var localizer = LocalizationService.Instance;
                var isKhmer = localizer.CurrentLanguage == "km";
                var font = isKhmer
                    ? (Microsoft.UI.Xaml.Media.FontFamily)Microsoft.UI.Xaml.Application.Current.Resources["KhmerFont"]
                    : (Microsoft.UI.Xaml.Media.FontFamily)(Microsoft.UI.Xaml.Application.Current.Resources["GlobalAppFont"] ?? new Microsoft.UI.Xaml.Media.FontFamily("Segoe UI"));

                title ??= localizer.GetString("StaffAssistanceTitle");
                message ??= localizer.GetString("StaffAssistanceMessage");

                var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
                {
                    XamlRoot = MainWindowInstance.Content.XamlRoot,
                    RequestedTheme = Microsoft.UI.Xaml.ElementTheme.Light,
                    Style = (Microsoft.UI.Xaml.Style)Microsoft.UI.Xaml.Application.Current.Resources["KioskContentDialogStyle"]
                };

                var container = new Microsoft.UI.Xaml.Controls.StackPanel
                {
                    HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center,
                    Spacing = 16,
                    Padding = new Microsoft.UI.Xaml.Thickness(16, 12, 16, 12),
                    MaxWidth = 460
                };

                var iconBadge = new Microsoft.UI.Xaml.Controls.Border
                {
                    Width = 64,
                    Height = 64,
                    CornerRadius = new Microsoft.UI.Xaml.CornerRadius(32),
                    Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 239, 246, 255)),
                    BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 219, 234, 254)),
                    BorderThickness = new Microsoft.UI.Xaml.Thickness(1.5),
                    HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center,
                    Child = new Microsoft.UI.Xaml.Controls.FontIcon
                    {
                        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"),
                        Glyph = "\uE77B",
                        FontSize = 26,
                        Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 37, 99, 235)),
                        HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center,
                        VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center
                    }
                };
                container.Children.Add(iconBadge);

                var titleBlock = new Microsoft.UI.Xaml.Controls.TextBlock
                {
                    Text = title,
                    FontSize = 20,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 15, 23, 42)),
                    HorizontalTextAlignment = Microsoft.UI.Xaml.TextAlignment.Center,
                    TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                    FontFamily = font
                };
                container.Children.Add(titleBlock);

                var msgBlock = new Microsoft.UI.Xaml.Controls.TextBlock
                {
                    Text = message,
                    FontSize = 14,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 116, 139)),
                    HorizontalTextAlignment = Microsoft.UI.Xaml.TextAlignment.Center,
                    TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                    Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 0, 12),
                    FontFamily = font
                };
                container.Children.Add(msgBlock);

                var adminBtn = new Microsoft.UI.Xaml.Controls.Button
                {
                    Content = new Microsoft.UI.Xaml.Controls.TextBlock
                    {
                        Text = localizer.GetString("StaffAssistanceAdminButton"),
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        FontSize = 14,
                        Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 15, 23, 42)),
                        FontFamily = font
                    },
                    Height = 40,
                    Padding = new Microsoft.UI.Xaml.Thickness(24, 0, 24, 0),
                    CornerRadius = new Microsoft.UI.Xaml.CornerRadius(8),
                    Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 241, 245, 249)),
                    BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                    BorderThickness = new Microsoft.UI.Xaml.Thickness(1),
                    HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Left
                };
                adminBtn.Click += (s, e) =>
                {
                    dialog.Hide();
                    tcs.TrySetResult(true);
                    AppRouter.ToAdmin();
                };
                container.Children.Add(adminBtn);

                dialog.Content = container;
                await dialog.ShowAsync();
            }
            catch
            {
                tcs.TrySetResult(false);
            }
        });

        return await tcs.Task;
    }

    /// <summary>
    /// Simple IHardwareIdProvider that returns a fixed pre-resolved hardware ID string.
    /// Used to carry the TPM-resolved (or fallback) hardware ID into the OfflineLicenseManager.
    /// </summary>
    private sealed class FixedHardwareIdProvider : IHardwareIdProvider
    {
        private readonly string _hardwareId;
        public FixedHardwareIdProvider(string hardwareId) => _hardwareId = hardwareId;
        public string GetHardwareId() => _hardwareId;
    }
}