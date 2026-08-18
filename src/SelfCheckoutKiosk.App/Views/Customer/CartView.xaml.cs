    using Microsoft.UI.Text;
    using Microsoft.UI.Xaml;
    using Microsoft.UI.Xaml.Controls;
    using Microsoft.UI.Xaml.Controls.Primitives;
    using Microsoft.UI.Xaml.Input;
    using Microsoft.UI.Xaml.Media;
    using Microsoft.UI.Xaml.Media.Animation;
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
            public LocalizationService Localizer => LocalizationService.Instance;
            public string RemoveItemText => Localizer.GetString("RemoveItemText");
            public string RemoveText => Localizer.GetString("RemoveText");
            private const int MaxKeyIntervalMs = 80;

            private readonly StringBuilder _barcodeBuffer = new();
            private DateTime _lastKeyTime = DateTime.MinValue;

            private bool _isEnglish = true;
            private bool _isOnline = true;
            private bool _isUsd = true;
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

                BackButton.Click += BackButton_Click;
                CheckoutButton.Click += CheckoutButton_Click;

                this.Loaded += CartView_Loaded;
                this.Unloaded += CartView_Unloaded;
            }

            private void CartView_Loaded(object sender, RoutedEventArgs e)
            {
                if (App.MainWindowInstance?.Content is FrameworkElement root)
                {
                    root.KeyDown -= Page_KeyDown;
                    root.KeyDown += Page_KeyDown;
                }
                else
                {
                    this.KeyDown -= Page_KeyDown;
                    this.KeyDown += Page_KeyDown;
                }

                RefreshNetworkStatusUI();
                RefreshCurrencyLabel();
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
                    NetworkStatusLabel.Text = Localizer.GetString("Online");
                }
                else
                {
                    NetworkIcon.Glyph = "\xEB5E";
                    NetworkIcon.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 138, 138));
                    NetworkStatusLabel.Text = Localizer.GetString("Offline");
            }
            }

            private void CurrencySwitch_Click(object sender, RoutedEventArgs e)
            {
                _isUsd = !_isUsd;

                ViewModel.ToggleCurrency();

                RefreshCurrencyLabel();
            }

            private void RefreshCurrencyLabel()
            {
                CurrencySwitchLabel.Text = Localizer.GetString(_isUsd ? "USD" : "KHR");
            }

            private void LanguageSwitch_Click(object sender, RoutedEventArgs e)
            {
                _isEnglish = !_isEnglish;
            }

            private async void HelpButton_Click(object sender, RoutedEventArgs e)
            {
                var dialog = new ContentDialog
                {
                    Content = new StackPanel
                    {
                        Spacing = 16,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Children =
                        {
                            new FontIcon
                            {
                                Glyph = "\uE946",
                                FontFamily = new FontFamily("Segoe Fluent Icons"),
                                FontSize = 42,
                                Foreground = (Brush)Application.Current.Resources["AccentBlueBrush"],
                                HorizontalAlignment = HorizontalAlignment.Center
                            },
                            new TextBlock
                            {
                                Text = Localizer.GetString("HelpIsOnTheWay"),
                                FontSize = 20,
                                FontWeight = FontWeights.SemiBold,
                                TextAlignment = TextAlignment.Center,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                FontFamily = (FontFamily)Application.Current.Resources["GlobalAppFont"]
                            },
                            new TextBlock
                            {
                                Text = Localizer.GetString("HelpMessage"),
                                FontSize = 16,
                                TextWrapping = TextWrapping.Wrap,
                                TextAlignment = TextAlignment.Center,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                FontFamily = (FontFamily)Application.Current.Resources["GlobalAppFont"]
                            }
                        }
                    },
                    Style = (Style)Application.Current.Resources["KioskContentDialogStyle"],
                    CloseButtonText = Localizer.GetString("OK"),
                    CloseButtonStyle = (Style)Application.Current.Resources["DialogButtonStyle"],
                    XamlRoot = this.XamlRoot,
                    RequestedTheme = ElementTheme.Light
                };

                await ShowDialogBlockingScansAsync(dialog);
            }

            private async void CheckPriceButton_Click(object sender, RoutedEventArgs e)
            {
                var resultPanel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 16) };
                ShowPriceCheckPlaceholder(resultPanel);

                // Pass onClear callback to reset the price display when Clear is pressed
                var (keypadPanel, getEnteredCode, clearEntry, entryBox) = BuildKeypadPanel(
                    Localizer.GetString("EnterEan13"),
                    onClear: () => ShowPriceCheckPlaceholder(resultPanel)
                );

                var contentPanel = new StackPanel { Spacing = 0 };
                contentPanel.Children.Add(resultPanel);
                contentPanel.Children.Add(keypadPanel);

                // Apply GlobalAppFont explicitly to Primary and Close buttons
                var baseAccentStyle = (Style)Application.Current.Resources["AccentButtonStyle"];
                var primaryButtonStyleWithFont = new Style(typeof(Button)) { BasedOn = baseAccentStyle };
                primaryButtonStyleWithFont.Setters.Add(new Setter(
                    Control.FontFamilyProperty,
                    (FontFamily)Application.Current.Resources["GlobalAppFont"]
                ));

                var baseDialogStyle = (Style)Application.Current.Resources["DialogButtonStyle"];
                var closeButtonStyleWithFont = new Style(typeof(Button)) { BasedOn = baseDialogStyle };
                closeButtonStyleWithFont.Setters.Add(new Setter(
                    Control.FontFamilyProperty,
                    (FontFamily)Application.Current.Resources["GlobalAppFont"]
                ));

                var dialog = new ContentDialog
                {
                    Title = new TextBlock
                    {
                        Text = Localizer.GetString("CheckPriceTitle"),
                        FontFamily = (FontFamily)Application.Current.Resources["GlobalAppFont"],
                        FontSize = 24,
                        FontWeight = FontWeights.SemiBold
                    },
                    Content = contentPanel,
                    Style = (Style)Application.Current.Resources["KioskContentDialogStyle"], // Applies CornerRadius="12"
                    PrimaryButtonText = Localizer.GetString("CheckPrice"),
                    CloseButtonText = Localizer.GetString("Close"),
                    PrimaryButtonStyle = primaryButtonStyleWithFont,
                    CloseButtonStyle = closeButtonStyleWithFont,
                    XamlRoot = this.XamlRoot,
                    RequestedTheme = ElementTheme.Light
                };

                dialog.PrimaryButtonClick += (s, args) =>
                {
                    args.Cancel = true; // Keep dialog open
                    var deferral = args.GetDeferral();

                    try
                    {
                        string code = getEnteredCode();

                        if (string.IsNullOrWhiteSpace(code))
                        {
                            ShowInlineError(resultPanel, Localizer.GetString("ErrorBarcodeRequired"));
                            entryBox.Focus(FocusState.Programmatic);
                            return;
                        }

                        RenderPriceCheckResult(resultPanel, code);
                        entryBox.Focus(FocusState.Programmatic);
                    }
                    finally
                    {
                        deferral.Complete();
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
                var latestItem = ViewModel.Items.FirstOrDefault();

                if (latestItem == null)
                {
                    var emptyDialog = new ContentDialog
                    {
                        Content = new StackPanel
                        {
                            Spacing = 16,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Children =
                    {
                        new FontIcon
                        {
                            Glyph = "\uE7BF", // Shopping cart icon
                            FontFamily = new FontFamily("Segoe Fluent Icons"),
                            FontSize = 42,
                            Foreground = (Brush)Application.Current.Resources["AccentBlueBrush"],
                            HorizontalAlignment = HorizontalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = Localizer.GetString("CartEmptyTitle"),
                            FontSize = 20,
                            FontWeight = FontWeights.SemiBold,
                            TextAlignment = TextAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            FontFamily = (FontFamily)Application.Current.Resources["GlobalAppFont"]
                        },
                        new TextBlock
                        {
                            Text = Localizer.GetString("CartEmptyMessage"),
                            FontSize = 16,
                            TextWrapping = TextWrapping.Wrap,
                            TextAlignment = TextAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            FontFamily = (FontFamily)Application.Current.Resources["GlobalAppFont"]
                        }
                    }
                        },
                        Style = (Style)Application.Current.Resources["KioskContentDialogStyle"],
                        CloseButtonText = Localizer.GetString("OK"),
                        CloseButtonStyle = (Style)Application.Current.Resources["DialogButtonStyle"],
                        XamlRoot = this.XamlRoot,
                        RequestedTheme = ElementTheme.Light
                    };

                    await ShowDialogBlockingScansAsync(emptyDialog);
                    return;
                }

                ViewModel.DecrementOrRemove(latestItem);
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
                var globalFont = (FontFamily)Application.Current.Resources["GlobalAppFont"];

                var resultPanel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 16) };

                // Helper method to create formatted instruction text
                TextBlock CreateInstructionText() => new TextBlock
                {
                    Text = Localizer.GetString("EnterBarcodeInstruction"),
                    FontSize = 14,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 116, 139)),
                    FontFamily = globalFont
                };

                // Show default instruction text in the result panel
                resultPanel.Children.Add(CreateInstructionText());

                var (keypadPanel, getEnteredCode, clearEntry, entryBox) = BuildKeypadPanel(
                    Localizer.GetString("EnterEan13"),
                    onClear: () =>
                    {
                        resultPanel.Children.Clear();
                        resultPanel.Children.Add(CreateInstructionText());
                    }
                );

                var contentPanel = new StackPanel { Spacing = 0 };
                contentPanel.Children.Add(resultPanel);
                contentPanel.Children.Add(keypadPanel);

                // Primary Button: AccentButtonStyle + GlobalAppFont
                var baseAccentStyle = (Style)Application.Current.Resources["AccentButtonStyle"];
                var primaryButtonStyleWithFont = new Style(typeof(Button)) { BasedOn = baseAccentStyle };
                primaryButtonStyleWithFont.Setters.Add(new Setter(Control.FontFamilyProperty, globalFont));

                // Close Button: DialogButtonStyle + GlobalAppFont
                var baseDialogStyle = (Style)Application.Current.Resources["DialogButtonStyle"];
                var closeButtonStyleWithFont = new Style(typeof(Button)) { BasedOn = baseDialogStyle };
                closeButtonStyleWithFont.Setters.Add(new Setter(Control.FontFamilyProperty, globalFont));

                var dialog = new ContentDialog
                {
                    Title = new TextBlock
                    {
                        Text = Localizer.GetString("AddItemManuallyTitle"),
                        FontFamily = globalFont,
                        FontSize = 22,
                        FontWeight = FontWeights.SemiBold
                    },
                    Content = contentPanel,
                    Style = (Style)Application.Current.Resources["KioskContentDialogStyle"], // Applies CornerRadius="12"
                    PrimaryButtonText = Localizer.GetString("AddToCart"),
                    CloseButtonText = Localizer.GetString("Close"),
                    PrimaryButtonStyle = primaryButtonStyleWithFont,
                    CloseButtonStyle = closeButtonStyleWithFont,
                    XamlRoot = this.XamlRoot,
                    RequestedTheme = ElementTheme.Light
                };

                dialog.PrimaryButtonClick += (s, args) =>
                {
                    string code = getEnteredCode();

                    if (string.IsNullOrWhiteSpace(code))
                    {
                        args.Cancel = true; // Keep dialog open
                        ShowInlineError(resultPanel, Localizer.GetString("ErrorBarcodeRequired"));
                        entryBox.Focus(FocusState.Programmatic);
                        return;
                    }

                    bool added = ViewModel.TryAddScannedBarcode(code, out _);

                    if (added)
                    {
                        UpdateCartStateUI();
                        // Dialog closes naturally on success
                    }
                    else
                    {
                        args.Cancel = true; // Keep dialog open on error
                        ShowInlineError(resultPanel, $"{Localizer.GetString("ErrorProductNotFoundForBarcode")}\n{code}");
                        entryBox.Focus(FocusState.Programmatic);
                    }
                };

                dialog.AddHandler(UIElement.KeyDownEvent, _dialogScanKeyHandler, true);
                dialog.Opened += (s, args) => entryBox.Focus(FocusState.Programmatic);

                _scanBehavior = ScanBehavior.AddToCart;
                _activeDialog = dialog;

                try
                {
                    await dialog.ShowAsync();
                }
                finally
                {
                    dialog.RemoveHandler(UIElement.KeyDownEvent, _dialogScanKeyHandler);
                    _activeDialog = null;
                    _scanBehavior = ScanBehavior.AddToCart;
                    ResetFocus();
                }
            }

            private async void BackButton_Click(object sender, RoutedEventArgs e)
            {
                // Create custom styles to ensure Khmer font loads properly on both buttons
                var baseAccentStyle = (Style)Application.Current.Resources["AccentButtonStyle"];
                var primaryButtonStyleWithFont = new Style(typeof(Button)) { BasedOn = baseAccentStyle };
                primaryButtonStyleWithFont.Setters.Add(new Setter(
                    Control.FontFamilyProperty,
                    (FontFamily)Application.Current.Resources["GlobalAppFont"]
                ));

                var baseDialogStyle = (Style)Application.Current.Resources["DialogButtonStyle"];
                var secondaryButtonStyleWithFont = new Style(typeof(Button)) { BasedOn = baseDialogStyle };
                secondaryButtonStyleWithFont.Setters.Add(new Setter(
                    Control.FontFamilyProperty,
                    (FontFamily)Application.Current.Resources["GlobalAppFont"]
                ));

                var dialog = new ContentDialog
                {
                    Content = new StackPanel
                    {
                        Spacing = 16,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Children =
                        {
                            new FontIcon
                            {
                                Glyph = "\uE814", // Warning / Alert Icon
                                FontFamily = new FontFamily("Segoe Fluent Icons"),
                                FontSize = 42,
                                Foreground = (Brush)Application.Current.Resources["DangerBrush"],
                                HorizontalAlignment = HorizontalAlignment.Center
                            },
                            new TextBlock
                            {
                                Text = Localizer.GetString("CancelOrderTitle"),
                                FontSize = 20,
                                FontWeight = FontWeights.SemiBold,
                                TextAlignment = TextAlignment.Center,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                FontFamily = (FontFamily)Application.Current.Resources["GlobalAppFont"]
                            },
                            new TextBlock
                            {
                                Text = Localizer.GetString("CancelOrderMessage"),
                                FontSize = 16,
                                TextWrapping = TextWrapping.Wrap,
                                TextAlignment = TextAlignment.Center,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                MaxWidth = 450,
                                FontFamily = (FontFamily)Application.Current.Resources["GlobalAppFont"]
                            }
                        }
                    },
                    Style = (Style)Application.Current.Resources["KioskContentDialogStyle"],
                    PrimaryButtonText = Localizer.GetString("KeepScanning"),
                    SecondaryButtonText = Localizer.GetString("CancelOrder"),
                    PrimaryButtonStyle = primaryButtonStyleWithFont,
                    SecondaryButtonStyle = secondaryButtonStyleWithFont,
                    XamlRoot = this.XamlRoot,
                    RequestedTheme = ElementTheme.Light
                };

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
                    Text = Localizer.GetString("ScanOrTypeCode"),
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

            private (StackPanel Panel, Func<string> GetEnteredCode, Action ClearEntry, TextBox EntryBox) BuildKeypadPanel(string placeholderText, Action? onClear = null)
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
                    onClear?.Invoke(); // Resets the price display panel
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

            private void CartItemRow_Loaded(object sender, RoutedEventArgs e)
            {
                if (sender is Border border && border.DataContext is CartItem item)
                {
                    // Unsubscribe first to avoid duplicate subscriptions if UI elements refresh
                    item.ItemUpdated -= OnItemUpdated;
                    item.ItemUpdated += OnItemUpdated;

                    void OnItemUpdated(object? s, EventArgs args)
                    {
                        // Find and play the FlashAnimation defined in this Border's Resources
                        if (border.Resources["FlashAnimation"] is Storyboard flashAnimation)
                        {
                            flashAnimation.Begin();
                        }
                    }

                    // Clean up event listener when element unloads (scrolled off-screen or removed)
                    RoutedEventHandler? unloadedHandler = null;
                    unloadedHandler = (s, ev) =>
                    {
                        border.Unloaded -= unloadedHandler;
                        item.ItemUpdated -= OnItemUpdated;
                    };
                    border.Unloaded += unloadedHandler;
                }
            }
        }
    }