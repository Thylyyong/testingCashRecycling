using Microsoft.UI.Xaml;
using SelfCheckoutKiosk.App.Services;

namespace SelfCheckoutKiosk.App
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;

        public static MainWindow? MainWindowInstance { get; private set; }

        // Application-wide singletons initialized at startup
        public static ICartService CartServiceInstance { get; } = new CartService();
        public static IProductService ProductServiceInstance { get; } = new MockProductService();
        public static IPaymentService PaymentServiceInstance { get; } = new PaymentService();
        public static IReceiptPrinterService ReceiptPrinterServiceInstance { get; } = new ReceiptPrinterService();
        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
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

            _window.Activate();
        }
    }
}