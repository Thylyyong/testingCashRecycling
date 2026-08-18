using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SelfCheckoutKiosk.App.ViewModels.Admin;
using Windows.System;

namespace SelfCheckoutKiosk.App.Views.Admin
{
    public sealed partial class AdminLoginView : Page
    {
        public AdminLoginViewModel ViewModel { get; }

        public AdminLoginView()
        {
            InitializeComponent();
            ViewModel = new AdminLoginViewModel();

            this.IsTabStop = true;
            this.Loaded += AdminLoginView_Loaded;
            this.Unloaded += AdminLoginView_Unloaded;
        }

        private void AdminLoginView_Loaded(object sender, RoutedEventArgs e)
        {
            this.Focus(FocusState.Programmatic);

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
        }

        private void AdminLoginView_Unloaded(object sender, RoutedEventArgs e)
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

        private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key >= VirtualKey.Number0 && e.Key <= VirtualKey.Number9)
            {
                string digit = ((int)(e.Key - VirtualKey.Number0)).ToString();
                ViewModel.AppendDigit(digit);
                e.Handled = true;
            }
            else if (e.Key >= VirtualKey.NumberPad0 && e.Key <= VirtualKey.NumberPad9)
            {
                string digit = ((int)(e.Key - VirtualKey.NumberPad0)).ToString();
                ViewModel.AppendDigit(digit);
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Back)
            {
                ViewModel.DeleteLast();
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Escape)
            {
                ViewModel.ClearPin();
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Enter)
            {
                if (ViewModel.CanSubmit)
                {
                    ViewModel.TryAuthenticate();
                }
                e.Handled = true;
            }
        }

        private void KeypadDigit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string digit)
            {
                ViewModel.AppendDigit(digit);
            }
        }

        private void KeypadClear_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ClearPin();
        }

        private void KeypadDelete_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.DeleteLast();
        }

        private void UnlockButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.TryAuthenticate();
        }

        private void ReturnButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ReturnToCustomerMode();
        }
    }
}
