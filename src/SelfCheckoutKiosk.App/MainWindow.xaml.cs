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
using SelfCheckoutKiosk.App.Services;
using System;
using System.Collections.Generic;
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

        // Fields for vertical 9:16 aspect ratio window hooking
        private IntPtr _hwnd;
        private Win32SubClassDelegate _wndProcDelegate;
        private IntPtr _oldWndProc;

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
            appWindow.SetIcon("Assets/Logo/ca.ico");

            // Setup vertical 9:16 aspect ratio window sizing hook
            _hwnd = WindowNative.GetWindowHandle(this);
            _wndProcDelegate = new Win32SubClassDelegate(CustomWndProc);
            IntPtr ptrWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
            _oldWndProc = SetWindowLongPtr(_hwnd, GWL_WNDPROC, ptrWndProc);

            // Configure window mode based on option flag
            if (_startInFullScreen)
            {
                // Native WinUI 3 Fullscreen mode (overrides window frame sizing)
                appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            }
            else
            {
                // Force initial window size to a clean 9:16 portrait layout on startup (e.g., Width: 540, Height: 960)
                EnforceInitialAspectRatio(540, 960);
            }

            // Initialize your navigation service with the root frame defined in XAML
            NavigationService = new NavigationService(RootFrame);

            // Navigate to the initial page using the service
            NavigationService.NavigateTo(typeof(KioskBaseView));
        }

        private void EnforceInitialAspectRatio(int initialWidth, int initialHeight)
        {
            // Forces the window to start immediately at a valid 9:16 resolution
            SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, initialWidth, initialHeight, SWP_NOMOVE | SWP_NOZORDER);
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

            // Ctrl + Shift + A -> Admin Login or Toggle Back to Kiosk if already in Admin
            if (ctrl && shift && e.Key == VirtualKey.A)
            {
                var currentPageType = RootFrame.Content?.GetType();
                if (currentPageType == typeof(AdminLoginView) ||
                    currentPageType == typeof(AdminDiagnosticsView) ||
                    currentPageType == typeof(MediaBrandingView))
                {
                    NavigationService.NavigateTo(
                        typeof(KioskBaseView),
                        null,
                        SlideNavigationTransitionEffect.FromBottom
                    );
                }
                else
                {
                    NavigationService.NavigateTo(
                        typeof(AdminLoginView),
                        null,
                        SlideNavigationTransitionEffect.FromBottom
                    );
                }
                e.Handled = true;
            }

            // Ctrl + Shift + Backspace -> Welcome Page
            if (ctrl && shift && e.Key == VirtualKey.Back)
            {
                NavigationService.NavigateTo(
                    typeof(KioskBaseView),
                    null,
                    new SuppressNavigationTransitionInfo()
                );
            }
        }
    }
}