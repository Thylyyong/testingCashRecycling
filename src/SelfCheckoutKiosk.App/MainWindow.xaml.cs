using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using SelfCheckoutKiosk.App.Views;
using SelfCheckoutKiosk.App.Views.Admin;
using SelfCheckoutKiosk.App.Navigation;
using SelfCheckoutKiosk.App.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.System;
using Windows.UI.Core;
using WinRT.Interop;
using SelfCheckoutKiosk.App.Views.Customer;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace SelfCheckoutKiosk.App
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        // Set this to true to boot directly into full screen mode
        private readonly bool _startInFullScreen = false;

        public INavigationService NavigationService { get; }

        public Frame MainRootFrame => RootFrame;

        // Fields for USB-HID keyboard wedge barcode buffer
        private readonly System.Text.StringBuilder _wedgeBuffer = new();
        private DateTimeOffset _lastWedgeKeyTime = DateTimeOffset.MinValue;

        // Fields for vertical 9:16 aspect ratio window hooking
        private IntPtr _hwnd;
        private Win32SubClassDelegate? _wndProcDelegate;
        private IntPtr _oldWndProc;
        private AppWindow? _appWindow;
        private bool _isFullScreen;

        private delegate IntPtr Win32SubClassDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private const int GWL_WNDPROC = -4;
        private const uint WM_SIZING = 0x0214;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
            public int Width => right - left;
            public int Height => bottom - top;
        }

        public MainWindow()
        {
            InitializeComponent();

            var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Logo", "ca.ico");
            if (File.Exists(iconPath))
            {
                appWindow.SetIcon(iconPath);
            }

            try
            {
                // Setup vertical 9:16 aspect ratio window sizing hook
                _hwnd = WindowNative.GetWindowHandle(this);
                _wndProcDelegate = new Win32SubClassDelegate(CustomWndProc);
                IntPtr ptrWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
                _oldWndProc = SetWindowLongPtr(_hwnd, GWL_WNDPROC, ptrWndProc);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Window SubClass Warning] {ex.Message}");
            }

            _appWindow = appWindow;

            // Configure window mode based on option flag
            if (_startInFullScreen)
            {
                // Native WinUI 3 Fullscreen mode (overrides window frame sizing)
                appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
                _isFullScreen = true;
            }
            else
            {
                // Force initial window size to a clean 9:16 portrait layout on startup (e.g., Width: 540, Height: 960)
                EnforceInitialAspectRatio(540, 960);
                _isFullScreen = false;
            }

            // Initialize your navigation service with the root frame defined in XAML
            NavigationService = new NavigationService(RootFrame);

            // Attach global key interceptor to prevent Enter / Space from accidentally activating hovered/focused buttons in kiosk mode
            if (this.Content is UIElement rootElement)
            {
                rootElement.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(Global_PreviewKeyDown), handledEventsToo: true);
            }
        }

        private void Global_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            // 1. If user is focused on a text entry field (TextBox, PasswordBox, AutoSuggestBox), let keys pass through
            if (this.Content?.XamlRoot != null)
            {
                var focused = FocusManager.GetFocusedElement(this.Content.XamlRoot);
                if (focused is TextBox or PasswordBox or AutoSuggestBox)
                {
                    return;
                }
            }

            // 1.5. F11 Fullscreen toggle
            if (e.Key == VirtualKey.F11)
            {
                ToggleFullScreen();
                e.Handled = true;
                return;
            }

            // 2. Allow Admin keyboard shortcuts (Ctrl+Shift+A, Ctrl+Shift+Backspace)
            var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
            var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
            if (ctrl && shift && (e.Key == VirtualKey.A || e.Key == VirtualKey.Back))
            {
                _wedgeBuffer.Clear();

                if (e.Key == VirtualKey.A)
                {
                    var currentPageType = RootFrame.Content?.GetType();
                    if (currentPageType == typeof(AdminDiagnosticsView) ||
                        currentPageType == typeof(MediaBrandingView))
                    {
                        AppRouter.BackToCustomer();
                    }
                    else
                    {
                        _ = AdminLoginDialog.ShowAsync(this.Content?.XamlRoot);
                    }
                    e.Handled = true;
                    return;
                }

                return;
            }

            // 3. Wedge Barcode Accumulator (Rapid keystrokes from USB barcode scanners)
            var now = DateTimeOffset.UtcNow;
            if ((now - _lastWedgeKeyTime).TotalMilliseconds > 300)
            {
                _wedgeBuffer.Clear();
            }
            _lastWedgeKeyTime = now;

            if (e.Key == VirtualKey.Enter)
            {
                if (_wedgeBuffer.Length >= 3)
                {
                    string barcode = _wedgeBuffer.ToString().Trim();
                    _wedgeBuffer.Clear();
                    App.DispatchWedgeBarcode(barcode);
                }
                e.Handled = true;
                return;
            }
            else if (e.Key >= VirtualKey.Number0 && e.Key <= VirtualKey.Number9)
            {
                _wedgeBuffer.Append((char)('0' + (e.Key - VirtualKey.Number0)));
            }
            else if (e.Key >= VirtualKey.NumberPad0 && e.Key <= VirtualKey.NumberPad9)
            {
                _wedgeBuffer.Append((char)('0' + (e.Key - VirtualKey.NumberPad0)));
            }
            else if (e.Key >= VirtualKey.A && e.Key <= VirtualKey.Z)
            {
                _wedgeBuffer.Append((char)('A' + (e.Key - VirtualKey.A)));
            }

            // 4. For buttons, cards, or non-text elements: PREVENT Enter or Space from clicking hovered/focused buttons!
            if (e.Key is VirtualKey.Enter or VirtualKey.Space or VirtualKey.Accept or VirtualKey.Execute)
            {
                e.Handled = true;
            }
        }

        private void EnforceInitialAspectRatio(int initialWidth, int initialHeight)
        {
            // Forces the window to start immediately at a valid 9:16 resolution
            SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, initialWidth, initialHeight, SWP_NOMOVE | SWP_NOZORDER);
        }

        public void ToggleFullScreen()
        {
            if (_appWindow == null)
            {
                var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
                _appWindow = AppWindow.GetFromWindowId(windowId);
            }

            if (_appWindow != null)
            {
                if (_isFullScreen || _appWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen)
                {
                    _appWindow.SetPresenter(AppWindowPresenterKind.Default);
                    _isFullScreen = false;
                }
                else
                {
                    _appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
                    _isFullScreen = true;
                }
            }
        }

        private IntPtr CustomWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_SIZING)
            {
                RECT rc = Marshal.PtrToStructure<RECT>(lParam);
                int edge = wParam.ToInt32();

                // Enforce strict vertical 9:16 aspect ratio (Width / Height = 9 / 16)
                switch (edge)
                {
                    case 1: // WMSZ_LEFT
                    case 2: // WMSZ_RIGHT
                        rc.bottom = rc.top + (int)(rc.Width * 16.0 / 9.0);
                        break;
                    case 3: // WMSZ_TOP
                    case 4: // WMSZ_TOPLEFT
                    case 5: // WMSZ_TOPRIGHT
                        rc.right = rc.left + (int)(rc.Height * 9.0 / 16.0);
                        break;
                    case 6: // WMSZ_BOTTOM
                    case 7: // WMSZ_BOTTOMLEFT
                    case 8: // WMSZ_BOTTOMRIGHT
                    default:
                        rc.right = rc.left + (int)(rc.Height * 9.0 / 16.0);
                        break;
                }

                Marshal.StructureToPtr(rc, lParam, false);
                return (IntPtr)1;
            }

            return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
        }

        private void Window_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
                .HasFlag(CoreVirtualKeyStates.Down);

            var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
                .HasFlag(CoreVirtualKeyStates.Down);

            // Ctrl + Shift + A -> Admin Login Dialog or Toggle Back to Kiosk if already in Admin
            if (ctrl && shift && e.Key == VirtualKey.A)
            {
                var currentPageType = RootFrame.Content?.GetType();
                if (currentPageType == typeof(AdminDiagnosticsView) ||
                    currentPageType == typeof(MediaBrandingView))
                {
                    AppRouter.BackToCustomer();
                }
                else
                {
                    _ = AdminLoginDialog.ShowAsync(this.Content?.XamlRoot);
                }
                e.Handled = true;
            }

            // Ctrl + Shift + Backspace -> Welcome Page
            //if (ctrl && shift && e.Key == VirtualKey.Back)
            //{
            //    NavigationService.NavigateTo(
            //        typeof(KioskBaseView),
            //        null,
            //        new SuppressNavigationTransitionInfo()
            //    );
            //}
        }

        public void ShowLicenseLockout(string? details = null)
        {
            var queue = this.DispatcherQueue;
            if (queue != null && !queue.HasThreadAccess)
            {
                queue.TryEnqueue(() => ShowLicenseLockout(details));
                return;
            }

            if (!string.IsNullOrWhiteSpace(details))
                LicenseLockoutDetailText.Text = details;

            // Navigate the frame to a blank Page so KioskBaseView (and its MediaPlayer) is
            // properly unloaded — otherwise the video audio continues playing under the overlay.
            if (RootFrame.Content != null)
            {
                RootFrame.Navigate(typeof(Page));
                RootFrame.BackStack.Clear();
            }

            LicenseLockoutOverlay.Visibility = Visibility.Visible;
        }

        public void HideLicenseLockout()
        {
            var queue = this.DispatcherQueue;
            if (queue != null && !queue.HasThreadAccess)
            {
                queue.TryEnqueue(HideLicenseLockout);
                return;
            }

            LicenseLockoutOverlay.Visibility = Visibility.Collapsed;
        }
    }
}