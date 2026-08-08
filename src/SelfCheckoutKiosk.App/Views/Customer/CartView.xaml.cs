using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using SelfCheckoutKiosk.App.Services;
using SelfCheckoutKiosk.App.ViewModels.Customer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.System;

namespace SelfCheckoutKiosk.App.Views.Customer
{
    public sealed partial class CartView : Page
    {
        public CartViewModel ViewModel { get; }

        private readonly IProductService _productService;

        // Barcode Scanner Keystroke Buffer State
        private readonly StringBuilder _barcodeBuffer = new();
        private DateTime _lastKeyTime = DateTime.MinValue;
        private const int MaxKeyIntervalMs = 80; // Scanners type < 50ms per key

        // Header state
        private bool _isUsd = true;
        private bool _isEnglish = true;
        private bool _isOnline = false;

        public CartView()
        {
            InitializeComponent();

            INavigationService navigationService = App.MainWindowInstance?.NavigationService
                ?? new NavigationService(Frame);

            _productService = new MockProductService();
            ViewModel = new CartViewModel(navigationService, _productService);

            //LoadMockCartData();

            BackToScanButton.Click += BackToScanButton_Click;
            CheckoutButton.Click += CheckoutButton_Click;

            this.Loaded += CartView_Loaded;
            this.Unloaded += CartView_Unloaded;
        }

        private void LoadMockCartData()
        {
            var mockProducts = _productService.GetAllProducts().Take(2).ToList();
            foreach (var product in mockProducts)
            {
                ViewModel.AddItem(product.Name, product.Sku, product.Price, 1);
            }
        }

        private void CartView_Loaded(object sender, RoutedEventArgs e)
        {
            // Subscribe to window-level key presses so scanning works anywhere on the page
            if (App.MainWindowInstance?.Content is FrameworkElement root)
            {
                root.KeyDown += Page_KeyDown;
            }
            else
            {
                this.KeyDown += Page_KeyDown;
            }

            RefreshCartUI();
            RefreshNetworkStatusUI();
        }

        private void CartView_Unloaded(object sender, RoutedEventArgs e)
        {
            // Unhook scanner event listener when leaving page
            if (App.MainWindowInstance?.Content is FrameworkElement root)
            {
                root.KeyDown -= Page_KeyDown;
            }
            else
            {
                this.KeyDown -= Page_KeyDown;
            }
        }

        // --- Barcode / QR Scanner Tracker ---

        private async void Page_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            var now = DateTime.Now;
            var elapsed = (now - _lastKeyTime).TotalMilliseconds;
            _lastKeyTime = now;

            // If keypress interval is too long, reset buffer (user is typing slowly on a keyboard)
            if (elapsed > MaxKeyIntervalMs && _barcodeBuffer.Length > 0)
            {
                _barcodeBuffer.Clear();
            }

            if (e.Key == VirtualKey.Enter)
            {
                if (_barcodeBuffer.Length > 0)
                {
                    string scannedSku = _barcodeBuffer.ToString().Trim();
                    _barcodeBuffer.Clear();

                    await ProcessScannedBarcodeAsync(scannedSku);
                    e.Handled = true;
                }
            }
            else
            {
                char character = GetCharFromVirtualKey(e.Key);
                if (character != '\0')
                {
                    _barcodeBuffer.Append(character);
                }
            }
        }

        private char GetCharFromVirtualKey(VirtualKey key)
        {
            if (key >= VirtualKey.Number0 && key <= VirtualKey.Number9)
                return (char)('0' + (key - VirtualKey.Number0));

            if (key >= VirtualKey.NumberPad0 && key <= VirtualKey.NumberPad9)
                return (char)('0' + (key - VirtualKey.NumberPad0));

            if (key >= VirtualKey.A && key <= VirtualKey.Z)
                return (char)('A' + (key - VirtualKey.A));

            return '\0';
        }

        private async Task ProcessScannedBarcodeAsync(string sku)
        {
            // 1. Look up item in catalog
            var product = _productService.GetAllProducts().FirstOrDefault(p => p.Sku.Equals(sku, StringComparison.OrdinalIgnoreCase));

            if (product != null)
            {
                // 2. Add scanned item to cart
                ViewModel.AddItem(product.Name, product.Sku, product.Price, 1);
                RefreshCartUI();
            }
            else
            {
                // 3. Unknown barcode feedback
                ContentDialog dialog = new ContentDialog
                {
                    Title = "Item Not Found",
                    Content = $"No product found for barcode: {sku}",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot,
                    RequestedTheme = ElementTheme.Light
                };

                await dialog.ShowAsync();
            }
        }

        // --- UI Updates ---

        private void RefreshCartUI()
        {
            if (ViewModel.IsEmpty)
            {
                EmptyCartState.Visibility = Visibility.Visible;
                ItemsListArea.Visibility = Visibility.Collapsed;
                ScanAnimation.Begin();
            }
            else
            {
                ScanAnimation.Stop();
                EmptyCartState.Visibility = Visibility.Collapsed;
                ItemsListArea.Visibility = Visibility.Visible;
                CartItemsControl.ItemsSource = ViewModel.Items;
            }

            ItemCountLabel.Text = $"{ViewModel.ItemCount} items";
            ItemCountSubLabel.Text = $"{ViewModel.ItemCount} items in cart";

            // Disable checkout when cart is empty
            CheckoutButton.IsEnabled = !ViewModel.IsEmpty;

            decimal totalKHR = ViewModel.Total * 4100;

            if (_isUsd)
            {
                TotalUSDLabel.Text = $"${ViewModel.Total:0.00}";
                TotalKHRLabel.Text = $"≈ ៛{totalKHR:N0}";
            }
            else
            {
                TotalUSDLabel.Text = $"៛{totalKHR:N0}";
                TotalKHRLabel.Text = $"≈ ${ViewModel.Total:0.00}";
            }
        }

        private void ConfirmRemove_Click(object sender, RoutedEventArgs e)
        {
            var innerButton = (Button)sender;

            CloseFlyoutForElement(innerButton);

            if (innerButton.DataContext is CartItem item)
            {
                ViewModel.DecrementOrRemove(item);
                RefreshCartUI();
            }
        }

        private void CloseFlyoutForElement(FrameworkElement element)
        {
            var popups = VisualTreeHelper.GetOpenPopupsForXamlRoot(element.XamlRoot);
            foreach (var popup in popups)
            {
                if (popup.Child is FlyoutPresenter presenter && IsChildOf(element, presenter))
                {
                    popup.IsOpen = false;
                    break;
                }
            }
        }

        private bool IsChildOf(DependencyObject child, DependencyObject parent)
        {
            while (child != null)
            {
                if (child == parent) return true;
                child = VisualTreeHelper.GetParent(child);
            }
            return false;
        }

        // --- Header Status ---

        private void RefreshNetworkStatusUI()
        {
            if (_isOnline)
            {
                NetworkIcon.Glyph = "\uE701";
                NetworkIcon.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 124, 252, 154));
                NetworkStatusLabel.Text = "Online";
            }
            else
            {
                NetworkIcon.Glyph = "\xEB5E";
                NetworkIcon.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 138, 138));
                NetworkStatusLabel.Text = "Offline";
            }
        }

        private async void CheckPriceButton_Click(object sender, RoutedEventArgs e)
        {
            ContentDialog dialog = new ContentDialog
            {
                Title = "Check Price",
                Content = "Scan an item to check its price.",
                CloseButtonText = "OK",
                XamlRoot = this.Content.XamlRoot,
                RequestedTheme = ElementTheme.Light
            };

            await dialog.ShowAsync();
        }

        private void RecallButton_Click(object sender, RoutedEventArgs e)
        {
            // TODO: undo latest action
        }

        private void CurrencySwitch_Click(object sender, RoutedEventArgs e)
        {
            _isUsd = !_isUsd;
            CurrencySwitchLabel.Text = _isUsd ? "USD" : "KHR";
            RefreshCartUI();
        }

        private void LanguageSwitch_Click(object sender, RoutedEventArgs e)
        {
            _isEnglish = !_isEnglish;
            //LanguageSwitchLabel.Text = _isEnglish ? "EN" : "KM";
        }

        private async void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            ContentDialog dialog = new ContentDialog
            {
                Title = "Help Is On the Way",
                Content = new TextBlock
                {
                    Text = "A staff member has been notified and will assist you shortly.",
                    TextWrapping = TextWrapping.Wrap
                },
                CloseButtonText = "OK",
                XamlRoot = this.Content.XamlRoot,
                RequestedTheme = ElementTheme.Light
            };

            dialog.CloseButtonStyle = new Style(typeof(Button))
            {
                Setters =
                {
                    new Setter(Button.HorizontalAlignmentProperty, HorizontalAlignment.Stretch),
                    new Setter(Button.HorizontalContentAlignmentProperty, HorizontalAlignment.Center)
                }
            };

            await dialog.ShowAsync();
        }

        // --- Bottom Actions ---

        private async void BackToScanButton_Click(object sender, RoutedEventArgs e)
        {
            ContentDialog dialog = new ContentDialog
            {
                Title = "Cancel Order?",
                Content = new TextBlock
                {
                    Text = "Going back will cancel your current Order process. Are you sure you want to continue?",
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 450
                },
                PrimaryButtonText = "Keep scanning",
                SecondaryButtonText = "Cancel Order",
                PrimaryButtonStyle = (Style)Application.Current.Resources["AccentButtonStyle"],
                XamlRoot = this.Content.XamlRoot,
                RequestedTheme = ElementTheme.Light
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Secondary)
            {
                ViewModel.ProceedToHome();
            }
        }

        private void CheckoutButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ProceedToPaymentSelection();
        }

        private async void AddItemManuallyLink_Click(object sender, RoutedEventArgs e)
        {
            var entryBox = new TextBox
            {
                FontSize = 32,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                IsReadOnly = true,
                PlaceholderText = "Enter EAN-13",
                MaxLength = 13,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            string enteredCode = "";

            void AppendDigit(string digit)
            {
                if (enteredCode.Length < 13)
                {
                    enteredCode += digit;
                    entryBox.Text = enteredCode;
                }
            }

            void ClearAll()
            {
                enteredCode = "";
                entryBox.Text = "";
            }

            void DeleteLast()
            {
                if (enteredCode.Length > 0)
                {
                    enteredCode = enteredCode[..^1];
                    entryBox.Text = enteredCode;
                }
            }

            // Keypad grid: 1-9, Clear, 0, Delete
            var keypadGrid = new Grid { Margin = new Thickness(0, 16, 0, 0) };
            for (int i = 0; i < 3; i++)
                keypadGrid.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < 4; i++)
                keypadGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });

            Button MakeKeyButton(string label, Action onClick, SolidColorBrush? bg = null, SolidColorBrush? fg = null)
            {
                var btn = new Button
                {
                    Content = label,
                    FontSize = 22,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(4),
                    CornerRadius = new CornerRadius(10),
                    Background = bg ?? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 241, 245, 249)),
                    Foreground = fg ?? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 41, 59)),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };
                btn.Click += (s, args) => onClick();
                return btn;
            }

            // Row 0: 1 2 3
            for (int i = 0; i < 3; i++)
            {
                string digit = (i + 1).ToString();
                var btn = MakeKeyButton(digit, () => AppendDigit(digit));
                Grid.SetRow(btn, 0);
                Grid.SetColumn(btn, i);
                keypadGrid.Children.Add(btn);
            }

            // Row 1: 4 5 6
            for (int i = 0; i < 3; i++)
            {
                string digit = (i + 4).ToString();
                var btn = MakeKeyButton(digit, () => AppendDigit(digit));
                Grid.SetRow(btn, 1);
                Grid.SetColumn(btn, i);
                keypadGrid.Children.Add(btn);
            }

            // Row 2: 7 8 9
            for (int i = 0; i < 3; i++)
            {
                string digit = (i + 7).ToString();
                var btn = MakeKeyButton(digit, () => AppendDigit(digit));
                Grid.SetRow(btn, 2);
                Grid.SetColumn(btn, i);
                keypadGrid.Children.Add(btn);
            }

            // Row 3: Clear | 0 | Delete
            var clearBtn = MakeKeyButton("Clear", ClearAll,
                new SolidColorBrush(Windows.UI.Color.FromArgb(255, 254, 226, 226)),
                new SolidColorBrush(Windows.UI.Color.FromArgb(255, 220, 38, 38)));
            Grid.SetRow(clearBtn, 3);
            Grid.SetColumn(clearBtn, 0);
            keypadGrid.Children.Add(clearBtn);

            var zeroBtn = MakeKeyButton("0", () => AppendDigit("0"));
            Grid.SetRow(zeroBtn, 3);
            Grid.SetColumn(zeroBtn, 1);
            keypadGrid.Children.Add(zeroBtn);

            var deleteBtn = MakeKeyButton("⌫", DeleteLast,
                new SolidColorBrush(Windows.UI.Color.FromArgb(255, 241, 245, 249)),
                new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 41, 59)));
            Grid.SetRow(deleteBtn, 3);
            Grid.SetColumn(deleteBtn, 2);
            keypadGrid.Children.Add(deleteBtn);

            var contentPanel = new StackPanel { Spacing = 0 };
            contentPanel.Children.Add(entryBox);
            contentPanel.Children.Add(keypadGrid);

            ContentDialog dialog = new ContentDialog
            {
                Title = "Enter Barcode (EAN-13)",
                Content = contentPanel,
                PrimaryButtonText = "Add to Cart",
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Primary,
                PrimaryButtonStyle = (Style)Application.Current.Resources["AccentButtonStyle"],
                XamlRoot = this.Content.XamlRoot,
                RequestedTheme = ElementTheme.Light
            };


            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary && enteredCode.Length > 0)
            {
                await ProcessScannedBarcodeAsync(enteredCode);
                RefreshCartUI();
            }
        }
    }
}