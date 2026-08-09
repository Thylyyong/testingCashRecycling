using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SelfCheckoutKiosk.App.Models;
using SelfCheckoutKiosk.App.Services;
using SelfCheckoutKiosk.App.ViewModels.Customer;
using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Windows.System;

namespace SelfCheckoutKiosk.App.Views.Customer
{
    public sealed partial class CartView : Page
    {
        private const int MaxKeyIntervalMs = 80;

        private readonly StringBuilder _barcodeBuffer = new();
        private DateTime _lastKeyTime = DateTime.MinValue;

        private bool _isEnglish = true;
        private bool _isOnline = true;

        private enum ScanBehavior { AddToCart, PriceCheck, Blocked }
        private ScanBehavior _scanBehavior = ScanBehavior.AddToCart;

        private ContentDialog? _activeDialog;
        private StackPanel? _activePriceResultPanel;

        private readonly KeyEventHandler _dialogScanKeyHandler;

        public CartViewModel ViewModel { get; }

        public CartView()
        {
            InitializeComponent();

            _dialogScanKeyHandler = new KeyEventHandler(Page_KeyDown);

            INavigationService navigationService = App.MainWindowInstance?.NavigationService
                ?? new NavigationService(Frame);

            ViewModel = new CartViewModel(
                navigationService,
                App.ProductServiceInstance,
                App.CartServiceInstance
            );

            this.IsTabStop = true;

            BackToScanButton.Click += BackToScanButton_Click;
            CheckoutButton.Click += CheckoutButton_Click;

            this.Loaded += CartView_Loaded;
            this.Unloaded += CartView_Unloaded;
        }

        private void CartView_Loaded(object sender, RoutedEventArgs e)
        {
            if (App.MainWindowInstance?.Content is FrameworkElement root)
            {
                root.KeyDown += Page_KeyDown;
            }
            else
            {
                this.KeyDown += Page_KeyDown;
            }

            RefreshNetworkStatusUI();
            UpdateCartStateUI();
        }

        private void CartView_Unloaded(object sender, RoutedEventArgs e)
        {
            if (App.MainWindowInstance?.Content is FrameworkElement root)
            {
                root.KeyDown -= Page_KeyDown;
            }
            else
            {
                this.KeyDown -= Page_KeyDown;
            }
        }

        private async void Page_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            var now = DateTime.Now;
            var elapsed = (now - _lastKeyTime).TotalMilliseconds;
            _lastKeyTime = now;

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
                    e.Handled = true;

                    switch (_scanBehavior)
                    {
                        case ScanBehavior.AddToCart:
                            await ProcessScannedBarcodeAsync(scannedSku);
                            break;

                        case ScanBehavior.PriceCheck:
                            if (_activePriceResultPanel != null)
                            {
                                RenderPriceCheckResult(_activePriceResultPanel, scannedSku);
                            }
                            break;

                        case ScanBehavior.Blocked:
                            break;
                    }
                }
            }
            else
            {
                char character = GetCharFromVirtualKey(e.Key);
                if (character != '\0')
                {
                    _barcodeBuffer.Append(character);
                    e.Handled = true;
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
            bool added = ViewModel.TryAddScannedBarcode(sku, out _);

            if (added)
            {
                UpdateCartStateUI();
            }
            else
            {
                var dialog = CreateBaseDialog("Item Not Found", $"No product found for barcode: {sku}");
                dialog.CloseButtonText = "OK";
                await ShowDialogBlockingScansAsync(dialog);
            }
        }

        private void UpdateCartStateUI()
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
        }

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

        private void CurrencySwitch_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ToggleCurrency();
        }

        private void LanguageSwitch_Click(object sender, RoutedEventArgs e)
        {
            _isEnglish = !_isEnglish;
        }

        private async void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = CreateBaseDialog("Help is on the way", new TextBlock
            {
                Text = "A staff member has been notified and will assist you shortly.",
                TextWrapping = TextWrapping.Wrap
            });

            dialog.CloseButtonText = "OK";
            await ShowDialogBlockingScansAsync(dialog);
        }

        private async void CheckPriceButton_Click(object sender, RoutedEventArgs e)
        {
            var resultPanel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 16) };
            ShowPriceCheckPlaceholder(resultPanel);

            var (keypadPanel, getEnteredCode, clearEntry, entryBox) = BuildKeypadPanel("Enter EAN-13");

            var contentPanel = new StackPanel { Spacing = 0 };
            contentPanel.Children.Add(resultPanel);
            contentPanel.Children.Add(keypadPanel);

            var dialog = CreateBaseDialog("Check Price (Scan or Enter)", contentPanel);
            dialog.PrimaryButtonText = "Check Price";
            dialog.CloseButtonText = "Close";
            dialog.PrimaryButtonStyle = (Style)Application.Current.Resources["AccentButtonStyle"];

            dialog.PrimaryButtonClick += (s, args) =>
            {
                args.Cancel = true; // Keep dialog open
                var deferral = args.GetDeferral();

                try
                {
                    string code = getEnteredCode();

                    if (string.IsNullOrWhiteSpace(code))
                    {
                        ShowInlineError(resultPanel, "Please scan or enter a barcode first.");
                        entryBox.Focus(FocusState.Programmatic);
                        return;
                    }

                    RenderPriceCheckResult(resultPanel, code);
                    clearEntry();
                    entryBox.Focus(FocusState.Programmatic);
                }
                finally
                {
                    deferral.Complete(); // Now completes safely without triggering dialog exceptions
                }
            };

            dialog.AddHandler(UIElement.KeyDownEvent, _dialogScanKeyHandler, true);
            dialog.Opened += (s, args) => entryBox.Focus(FocusState.Programmatic);

            _scanBehavior = ScanBehavior.PriceCheck;
            _activeDialog = dialog;
            _activePriceResultPanel = resultPanel;

            try
            {
                await dialog.ShowAsync();
            }
            finally
            {
                dialog.RemoveHandler(UIElement.KeyDownEvent, _dialogScanKeyHandler);
                _activeDialog = null;
                _activePriceResultPanel = null;
                _scanBehavior = ScanBehavior.AddToCart;
                ResetFocus();
            }
        }

        private async void RecallButton_Click(object sender, RoutedEventArgs e)
        {
            var lastItem = ViewModel.Items.LastOrDefault();

            if (lastItem == null)
            {
                var emptyDialog = CreateBaseDialog("Your Cart is Empty", "There are no items in the cart to remove.");
                emptyDialog.CloseButtonText = "OK";
                await ShowDialogBlockingScansAsync(emptyDialog);
                return;
            }

            ViewModel.DecrementOrRemove(lastItem);
            UpdateCartStateUI();
            ResetFocus();
        }

        private void ConfirmRemove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button innerButton)
            {
                CloseFlyoutForElement(innerButton);

                if (innerButton.DataContext is CartItem item)
                {
                    ViewModel.DecrementOrRemove(item);
                    UpdateCartStateUI();
                }
            }

            ResetFocus();
        }

        private async void AddItemManuallyLink_Click(object sender, RoutedEventArgs e)
        {
            var (dialog, getEnteredCode) = CreateNumericKeypadDialog("Enter Barcode (EAN-13)", "Enter EAN-13", "Add to Cart");

            var result = await ShowDialogBlockingScansAsync(dialog);

            if (result == ContentDialogResult.Primary)
            {
                string code = getEnteredCode();
                if (!string.IsNullOrEmpty(code))
                {
                    await ProcessScannedBarcodeAsync(code);
                }
            }
        }

        private async void BackToScanButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = CreateBaseDialog("Cancel Order?", new TextBlock
            {
                Text = "Going back will cancel your current Order process. Are you sure you want to continue?",
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 450
            });

            dialog.PrimaryButtonText = "Keep scanning";
            dialog.SecondaryButtonText = "Cancel Order";
            dialog.PrimaryButtonStyle = (Style)Application.Current.Resources["AccentButtonStyle"];

            var result = await ShowDialogBlockingScansAsync(dialog);

            if (result == ContentDialogResult.Secondary)
            {
                ViewModel.CancelOrderAndProceedHome();
            }
        }

        private void CheckoutButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ProceedToPaymentSelection();
        }

        private void ShowPriceCheckPlaceholder(StackPanel panel)
        {
            panel.Children.Clear();
            panel.Children.Add(new TextBlock
            {
                Text = "Scan an item, or type its code below.",
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 116, 139))
            });
        }

        private void ShowInlineError(StackPanel panel, string message)
        {
            panel.Children.Clear();
            panel.Children.Add(new TextBlock
            {
                Text = message,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 220, 38, 38)),
                TextWrapping = TextWrapping.Wrap
            });
        }

        private void RenderPriceCheckResult(StackPanel panel, string sku)
        {
            panel.Children.Clear();

            var product = ViewModel.FindProductBySku(sku);

            if (product != null)
            {
                decimal priceKHR = product.Price * ViewModel.ExchangeRate;
                panel.Children.Add(new TextBlock { Text = product.Name, FontSize = 20, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
                panel.Children.Add(new TextBlock { Text = $"SKU: {product.Sku}", FontSize = 14, Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 116, 139)) });
                panel.Children.Add(new TextBlock { Text = $"${product.Price:0.00} (≈ ៛{priceKHR:N0})", FontSize = 28, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 185, 129)) });
            }
            else
            {
                ShowInlineError(panel, $"No product found for barcode:\n{sku}");
            }
        }

        private async Task<ContentDialogResult> ShowDialogBlockingScansAsync(ContentDialog dialog)
        {
            _scanBehavior = ScanBehavior.Blocked;
            _activeDialog = dialog;
            try
            {
                return await dialog.ShowAsync();
            }
            finally
            {
                _activeDialog = null;
                _scanBehavior = ScanBehavior.AddToCart;
                ResetFocus();
            }
        }

        private void ResetFocus()
        {
            this.Focus(FocusState.Programmatic);
        }

        private ContentDialog CreateBaseDialog(string title, object? content)
        {
            return new ContentDialog
            {
                Title = title,
                Content = content,
                XamlRoot = this.Content?.XamlRoot ?? this.XamlRoot,
                RequestedTheme = ElementTheme.Light
            };
        }

        private (ContentDialog Dialog, Func<string> GetEnteredCode) CreateNumericKeypadDialog(string title, string placeholderText, string primaryButtonText)
        {
            var (panel, getCode, _, _) = BuildKeypadPanel(placeholderText);

            var dialog = CreateBaseDialog(title, panel);
            dialog.PrimaryButtonText = primaryButtonText;
            dialog.CloseButtonText = "Close";
            dialog.DefaultButton = ContentDialogButton.Primary;
            dialog.PrimaryButtonStyle = (Style)Application.Current.Resources["AccentButtonStyle"];

            return (dialog, getCode);
        }

        private (StackPanel Panel, Func<string> GetEnteredCode, Action ClearEntry, TextBox EntryBox) BuildKeypadPanel(string placeholderText)
        {
            string enteredCode = "";

            var entryBox = new TextBox
            {
                FontSize = 32,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                IsReadOnly = true,
                PlaceholderText = placeholderText,
                MaxLength = 13,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

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

            var keypadGrid = new Grid { Margin = new Thickness(0, 16, 0, 0) };
            for (int i = 0; i < 3; i++) keypadGrid.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < 4; i++) keypadGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });

            Button MakeKeyButton(object content, Action onClick, SolidColorBrush? bg = null, SolidColorBrush? fg = null, double? fontSize = null)
            {
                var btn = new Button
                {
                    Content = content,
                    FontSize = fontSize ?? 22,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(4),
                    CornerRadius = new CornerRadius(10),
                    Background = bg ?? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 241, 245, 249)),
                    Foreground = fg ?? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 41, 59)),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };
                btn.Click += (s, args) =>
                {
                    onClick();
                    entryBox.Focus(FocusState.Programmatic);
                };
                return btn;
            }

            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 3; col++)
                {
                    string digit = (row * 3 + col + 1).ToString();
                    var btn = MakeKeyButton(digit, () => AppendDigit(digit));
                    Grid.SetRow(btn, row);
                    Grid.SetColumn(btn, col);
                    keypadGrid.Children.Add(btn);
                }
            }

            var clearBtn = MakeKeyButton("Clear", ClearAll,
                new SolidColorBrush(Windows.UI.Color.FromArgb(255, 254, 226, 226)),
                new SolidColorBrush(Windows.UI.Color.FromArgb(255, 220, 38, 38)),
                18);
            Grid.SetRow(clearBtn, 3); Grid.SetColumn(clearBtn, 0); keypadGrid.Children.Add(clearBtn);

            var zeroBtn = MakeKeyButton("0", () => AppendDigit("0"));
            Grid.SetRow(zeroBtn, 3); Grid.SetColumn(zeroBtn, 1); keypadGrid.Children.Add(zeroBtn);

            var deleteBtn = MakeKeyButton(
                new FontIcon { Glyph = "\xE925", FontSize = 30 },
                DeleteLast,
                new SolidColorBrush(Windows.UI.Color.FromArgb(255, 241, 245, 249)),
                new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 41, 59))
            );

            Grid.SetRow(deleteBtn, 3);
            Grid.SetColumn(deleteBtn, 2);
            keypadGrid.Children.Add(deleteBtn);

            var contentPanel = new StackPanel { Spacing = 0 };
            contentPanel.Children.Add(entryBox);
            contentPanel.Children.Add(keypadGrid);

            return (contentPanel, () => enteredCode, ClearAll, entryBox);
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

        private bool IsChildOf(DependencyObject? child, DependencyObject parent)
        {
            while (child != null)
            {
                if (child == parent) return true;
                child = VisualTreeHelper.GetParent(child);
            }
            return false;
        }
    }
}