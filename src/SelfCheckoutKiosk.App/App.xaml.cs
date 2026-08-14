using Microsoft.UI.Xaml;
using SelfCheckoutKiosk.App.Services;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace SelfCheckoutKiosk.App
{
    public partial class App : Application
    {
        private Window? _window;

        public static MainWindow? MainWindowInstance { get; private set; }

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