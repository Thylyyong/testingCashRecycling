using System.Diagnostics;
using Microsoft.UI.Xaml;
using SelfCheckoutKiosk.App.Composition;
using SelfCheckoutKiosk.App.Services;
using SelfCheckoutKiosk.App.Views;

namespace SelfCheckoutKiosk.App
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
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
        }

        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            var mainWindow = new MainWindow();
            _window = mainWindow;

            // Assign the static reference BEFORE window operations or navigation occur
            MainWindowInstance = mainWindow;
            // Ensure the static reference is cleared when the window is closed so consumers
            // do not attempt to access a disposed native window (avoids COMException).
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
        }
    }
}