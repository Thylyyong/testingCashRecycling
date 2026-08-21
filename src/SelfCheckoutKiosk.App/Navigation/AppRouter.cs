using System;
using Microsoft.UI.Xaml.Media.Animation;
using SelfCheckoutKiosk.App.Services;

namespace SelfCheckoutKiosk.App.Navigation
{
    /// <summary>
    /// Global application router providing high-level, strongly-typed navigation across all Kiosk screens.
    /// Eliminates boilerplate type checks, frame manipulations, and repetitive transition imports.
    /// </summary>
    public static class AppRouter
    {
        private static INavigationService? Service => App.MainWindowInstance?.NavigationService;

        /// <summary>
        /// Gets the currently active KioskRoute, if identifiable.
        /// </summary>
        public static KioskRoute? CurrentRoute => AppRoutes.GetRouteFromPageType(Service?.CurrentPageType);

        /// <summary>
        /// Navigates to the specified KioskRoute with optional parameters and transition override.
        /// </summary>
        public static bool Navigate(
            KioskRoute route,
            object? parameter = null,
            SlideNavigationTransitionEffect? overrideEffect = null)
        {
            var navService = Service;
            if (navService == null)
            {
                return false;
            }

            var pageType = AppRoutes.GetPageType(route);
            var transition = overrideEffect ?? AppRoutes.GetDefaultTransition(route);

            return navService.NavigateTo(pageType, parameter, transition);
        }

        /// <summary>Navigates to Attract / Idle Video Loop screen (KioskBaseView)</summary>
        public static bool ToAttract(SlideNavigationTransitionEffect? effect = SlideNavigationTransitionEffect.FromLeft) =>
            Navigate(KioskRoute.Attract, null, effect);

        /// <summary>Navigates to Customer Landing screen (HomeView)</summary>
        public static bool ToHome(SlideNavigationTransitionEffect? effect = null) =>
            Navigate(KioskRoute.Home, null, effect);

        /// <summary>Navigates to Active Shopping Cart & Scanning screen (CartView)</summary>
        public static bool ToCart(SlideNavigationTransitionEffect? effect = null) =>
            Navigate(KioskRoute.Cart, null, effect);

        /// <summary>Navigates to Payment Method Selection screen (PaymentSelectionView)</summary>
        public static bool ToPaymentSelection(SlideNavigationTransitionEffect? effect = null) =>
            Navigate(KioskRoute.PaymentSelection, null, effect);

        /// <summary>Navigates to Accepted Payment Overview screen (PaymentOptionView)</summary>
        public static bool ToPaymentOptions(SlideNavigationTransitionEffect? effect = null) =>
            Navigate(KioskRoute.PaymentOptions, null, effect);

        /// <summary>Navigates to ABA / KHQR QR Payment screen (QRPaymentView)</summary>
        public static bool ToQRPayment(object? parameter = null, SlideNavigationTransitionEffect? effect = null) =>
            Navigate(KioskRoute.QRPayment, parameter, effect);

        /// <summary>Navigates to Cash Ingestion & Change Dispensing screen (IngestionProgressView)</summary>
        public static bool ToCashIngestion(object? parameter = null, SlideNavigationTransitionEffect? effect = null) =>
            Navigate(KioskRoute.CashIngestion, parameter, effect);

        /// <summary>Navigates to Transaction Complete Receipt screen (SuccessView)</summary>
        public static bool ToSuccess(object? parameter = null, SlideNavigationTransitionEffect? effect = null) =>
            Navigate(KioskRoute.Success, parameter, effect);

        /// <summary>Navigates to Staff / Attendant PIN Login screen (AdminLoginView)</summary>
        public static bool ToAdmin(SlideNavigationTransitionEffect? effect = SlideNavigationTransitionEffect.FromRight) =>
            Navigate(KioskRoute.AdminLogin, null, effect);

        /// <summary>Navigates to Staff Diagnostics & Hardware Management screen (AdminDiagnosticsView)</summary>
        public static bool ToAdminDiagnostics(SlideNavigationTransitionEffect? effect = null) =>
            Navigate(KioskRoute.AdminDiagnostics, null, effect);

        /// <summary>Navigates to Store Media & Branding Settings screen (MediaBrandingView)</summary>
        public static bool ToMediaBranding(SlideNavigationTransitionEffect? effect = null) =>
            Navigate(KioskRoute.MediaBranding, null, effect);

        /// <summary>
        /// Safely prunes all Admin pages from the backstack and returns to the calling customer screen.
        /// </summary>
        public static bool BackToCustomer(SlideNavigationTransitionEffect effect = SlideNavigationTransitionEffect.FromLeft) =>
            Service?.NavigateBackToCustomer(effect) ?? false;

        /// <summary>
        /// Performs a standard frame GoBack with the specified transition effect.
        /// </summary>
        public static bool GoBack(SlideNavigationTransitionEffect effect = SlideNavigationTransitionEffect.FromLeft) =>
            Service?.GoBack(effect) ?? false;
    }
}
