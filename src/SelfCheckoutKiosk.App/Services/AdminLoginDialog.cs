using System;
using System.Threading.Tasks;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SelfCheckoutKiosk.App.Services.Audio;
using SelfCheckoutKiosk.App.ViewModels.Admin;
using Windows.System;

namespace SelfCheckoutKiosk.App.Services
{
    /// <summary>
    /// Static modal dialog manager that presents a full 3x4 touch keypad for Administrator PIN authentication.
    /// Replaces full-page AdminLoginView navigation with a smooth, in-place ContentDialog.
    /// </summary>
    public static class AdminLoginDialog
    {
        private static bool _isDialogOpen = false;

        /// <summary>
        /// Displays the Administrator Login PIN pad dialog attached to the given XamlRoot.
        /// Returns true if the attendant authenticated successfully, false otherwise.
        /// </summary>
        public static async Task<bool> ShowAsync(XamlRoot? xamlRoot)
        {
            if (_isDialogOpen)
            {
                return false;
            }

            var root = xamlRoot ?? App.MainWindowInstance?.Content?.XamlRoot;
            if (root == null)
            {
                return false;
            }

            _isDialogOpen = true;

            try
            {
                var localizer = LocalizationService.Instance;
                var isKhmer = localizer.CurrentLanguage == "km";
                var font = isKhmer
                    ? (FontFamily)(Application.Current.Resources["KhmerFont"] ?? new FontFamily("Noto Sans Khmer"))
                    : (FontFamily)(Application.Current.Resources["GlobalAppFont"] ?? new FontFamily("Segoe UI"));

                var viewModel = new AdminLoginViewModel();
                ContentDialog? dialog = null;
                bool authSuccess = false;

                // --- 1. Dialog Container ---
                var container = new StackPanel
                {
                    Width = 380,
                    Padding = new Thickness(16, 6, 16, 10),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Spacing = 0
                };

                // --- 2. Header: Icon & Typography ---
                var headerStack = new StackPanel
                {
                    Spacing = 4,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 16)
                };

                var iconBadge = new Border
                {
                    Width = 48,
                    Height = 48,
                    CornerRadius = new CornerRadius(14),
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 239, 246, 255)),
                    BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 191, 219, 254)),
                    BorderThickness = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 8),
                    Child = new FontIcon
                    {
                        FontFamily = new FontFamily("Segoe Fluent Icons"),
                        Glyph = "\uE7EE", // Shield / Admin icon
                        FontSize = 24,
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 37, 99, 235)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                headerStack.Children.Add(iconBadge);

                var titleText = new TextBlock
                {
                    Text = localizer.GetString("AdminLoginTitle"),
                    FontFamily = font,
                    FontSize = isKhmer ? 18 : 22,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 15, 23, 42)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center
                };
                headerStack.Children.Add(titleText);

                var subtitleText = new TextBlock
                {
                    Text = localizer.GetString("AdminLoginSubtitle"),
                    FontFamily = font,
                    FontSize = 13,
                    FontWeight = FontWeights.Medium,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 116, 139)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center
                };
                headerStack.Children.Add(subtitleText);

                container.Children.Add(headerStack);

                // --- 3. Error Banner ---
                var errorBanner = new Border
                {
                    Background = (Brush)(Application.Current.Resources["DangerLightBrush"] ?? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 254, 226, 226))),
                    BorderBrush = (Brush)(Application.Current.Resources["DangerBorderBrush"] ?? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 254, 202, 202))),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(3, 0, 3, 10),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Visibility = Visibility.Collapsed
                };

                var errorStack = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    VerticalAlignment = VerticalAlignment.Center
                };

                errorStack.Children.Add(new FontIcon
                {
                    FontFamily = new FontFamily("Segoe Fluent Icons"),
                    Glyph = "\uE783",
                    FontSize = 14,
                    Foreground = (Brush)(Application.Current.Resources["DangerBrush"] ?? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 220, 38, 38))),
                    VerticalAlignment = VerticalAlignment.Center
                });

                var errorMessageText = new TextBlock
                {
                    Text = string.Empty,
                    Foreground = (Brush)(Application.Current.Resources["DangerBrush"] ?? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 220, 38, 38))),
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontFamily = font,
                    MaxWidth = 300
                };
                errorStack.Children.Add(errorMessageText);
                errorBanner.Child = errorStack;
                container.Children.Add(errorBanner);

                // --- 4. Masked PIN Entry Box ---
                var pinBox = new TextBox
                {
                    PlaceholderText = "••••••••",
                    FontSize = 22,
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Center,
                    Height = 50,
                    CharacterSpacing = 140,
                    IsReadOnly = true,
                    IsTabStop = true,
                    Margin = new Thickness(3, 0, 3, 10),
                    CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 250, 252)),
                    BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 203, 213, 225)),
                    BorderThickness = new Thickness(1.5),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    FontFamily = font
                };
                container.Children.Add(pinBox);

                // Helper to refresh UI when ViewModel changes
                void UpdateUI()
                {
                    pinBox.Text = viewModel.DisplayPin;
                    if (viewModel.HasError && !string.IsNullOrWhiteSpace(viewModel.ErrorMessage))
                    {
                        errorMessageText.Text = viewModel.ErrorMessage;
                        errorBanner.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        errorBanner.Visibility = Visibility.Collapsed;
                    }
                }

                void DoAuth()
                {
                    if (viewModel.TryAuthenticate())
                    {
                        authSuccess = true;
                        dialog?.Hide();
                    }
                    else
                    {
                        UpdateUI();
                    }
                }

                // --- 5. 3x4 Touch Keypad Grid ---
                var keypadGrid = new Grid
                {
                    Margin = new Thickness(0, 0, 0, 12),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                for (int i = 0; i < 3; i++) keypadGrid.ColumnDefinitions.Add(new ColumnDefinition());
                for (int i = 0; i < 4; i++) keypadGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(54) });

                Button CreateKeyButton(object content, Action onClick, bool isDanger = false)
                {
                    var style = (Style)(isDanger
                        ? Application.Current.Resources["KeypadDangerButtonStyle"]
                        : Application.Current.Resources["KeypadDigitButtonStyle"]);

                    var btn = new Button
                    {
                        Content = content,
                        Style = style,
                        FontFamily = font,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch
                    };
                    btn.Click += (s, e) =>
                    {
                        onClick();
                        pinBox.Focus(FocusState.Programmatic);
                    };
                    return btn;
                }

                // Numbers 1-9
                for (int row = 0; row < 3; row++)
                {
                    for (int col = 0; col < 3; col++)
                    {
                        char d = (char)('1' + (row * 3 + col));
                        string digitStr = d.ToString();
                        var digitBtn = CreateKeyButton(digitStr, () =>
                        {
                            AppSound.KeypadClick();
                            viewModel.AppendDigit(digitStr);
                            UpdateUI();
                        });
                        Grid.SetRow(digitBtn, row);
                        Grid.SetColumn(digitBtn, col);
                        keypadGrid.Children.Add(digitBtn);
                    }
                }

                // Row 4: Clear, 0, Delete
                var clearBtn = CreateKeyButton(localizer.GetString("Clear"), () =>
                {
                    AppSound.ButtonClick();
                    viewModel.ClearPin();
                    UpdateUI();
                }, isDanger: true);
                Grid.SetRow(clearBtn, 3);
                Grid.SetColumn(clearBtn, 0);
                keypadGrid.Children.Add(clearBtn);

                var zeroBtn = CreateKeyButton("0", () =>
                {
                    AppSound.KeypadClick();
                    viewModel.AppendDigit("0");
                    UpdateUI();
                });
                Grid.SetRow(zeroBtn, 3);
                Grid.SetColumn(zeroBtn, 1);
                keypadGrid.Children.Add(zeroBtn);

                var deleteBtn = CreateKeyButton(new FontIcon
                {
                    FontFamily = new FontFamily("Segoe Fluent Icons"),
                    Glyph = "\uE925",
                    FontSize = 20,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 15, 23, 42))
                }, () =>
                {
                    AppSound.KeypadClick();
                    viewModel.DeleteLast();
                    UpdateUI();
                });
                Grid.SetRow(deleteBtn, 3);
                Grid.SetColumn(deleteBtn, 2);
                keypadGrid.Children.Add(deleteBtn);

                container.Children.Add(keypadGrid);

                // --- 6. Action Buttons: Cancel & Sign In ---
                var actionGrid = new Grid
                {
                    ColumnSpacing = 6,
                    Margin = new Thickness(3, 0, 3, 0),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                actionGrid.ColumnDefinitions.Add(new ColumnDefinition());
                actionGrid.ColumnDefinitions.Add(new ColumnDefinition());

                var cancelBtn = new Button
                {
                    Content = localizer.GetString("Cancel"),
                    Style = (Style)Application.Current.Resources["DialogButtonStyle"],
                    Height = 48,
                    CornerRadius = new CornerRadius(8),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    FontFamily = font
                };
                cancelBtn.Click += (s, e) =>
                {
                    AppSound.ButtonClick();
                    dialog?.Hide();
                };
                Grid.SetColumn(cancelBtn, 0);
                actionGrid.Children.Add(cancelBtn);

                var signInBtn = new Button
                {
                    Content = localizer.GetString("SignIn"),
                    Style = (Style)Application.Current.Resources["AccentButtonStyle"],
                    Height = 48,
                    CornerRadius = new CornerRadius(8),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    FontFamily = font
                };
                signInBtn.Click += (s, e) =>
                {
                    AppSound.ButtonClick();
                    DoAuth();
                };
                Grid.SetColumn(signInBtn, 1);
                actionGrid.Children.Add(signInBtn);

                container.Children.Add(actionGrid);

                // --- 7. Key Event Listener ---
                void HandleKey(VirtualKey key)
                {
                    if (key >= VirtualKey.Number0 && key <= VirtualKey.Number9)
                    {
                        AppSound.KeypadClick();
                        string digit = ((int)(key - VirtualKey.Number0)).ToString();
                        viewModel.AppendDigit(digit);
                        UpdateUI();
                    }
                    else if (key >= VirtualKey.NumberPad0 && key <= VirtualKey.NumberPad9)
                    {
                        AppSound.KeypadClick();
                        string digit = ((int)(key - VirtualKey.NumberPad0)).ToString();
                        viewModel.AppendDigit(digit);
                        UpdateUI();
                    }
                    else if (key == VirtualKey.Back || key == VirtualKey.Delete)
                    {
                        AppSound.KeypadClick();
                        viewModel.DeleteLast();
                        UpdateUI();
                    }
                    else if (key == VirtualKey.Escape)
                    {
                        AppSound.ButtonClick();
                        dialog?.Hide();
                    }
                    else if (key == VirtualKey.Enter)
                    {
                        if (viewModel.PinLength == AdminLoginViewModel.RequiredPinLength)
                        {
                            AppSound.ButtonClick();
                            DoAuth();
                        }
                        else
                        {
                            AppSound.ErrorPassword();
                            viewModel.ErrorMessage = $"Please enter all {AdminLoginViewModel.RequiredPinLength} digits of your admin PIN.";
                            UpdateUI();
                        }
                    }
                }

                KeyEventHandler previewKeyHandler = (sender, e) =>
                {
                    if (e.Key is VirtualKey.Number0 or VirtualKey.Number1 or VirtualKey.Number2 or
                        VirtualKey.Number3 or VirtualKey.Number4 or VirtualKey.Number5 or
                        VirtualKey.Number6 or VirtualKey.Number7 or VirtualKey.Number8 or
                        VirtualKey.Number9 or VirtualKey.NumberPad0 or VirtualKey.NumberPad1 or
                        VirtualKey.NumberPad2 or VirtualKey.NumberPad3 or VirtualKey.NumberPad4 or
                        VirtualKey.NumberPad5 or VirtualKey.NumberPad6 or VirtualKey.NumberPad7 or
                        VirtualKey.NumberPad8 or VirtualKey.NumberPad9 or VirtualKey.Back or
                        VirtualKey.Delete or VirtualKey.Escape or VirtualKey.Enter)
                    {
                        HandleKey(e.Key);
                        e.Handled = true;
                    }
                };

                // --- 8. Create & Show ContentDialog ---
                dialog = new ContentDialog
                {
                    Content = container,
                    Style = (Style)Application.Current.Resources["KioskContentDialogStyle"],
                    XamlRoot = root,
                    RequestedTheme = ElementTheme.Light
                };

                dialog.AddHandler(UIElement.PreviewKeyDownEvent, previewKeyHandler, handledEventsToo: true);
                container.AddHandler(UIElement.PreviewKeyDownEvent, previewKeyHandler, handledEventsToo: true);

                UIElement? registeredAppRoot = null;

                dialog.Opened += (s, e) =>
                {
                    pinBox.Focus(FocusState.Programmatic);
                    if (App.MainWindowInstance?.Content is UIElement appRoot)
                    {
                        registeredAppRoot = appRoot;
                        appRoot.AddHandler(UIElement.PreviewKeyDownEvent, previewKeyHandler, handledEventsToo: true);
                    }
                };

                dialog.Closed += (s, e) =>
                {
                    if (registeredAppRoot != null)
                    {
                        registeredAppRoot.RemoveHandler(UIElement.PreviewKeyDownEvent, previewKeyHandler);
                        registeredAppRoot = null;
                    }
                    dialog?.RemoveHandler(UIElement.PreviewKeyDownEvent, previewKeyHandler);
                    container.RemoveHandler(UIElement.PreviewKeyDownEvent, previewKeyHandler);
                    viewModel.ClearPin();
                };

                await dialog.ShowAsync();
                return authSuccess;
            }
            finally
            {
                _isDialogOpen = false;
            }
        }
    }
}
