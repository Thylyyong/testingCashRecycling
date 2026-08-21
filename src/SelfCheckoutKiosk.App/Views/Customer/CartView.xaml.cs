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

            _dialogScanKeyHandler = new KeyEventHandler(Page_PreviewKeyDown);

            INavigationService navigationService = App.MainWindowInstance?.NavigationService
                ?? new NavigationService(Frame);

            ViewModel = new CartViewModel(
                navigationService,
                App.ProductServiceInstance,
                App.CartServiceInstance
            );

            this.IsTabStop = false;

            BackButton.Click += BackButton_Click;
            CheckoutButton.Click += CheckoutButton_Click;

            this.Loaded += CartView_Loaded;
            this.Unloaded += CartView_Unloaded;

            HardwareStatusManager.Instance.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(HardwareStatusManager.IsServerOnline))
                {
                    DispatcherQueue?.TryEnqueue(RefreshNetworkStatusUI);
                }
            };
        }

        private void CartView_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshNetworkStatusUI();
            RefreshCurrencyLabel();
            UpdateCartStateUI();
        }

        private void CartView_Unloaded(object sender, RoutedEventArgs e)
        {
        }

        private void Scanner_OnBarcodeScanned(object? sender, SelfCheckoutKiosk.Core.Abstractions.BarcodeScannedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.RawBarcode)) return;

            DispatcherQueue?.TryEnqueue(async () =>
            {
                if (_scanBehavior == ScanBehavior.AddToCart)
                {
                    await ProcessScannedBarcodeAsync(e.RawBarcode);
                }
                else if (_scanBehavior == ScanBehavior.PriceCheck && _activePriceResultPanel != null)
                {
                    RenderPriceCheckResult(_activePriceResultPanel, e.RawBarcode);
                }
            });
        }

        private async void Page_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
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
                else
                {
                    if (_activeDialog == null)
                    {
                        e.Handled = true;
                    }
                }
            }
            else
            {
                char character = GetCharFromVirtualKey(e.Key);
                if (character != '\0')
                {
                    _barcodeBuffer.Append(character);
                    if (_activeDialog == null)
                    {
                        e.Handled = true;
                    }
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

        public async Task ProcessScannedBarcodeAsync(string sku)
        {
            if (string.IsNullOrWhiteSpace(sku)) return;

            string cleanedSku = sku.Trim();

            // Ignore any corrupted binary serial traffic, control characters, or non-barcode noise
            if (cleanedSku.Length < 3 || cleanedSku.Length > 64 || cleanedSku.Any(char.IsControl))
            {
                Debug.WriteLine($"[CartView] Discarded invalid / non-barcode scan: '{cleanedSku}'");
                return;
            }

            int questionMarks = cleanedSku.Count(c => c == '?');
            if (questionMarks > 0 && (double)questionMarks / cleanedSku.Length > 0.15)
            {
                Debug.WriteLine($"[CartView] Discarded corrupted binary scan data: '{cleanedSku}'");
                return;
            }

            if (_scanBehavior == ScanBehavior.PriceCheck && _activePriceResultPanel != null)
            {
                RenderPriceCheckResult(_activePriceResultPanel, cleanedSku);
                return;
            }

            if (_scanBehavior == ScanBehavior.Blocked)
            {
                Debug.WriteLine("[CartView] Scan ignored: current dialog blocks scanning.");
                return;
            }

            var product = ViewModel.FindProductBySku(cleanedSku);
            if (product == null)
            {
                var notFoundDialog = CreateBaseDialog("Item Not Found", $"No product found for barcode: {cleanedSku}");
                notFoundDialog.CloseButtonText = "OK";
                await ShowDialogBlockingScansAsync(notFoundDialog);
                return;
            }

            if (product.IsAgeRestricted)
            {
                await ShowStaffAssistanceApprovalModalAsync(product);
            }
            else
            {
                ViewModel.AddItem(product.Name, product.Sku, product.Price, 1);
                UpdateCartStateUI();
            }
        }

        private async Task<bool> ShowStaffAssistanceApprovalModalAsync(Product product)
        {
            var localizer = LocalizationService.Instance;
            var isKhmer = localizer.CurrentLanguage == "km";
            var font = isKhmer
                ? (FontFamily)Application.Current.Resources["KhmerFont"]
                : (FontFamily)(Application.Current.Resources["GlobalAppFont"] ?? new FontFamily("Segoe UI"));

            var dialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot ?? App.MainWindowInstance?.Content?.XamlRoot,
                RequestedTheme = ElementTheme.Light,
                Style = (Style)Application.Current.Resources["KioskContentDialogStyle"]
            };

            var container = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 16,
                Padding = new Thickness(12, 10, 12, 10),
                MaxWidth = 460
            };

            var iconBadge = new Border
            {
                Width = 64,
                Height = 64,
                CornerRadius = new CornerRadius(32),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 239, 246, 255)),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 219, 234, 254)),
                BorderThickness = new Thickness(1.5),
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = new FontIcon
                {
                    FontFamily = new FontFamily("Segoe Fluent Icons"),
                    Glyph = "\uE77B",
                    FontSize = 26,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 37, 99, 235)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            container.Children.Add(iconBadge);

            var titleText = new TextBlock
            {
                Text = localizer.GetString("StaffAssistanceTitle"),
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 15, 23, 42)),
                HorizontalTextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = font
            };
            container.Children.Add(titleText);

            var subtitleText = new TextBlock
            {
                Text = localizer.GetString("StaffAssistanceMessage"),
                FontSize = 14,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 116, 139)),
                HorizontalTextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12),
                FontFamily = font
            };
            container.Children.Add(subtitleText);

            var adminBtn = new Button
            {
                Content = new TextBlock
                {
                    Text = localizer.GetString("StaffAssistanceAdminButton"),
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 15, 23, 42)),
                    FontFamily = font
                },
                Height = 40,
                Padding = new Thickness(24, 0, 24, 0),
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 241, 245, 249)),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240)),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            adminBtn.Click += (s, e) =>
            {
                dialog.Hide();
                App.MainWindowInstance?.NavigationService.NavigateTo(
                    typeof(Views.Admin.AdminLoginView),
                    null,
                    SlideNavigationTransitionEffect.FromBottom
                );
            };
            container.Children.Add(adminBtn);

            dialog.Content = container;

            var approvalTask = AgeRestrictedApprovalManager.Instance.RequestApprovalAsync(product);

            _ = approvalTask.ContinueWith(t =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    try { dialog.Hide(); } catch { }
                });
            });

            _activeDialog = dialog;
            _scanBehavior = ScanBehavior.Blocked;
            try
            {
                await dialog.ShowAsync();
            }
            finally
            {
                _activeDialog = null;
                _scanBehavior = ScanBehavior.AddToCart;
            }

            if (approvalTask.IsCompleted)
            {
                return await approvalTask;
            }

            return false;
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
            bool isOnline = HardwareStatusManager.Instance.IsServerOnline;
            if (isOnline)
            {
                NetworkIcon.Glyph = "\uE701";
                NetworkIcon.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 124, 252, 154));
                NetworkStatusLabel.Text = Localizer.GetString("Online");
            }
            else
            {
                NetworkIcon.Glyph = "\uEB5E";
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

        private async void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            var globalFont = (FontFamily)(Application.Current.Resources["GlobalAppFont"] ?? new FontFamily("Segoe UI"));

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
                            FontFamily = globalFont
                        },
                        new TextBlock
                        {
                            Text = Localizer.GetString("HelpMessage"),
                            FontSize = 16,
                            TextWrapping = TextWrapping.Wrap,
                            TextAlignment = TextAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            FontFamily = globalFont
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
            var font = LocalizationService.Instance.CurrentLanguage == "km"
                ? (FontFamily)Application.Current.Resources["KhmerFont"]
                : (FontFamily)(Application.Current.Resources["GlobalAppFont"] ?? new FontFamily("Segoe UI"));

            var resultPanel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 16), Width = 380, MaxWidth = 380 };
            ShowPriceCheckPlaceholder(resultPanel);

            var (keypadPanel, getEnteredCode, clearEntry, entryBox) = BuildKeypadPanel(
                Localizer.GetString("EnterEan13"),
                onClear: () => ShowPriceCheckPlaceholder(resultPanel)
            );

            var contentPanel = new StackPanel { Spacing = 0, Width = 380, MaxWidth = 380, HorizontalAlignment = HorizontalAlignment.Center };
            contentPanel.Children.Add(resultPanel);
            contentPanel.Children.Add(keypadPanel);

            var baseAccentStyle = (Style)Application.Current.Resources["AccentButtonStyle"];
            var primaryButtonStyleWithFont = new Style(typeof(Button)) { BasedOn = baseAccentStyle };
            primaryButtonStyleWithFont.Setters.Add(new Setter(Control.FontFamilyProperty, font));

            var baseDialogStyle = (Style)Application.Current.Resources["DialogButtonStyle"];
            var closeButtonStyleWithFont = new Style(typeof(Button)) { BasedOn = baseDialogStyle };
            closeButtonStyleWithFont.Setters.Add(new Setter(Control.FontFamilyProperty, font));

            var dialog = new ContentDialog
            {
                Title = new TextBlock
                {
                    Text = Localizer.GetString("CheckPriceTitle"),
                    FontFamily = font,
                    FontSize = 22,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 380
                },
                Content = contentPanel,
                Style = (Style)Application.Current.Resources["KioskContentDialogStyle"],
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

        private async void SaveCartButton_Click(object sender, RoutedEventArgs e)
        {
            var font = LocalizationService.Instance.CurrentLanguage == "km"
                ? (FontFamily)Application.Current.Resources["KhmerFont"]
                : (FontFamily)(Application.Current.Resources["GlobalAppFont"] ?? new FontFamily("Segoe UI"));

            if (ViewModel.IsEmpty)
            {
                var emptyDialog = new ContentDialog
                {
                    Content = new StackPanel
                    {
                        Spacing = 16,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Width = 380,
                        MaxWidth = 380,
                        Children =
                        {
                            new FontIcon
                            {
                                Glyph = "\uE7BF",
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
                                TextWrapping = TextWrapping.Wrap,
                                MaxWidth = 380,
                                FontFamily = font
                            },
                            new TextBlock
                            {
                                Text = Localizer.GetString("CartEmptySaveMessage"),
                                FontSize = 16,
                                TextWrapping = TextWrapping.Wrap,
                                TextAlignment = TextAlignment.Center,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                MaxWidth = 380,
                                FontFamily = font
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

            string? pin = ViewModel.SaveCartForLater();
            if (pin == null) return;

            var savedSuccessDialog = new ContentDialog
            {
                Content = new StackPanel
                {
                    Spacing = 16,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Width = 380,
                    MaxWidth = 380,
                    Children =
                    {
                        new FontIcon
                        {
                            Glyph = "\xEC61", // Checkmark icon
                            FontFamily = new FontFamily("Segoe Fluent Icons"),
                            FontSize = 44,
                            Foreground = (Brush)Application.Current.Resources["SuccessBrush"],
                            HorizontalAlignment = HorizontalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = Localizer.GetString("CartSavedTitle"),
                            FontSize = 22,
                            FontWeight = FontWeights.Bold,
                            TextAlignment = TextAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            TextWrapping = TextWrapping.Wrap,
                            MaxWidth = 380,
                            FontFamily = font
                        },
                        new TextBlock
                        {
                            Text = Localizer.GetString("RecallCodeLabel"),
                            FontSize = 14,
                            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 116, 139)),
                            TextAlignment = TextAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            TextWrapping = TextWrapping.Wrap,
                            MaxWidth = 380,
                            FontFamily = font
                        },
                        new Border
                        {
                            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 239, 246, 255)),
                            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 191, 219, 254)),
                            BorderThickness = new Thickness(2),
                            CornerRadius = new CornerRadius(12),
                            Padding = new Thickness(24, 12, 24, 12),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Child = new TextBlock
                            {
                                Text = pin,
                                FontSize = 36,
                                FontWeight = FontWeights.Black,
                                CharacterSpacing = 140,
                                Foreground = (Brush)Application.Current.Resources["PrimaryBrandBrush"],
                                HorizontalAlignment = HorizontalAlignment.Center
                            }
                        },
                        new TextBlock
                        {
                            Text = Localizer.GetString("RecallCodeNotice"),
                            FontSize = 14,
                            TextWrapping = TextWrapping.Wrap,
                            TextAlignment = TextAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            MaxWidth = 380,
                            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 71, 85, 105)),
                            FontFamily = font
                        }
                    }
                },
                Style = (Style)Application.Current.Resources["KioskContentDialogStyle"],
                PrimaryButtonText = Localizer.GetString("OK"),
                PrimaryButtonStyle = (Style)Application.Current.Resources["AccentButtonStyle"],
                XamlRoot = this.XamlRoot,
                RequestedTheme = ElementTheme.Light
            };

            await ShowDialogBlockingScansAsync(savedSuccessDialog);
            ViewModel.ProceedToHome();
        }

        private async void RecallButton_Click(object sender, RoutedEventArgs e)
        {
            var font = LocalizationService.Instance.CurrentLanguage == "km"
                ? (FontFamily)Application.Current.Resources["KhmerFont"]
                : (FontFamily)(Application.Current.Resources["GlobalAppFont"] ?? new FontFamily("Segoe UI"));

            var resultPanel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 16), Width = 380, MaxWidth = 380 };

            resultPanel.Children.Add(new TextBlock
            {
                Text = Localizer.GetString("RecallCartInstruction"),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 380,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 116, 139)),
                FontFamily = font
            });

            var (keypadPanel, getEnteredCode, clearEntry, entryBox) = BuildKeypadPanel(
                Localizer.GetString("SixDigitPin"),
                onClear: () =>
                {
                    resultPanel.Children.Clear();
                    resultPanel.Children.Add(new TextBlock
                    {
                        Text = Localizer.GetString("RecallCartInstruction"),
                        FontSize = 14,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 380,
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 116, 139)),
                        FontFamily = font
                    });
                }
            );

            var contentPanel = new StackPanel { Spacing = 0, Width = 380, MaxWidth = 380, HorizontalAlignment = HorizontalAlignment.Center };
            contentPanel.Children.Add(resultPanel);
            contentPanel.Children.Add(keypadPanel);

            var baseAccentStyle = (Style)Application.Current.Resources["AccentButtonStyle"];
            var primaryButtonStyleWithFont = new Style(typeof(Button)) { BasedOn = baseAccentStyle };
            primaryButtonStyleWithFont.Setters.Add(new Setter(Control.FontFamilyProperty, font));

            var baseDialogStyle = (Style)Application.Current.Resources["DialogButtonStyle"];
            var closeButtonStyleWithFont = new Style(typeof(Button)) { BasedOn = baseDialogStyle };
            closeButtonStyleWithFont.Setters.Add(new Setter(Control.FontFamilyProperty, font));

            var dialog = new ContentDialog
            {
                Title = new TextBlock
                {
                    Text = Localizer.GetString("RecallCartTitle"),
                    FontFamily = font,
                    FontSize = 22,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 380
                },
                Content = contentPanel,
                Style = (Style)Application.Current.Resources["KioskContentDialogStyle"],
                PrimaryButtonText = Localizer.GetString("RestoreCart"),
                CloseButtonText = Localizer.GetString("Cancel"),
                PrimaryButtonStyle = primaryButtonStyleWithFont,
                CloseButtonStyle = closeButtonStyleWithFont,
                XamlRoot = this.XamlRoot,
                RequestedTheme = ElementTheme.Light
            };

            dialog.PrimaryButtonClick += (s, args) =>
            {
                args.Cancel = true; // Keep dialog open while validating
                var deferral = args.GetDeferral();

                try
                {
                    string pin = getEnteredCode()?.Trim() ?? string.Empty;

                    if (string.IsNullOrWhiteSpace(pin) || pin.Length != 6)
                    {
                        ShowInlineError(resultPanel, Localizer.GetString("ErrorInvalidPin"));
                        entryBox.Focus(FocusState.Programmatic);
                        return;
                    }

                    if (ViewModel.TryRecallSavedCart(pin, out string errorReason))
                    {
                        UpdateCartStateUI();
                        dialog.Hide(); // Successfully restored, dismiss dialog
                    }
                    else
                    {
                        string displayError = !string.IsNullOrWhiteSpace(errorReason)
                            ? Localizer.GetString(errorReason)
                            : Localizer.GetString("ErrorCartNotFound");

                        if (string.IsNullOrWhiteSpace(displayError))
                        {
                            displayError = Localizer.GetString("ErrorCartNotFound");
                        }

                        ShowInlineError(resultPanel, displayError);
                        entryBox.Focus(FocusState.Programmatic);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Recall Exception] {ex}");
                    ShowInlineError(resultPanel, Localizer.GetString("ErrorRecallGeneric"));
                }
                finally
                {
                    deferral.Complete();
                }
            };

            dialog.Opened += (s, args) => entryBox.Focus(FocusState.Programmatic);

            await ShowDialogBlockingScansAsync(dialog);
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
            var font = LocalizationService.Instance.CurrentLanguage == "km"
                ? (FontFamily)Application.Current.Resources["KhmerFont"]
                : (FontFamily)(Application.Current.Resources["GlobalAppFont"] ?? new FontFamily("Segoe UI"));

            var resultPanel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 16), Width = 380, MaxWidth = 380 };

            TextBlock CreateInstructionText() => new TextBlock
            {
                Text = Localizer.GetString("EnterBarcodeInstruction"),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 380,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 116, 139)),
                FontFamily = font
            };

            resultPanel.Children.Add(CreateInstructionText());

            var (keypadPanel, getEnteredCode, clearEntry, entryBox) = BuildKeypadPanel(
                Localizer.GetString("EnterEan13"),
                onClear: () =>
                {
                    resultPanel.Children.Clear();
                    resultPanel.Children.Add(CreateInstructionText());
                }
            );

            var contentPanel = new StackPanel { Spacing = 0, Width = 380, MaxWidth = 380, HorizontalAlignment = HorizontalAlignment.Center };
            contentPanel.Children.Add(resultPanel);
            contentPanel.Children.Add(keypadPanel);

            var baseAccentStyle = (Style)Application.Current.Resources["AccentButtonStyle"];
            var primaryButtonStyleWithFont = new Style(typeof(Button)) { BasedOn = baseAccentStyle };
            primaryButtonStyleWithFont.Setters.Add(new Setter(Control.FontFamilyProperty, font));

            var baseDialogStyle = (Style)Application.Current.Resources["DialogButtonStyle"];
            var closeButtonStyleWithFont = new Style(typeof(Button)) { BasedOn = baseDialogStyle };
            closeButtonStyleWithFont.Setters.Add(new Setter(Control.FontFamilyProperty, font));

            var dialog = new ContentDialog
            {
                Title = new TextBlock
                {
                    Text = Localizer.GetString("AddItemManuallyTitle"),
                    FontFamily = font,
                    FontSize = 22,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 380
                },
                Content = contentPanel,
                Style = (Style)Application.Current.Resources["KioskContentDialogStyle"],
                PrimaryButtonText = Localizer.GetString("AddToCart"),
                CloseButtonText = Localizer.GetString("Close"),
                PrimaryButtonStyle = primaryButtonStyleWithFont,
                CloseButtonStyle = closeButtonStyleWithFont,
                XamlRoot = this.XamlRoot,
                RequestedTheme = ElementTheme.Light
            };

            dialog.PrimaryButtonClick += (s, args) =>
            {
                args.Cancel = true; // Keep dialog open while processing
                var deferral = args.GetDeferral();

                try
                {
                    string code = getEnteredCode()?.Trim() ?? string.Empty;

                    if (string.IsNullOrWhiteSpace(code))
                    {
                        ShowInlineError(resultPanel, Localizer.GetString("ErrorBarcodeRequired"));
                        entryBox.Focus(FocusState.Programmatic);
                        return;
                    }

                    bool added = ViewModel.TryAddScannedBarcode(code, out _);

                    if (added)
                    {
                        UpdateCartStateUI();
                        dialog.Hide(); // Close dialog on success
                    }
                    else
                    {
                        ShowInlineError(resultPanel, $"{Localizer.GetString("ErrorProductNotFoundForBarcode")}\n{code}");
                        entryBox.Focus(FocusState.Programmatic);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Add Item Exception] {ex}");
                    ShowInlineError(resultPanel, "An error occurred while adding this item.");
                }
                finally
                {
                    deferral.Complete();
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
            var globalFont = (FontFamily)(Application.Current.Resources["GlobalAppFont"] ?? new FontFamily("Segoe UI"));
            var baseAccentStyle = (Style)Application.Current.Resources["AccentButtonStyle"];
            var primaryButtonStyleWithFont = new Style(typeof(Button)) { BasedOn = baseAccentStyle };
            primaryButtonStyleWithFont.Setters.Add(new Setter(Control.FontFamilyProperty, globalFont));

            var baseDialogStyle = (Style)Application.Current.Resources["DialogButtonStyle"];
            var secondaryButtonStyleWithFont = new Style(typeof(Button)) { BasedOn = baseDialogStyle };
            secondaryButtonStyleWithFont.Setters.Add(new Setter(Control.FontFamilyProperty, globalFont));

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
                            FontFamily = globalFont
                        },
                        new TextBlock
                        {
                            Text = Localizer.GetString("CancelOrderMessage"),
                            FontSize = 16,
                            TextWrapping = TextWrapping.Wrap,
                            TextAlignment = TextAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            MaxWidth = 450,
                            FontFamily = globalFont
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
            if (ViewModel.HasItems)
            {
                ViewModel.ProceedToPaymentSelection();
            }
        }

        private void ResetFocus()
        {
            this.Focus(FocusState.Programmatic);
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

        private ContentDialog CreateBaseDialog(string title, string message)
        {
            var globalFont = (FontFamily)(Application.Current.Resources["GlobalAppFont"] ?? new FontFamily("Segoe UI"));
            var baseAccentStyle = (Style)Application.Current.Resources["AccentButtonStyle"];
            var closeButtonStyleWithFont = new Style(typeof(Button)) { BasedOn = baseAccentStyle };
            closeButtonStyleWithFont.Setters.Add(new Setter(Control.FontFamilyProperty, globalFont));

            return new ContentDialog
            {
                Title = new TextBlock
                {
                    Text = title,
                    FontFamily = globalFont,
                    FontSize = 22,
                    FontWeight = FontWeights.SemiBold
                },
                Content = new TextBlock
                {
                    Text = message,
                    FontSize = 16,
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = globalFont
                },
                Style = (Style)Application.Current.Resources["KioskContentDialogStyle"],
                CloseButtonStyle = closeButtonStyleWithFont,
                XamlRoot = this.XamlRoot,
                RequestedTheme = ElementTheme.Light
            };
        }

        private void ShowInlineError(StackPanel targetPanel, string errorMessage)
        {
            var font = LocalizationService.Instance.CurrentLanguage == "km"
                ? (FontFamily)Application.Current.Resources["KhmerFont"]
                : (FontFamily)(Application.Current.Resources["GlobalAppFont"] ?? new FontFamily("Segoe UI"));

            targetPanel.Children.Clear();

            Brush dangerLight = (Application.Current.Resources.TryGetValue("DangerLightBrush", out var bg) && bg is Brush bgb)
                ? bgb : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 254, 226, 226));

            Brush dangerBorder = (Application.Current.Resources.TryGetValue("DangerBorderBrush", out var br) && br is Brush brb)
                ? brb : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 254, 202, 202));

            Brush dangerFg = (Application.Current.Resources.TryGetValue("DangerBrush", out var fg) && fg is Brush fgb)
                ? fgb : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 220, 38, 38));

            var errorBorder = new Border
            {
                Background = dangerLight,
                BorderBrush = dangerBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 4),
                Width = 380,
                MaxWidth = 380,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var errorGrid = new Grid
            {
                ColumnSpacing = 8,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 356,
                MaxWidth = 356
            };
            errorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            errorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var icon = new FontIcon
            {
                Glyph = "\uE783", // Error / Warning Icon
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 16,
                Foreground = dangerFg,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(icon, 0);

            var errorText = new TextBlock
            {
                Text = errorMessage,
                Foreground = dangerFg,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 320,
                FontFamily = font
            };
            Grid.SetColumn(errorText, 1);

            errorGrid.Children.Add(icon);
            errorGrid.Children.Add(errorText);
            errorBorder.Child = errorGrid;
            targetPanel.Children.Add(errorBorder);
        }

        private void ShowPriceCheckPlaceholder(StackPanel targetPanel)
        {
            var font = LocalizationService.Instance.CurrentLanguage == "km"
                ? (FontFamily)Application.Current.Resources["KhmerFont"]
                : (FontFamily)(Application.Current.Resources["GlobalAppFont"] ?? new FontFamily("Segoe UI"));

            targetPanel.Children.Clear();
            targetPanel.Children.Add(new TextBlock
            {
                Text = Localizer.GetString("ScanOrTypeCode"),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 380,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 116, 139)),
                FontFamily = font
            });
        }

        private void RenderPriceCheckResult(StackPanel targetPanel, string code)
        {
            var font = LocalizationService.Instance.CurrentLanguage == "km"
                ? (FontFamily)Application.Current.Resources["KhmerFont"]
                : (FontFamily)(Application.Current.Resources["GlobalAppFont"] ?? new FontFamily("Segoe UI"));

            var product = ViewModel.FindProductBySku(code);
            targetPanel.Children.Clear();

            if (product != null)
            {
                var card = new Border
                {
                    Background = (Brush)Application.Current.Resources["SuccessSoftBrush"],
                    BorderBrush = (Brush)Application.Current.Resources["SuccessBorderBrush"],
                    BorderThickness = new Thickness(1.5),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(16, 12, 16, 12),
                    Margin = new Thickness(0, 0, 0, 4),
                    Width = 380,
                    MaxWidth = 380
                };

                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var leftStack = new StackPanel { Spacing = 2, MaxWidth = 240 };
                leftStack.Children.Add(new TextBlock
                {
                    Text = product.Name,
                    FontWeight = FontWeights.Bold,
                    FontSize = 16,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 240,
                    Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
                    FontFamily = font
                });
                leftStack.Children.Add(new TextBlock
                {
                    Text = $"SKU: {product.Sku}",
                    FontSize = 12,
                    Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
                    FontFamily = font
                });

                var rightStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                rightStack.Children.Add(new TextBlock
                {
                    Text = $"${product.Price:0.00}",
                    FontWeight = FontWeights.Bold,
                    FontSize = 22,
                    Foreground = (Brush)Application.Current.Resources["SuccessBrush"],
                    HorizontalAlignment = HorizontalAlignment.Right,
                    FontFamily = font
                });
                rightStack.Children.Add(new TextBlock
                {
                    Text = $"≈ ៛{product.Price * ViewModel.ExchangeRate:N0}",
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
                    HorizontalAlignment = HorizontalAlignment.Right,
                    FontFamily = font
                });

                Grid.SetColumn(leftStack, 0);
                Grid.SetColumn(rightStack, 1);
                row.Children.Add(leftStack);
                row.Children.Add(rightStack);
                card.Child = row;
                targetPanel.Children.Add(card);
            }
            else
            {
                ShowInlineError(targetPanel, $"{Localizer.GetString("ErrorProductNotFoundForBarcode")}\n{code}");
            }
        }

        private (FrameworkElement Panel, Func<string> GetCode, Action Clear, TextBox EntryBox) BuildKeypadPanel(
            string placeholder,
            Action? onClear = null)
        {
            var isKm = LocalizationService.Instance.CurrentLanguage == "km";
            var font = isKm
                ? (FontFamily)Application.Current.Resources["KhmerFont"]
                : (FontFamily)(Application.Current.Resources["GlobalAppFont"] ?? new FontFamily("Segoe UI"));
            string enteredCode = string.Empty;

            var entryBox = new TextBox
            {
                PlaceholderText = placeholder,
                FontSize = isKm ? 18 : 24,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                Height = 52,
                CharacterSpacing = isKm ? 0 : 100,
                IsReadOnly = true,
                Margin = new Thickness(0, 0, 0, 8),
                CornerRadius = new CornerRadius(8),
                BorderBrush = (Brush)Application.Current.Resources["CardBorderBrush"],
                BorderThickness = new Thickness(1.5),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                FontFamily = font
            };

            void AppendDigit(string digit)
            {
                if (enteredCode.Length < 16)
                {
                    enteredCode += digit;
                    entryBox.CharacterSpacing = 100;
                    entryBox.FontSize = 24;
                    entryBox.Text = enteredCode;
                }
            }

            void ClearAll()
            {
                enteredCode = string.Empty;
                entryBox.CharacterSpacing = isKm ? 0 : 100;
                entryBox.FontSize = isKm ? 18 : 24;
                entryBox.Text = string.Empty;
                onClear?.Invoke();
            }

            void DeleteLast()
            {
                if (enteredCode.Length > 0)
                {
                    enteredCode = enteredCode.Substring(0, enteredCode.Length - 1);
                    if (enteredCode.Length == 0)
                    {
                        entryBox.CharacterSpacing = isKm ? 0 : 100;
                        entryBox.FontSize = isKm ? 18 : 24;
                    }
                    entryBox.Text = enteredCode;
                }
            }

            var keypadGrid = new Grid
            {
                Margin = new Thickness(0, 8, 0, 0),
                Width = 380,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            for (int i = 0; i < 3; i++) keypadGrid.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < 4; i++) keypadGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });

            Button MakeKeyButton(object content, Action onClick, bool isDanger = false)
            {
                var style = (Style)(isDanger
                    ? Application.Current.Resources["KeypadDangerButtonStyle"]
                    : Application.Current.Resources["KeypadDigitButtonStyle"]);

                var btn = new Button
                {
                    Content = content,
                    Style = style,
                    FontFamily = font
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

            var clearBtn = MakeKeyButton(Localizer.GetString("Clear"), ClearAll, isDanger: true);
            Grid.SetRow(clearBtn, 3); Grid.SetColumn(clearBtn, 0); keypadGrid.Children.Add(clearBtn);

            var zeroBtn = MakeKeyButton("0", () => AppendDigit("0"));
            Grid.SetRow(zeroBtn, 3); Grid.SetColumn(zeroBtn, 1); keypadGrid.Children.Add(zeroBtn);

            var deleteBtn = MakeKeyButton(
                new FontIcon { Glyph = "\uE925", FontSize = 22, Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 15, 23, 42)) },
                DeleteLast);

            Grid.SetRow(deleteBtn, 3);
            Grid.SetColumn(deleteBtn, 2);
            keypadGrid.Children.Add(deleteBtn);

            var contentPanel = new StackPanel { Spacing = 0, HorizontalAlignment = HorizontalAlignment.Center, Width = 380 };
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
                item.ItemUpdated -= OnItemUpdated;
                item.ItemUpdated += OnItemUpdated;

                void OnItemUpdated(object? s, EventArgs args)
                {
                    if (border.Resources["FlashAnimation"] is Storyboard flashAnimation)
                    {
                        flashAnimation.Begin();
                    }
                }

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