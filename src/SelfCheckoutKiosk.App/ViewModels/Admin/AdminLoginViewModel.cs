using Microsoft.UI.Xaml.Media.Animation;
using SelfCheckoutKiosk.App.Views;
using SelfCheckoutKiosk.App.Views.Customer;
using SelfCheckoutKiosk.App.Helpers;
using SelfCheckoutKiosk.App.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SelfCheckoutKiosk.App.ViewModels.Admin
{
    public class AdminLoginViewModel
    {
        // Use the Model as our data source of truth
        public AdminUser CurrentAdmin { get; set; } = new AdminUser();

        public void SignIn()
        {
            // 1. Sanitize inputs to prevent script injections
            CurrentAdmin.Username = InputValidator.SanitizeInput(CurrentAdmin.Username);
            CurrentAdmin.Password = InputValidator.SanitizeInput(CurrentAdmin.Password);

            // 2. Validate Username using strict Alphanumeric rule (rejects symbols, spaces, emojis)
            if (!InputValidator.IsAlphanumericOnly(CurrentAdmin.Username, out string userError))
            {
                Debug.WriteLine($"[Validation Error - Username]: {userError}");
                return;
            }

            // 3. Validate Password using strict Alphanumeric rule
            if (!InputValidator.IsAlphanumericOnly(CurrentAdmin.Password, out string passError))
            {
                Debug.WriteLine($"[Validation Error - Password]: {passError}");
                return;
            }

            // 4. Passed! Safe to send to local SQLite database or backend service
            Debug.WriteLine($"[Success] AdminUser validated successfully for: {CurrentAdmin.Username}");
        }

        public void ReturnToCustomerMode()
        {
            App.MainWindowInstance?.NavigationService?.NavigateTo(
                typeof(KioskBaseView2),
                null,
                new SuppressNavigationTransitionInfo()
            );
        }
    }
}
