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

    }
}