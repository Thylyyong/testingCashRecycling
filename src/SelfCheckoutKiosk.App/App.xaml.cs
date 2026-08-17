using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using SelfCheckoutKiosk.App.Composition;
using SelfCheckoutKiosk.App.Services;
using SelfCheckoutKiosk.App.Views;

namespace SelfCheckoutKiosk.App
{
    public partial class App : Application
    {
        private Window? _window;

        public static MainWindow? MainWindowInstance { get; private set; }

        /// <summary>
        /// Resolved engine/services from CompositionRoot.Build(). Populated during
        /// OnLaunched regardless of whether InitializeAsync() below succeeds —
        /// consumers must not assume the engine is actually initialized just
        /// because this is non-null.
        /// </summary>
        public static KioskServices? Services { get; private set; }

        // Application-wide singletons initialized at startup
        public static ICartService CartServiceInstance { get; } = new CartService();
        public static IProductService ProductServiceInstance { get; } = new MockProductService();
        public static IPaymentService PaymentServiceInstance { get; } = new PaymentService();
        public static IReceiptPrinterService ReceiptPrinterServiceInstance { get; } = new ReceiptPrinterService();

        public App()
        {
            InitializeComponent();

            // Catch global unhandled exceptions to prevent hard Win32 process crashes
            this.UnhandledException += App_UnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }

        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            try
            {
                var mainWindow = new MainWindow();
                _window = mainWindow;

                MainWindowInstance = mainWindow;
                mainWindow.Closed += (_, __) =>
                {
                    MainWindowInstance = null;
                    _window = null;
                };

                // Blueprint §6 step 2: license/hardware failure is a hard stop, not a
                // degraded mode. mainWindow's own constructor already navigated to the
                // normal Home flow (KioskBaseView2) — if anything in this block fails,
                // that navigation is overridden with the blocking FaultView BEFORE the
                // window is ever activated/shown, so the user never sees the normal UI.
                //
                // CompositionRoot.Build() is inside this try, not just InitializeAsync():
                // it opens a real SQLite connection while constructing the license
                // source (LicenseConfigurationSource needs a live KioskDbContext), so
                // it can throw for the same reasons InitializeAsync() can. Nothing
                // between "window constructed" and "window activated" is allowed to
                // throw unhandled past this point.
                try
                {
                    Services = CompositionRoot.Build();

                    await Services.DatabaseInitializer.InitializeAsync(
                        Services.DatabasePath,
                        Services.DatabaseEncryptionKey);

                    await Services.Engine.InitializeAsync();
                }
                catch (Exception exception)
                {
                    Debug.WriteLine($"[FATAL] Kiosk engine failed to start: {exception}");

                    mainWindow.NavigationService.NavigateTo(
                        typeof(FaultView),
                        $"{exception.GetType().Name}: {exception.Message}");
                }

                _window.Activate();

                await InitializeLocalizationAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App Startup Crash] {ex}");
            }
        }

        private async Task InitializeLocalizationAsync()
        {
            string savedLang = "en";

            try
            {
                // Access ApplicationData safely
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

        #region Global Exception Handlers (Logs instead of hard crashing)

        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            Debug.WriteLine($"[Global WinUI Exception] {e.Message}");
            e.Handled = true; // Prevents process exit
        }

        private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
        {
            Debug.WriteLine($"[Global AppDomain Exception] {e.ExceptionObject}");
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            Debug.WriteLine($"[Unobserved Task Exception] {e.Exception}");
            e.SetObserved();
        }

        #endregion
    }
}