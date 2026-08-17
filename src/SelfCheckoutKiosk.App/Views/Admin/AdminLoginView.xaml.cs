using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SelfCheckoutKiosk.App.ViewModels.Admin;
using SelfCheckoutKiosk.App.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SelfCheckoutKiosk.App.Views.Admin
{
    public sealed partial class AdminLoginView : Page
    {
        public AdminLoginViewModel ViewModel { get; }

        public AdminLoginView()
        {
            InitializeComponent();
            ViewModel = new AdminLoginViewModel();
        }

        // Live validation during typing (Blocks symbols/emojis instantly)
        private void UsernameTextBox_BeforeTextChanging(TextBox sender, TextBoxBeforeTextChangingEventArgs args)
        {
            if (!string.IsNullOrEmpty(args.NewText))
            {
                if (InputValidator.ContainsEmoji(args.NewText) || !System.Text.RegularExpressions.Regex.IsMatch(args.NewText, "^[a-zA-Z0-9]*$"))
                {
                    args.Cancel = true; // Reject bad input on the fly
                }
            }
        }

        private void SignInButton_Click(object sender, RoutedEventArgs e)
        {
            // Map UI control inputs directly into the Model properties inside the ViewModel
            ViewModel.CurrentAdmin.Username = UsernameTextBox.Text;
            ViewModel.CurrentAdmin.Password = PasswordBox.Password;

            // Trigger validation and login logic
            ViewModel.SignIn();
        }

        private void ReturnButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ReturnToCustomerMode();
        }
    }
}
