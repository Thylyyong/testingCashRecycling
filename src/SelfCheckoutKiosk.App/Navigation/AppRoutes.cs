using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml.Media.Animation;
using SelfCheckoutKiosk.App.Views.Admin;
using SelfCheckoutKiosk.App.Views.Customer;

namespace SelfCheckoutKiosk.App.Navigation
{
    /// <summary>
    /// Central Route Table and transition configuration for the Kiosk application.
    /// Equivalent to Angular's app.routes.ts, defining view mappings and default animation behaviors.
    /// </summary>
    public static class AppRoutes
    {
        private static readonly Dictionary<KioskRoute, (Type PageType, SlideNavigationTransitionEffect DefaultTransition)> RouteTable = new()
        {
            [KioskRoute.Attract] = (typeof(KioskBaseView), SlideNavigationTransitionEffect.FromLeft),
            [KioskRoute.Home] = (typeof(HomeView), SlideNavigationTransitionEffect.FromRight),
            [KioskRoute.Cart] = (typeof(CartView), SlideNavigationTransitionEffect.FromRight),
            [KioskRoute.PaymentSelection] = (typeof(PaymentSelectionView), SlideNavigationTransitionEffect.FromRight),
            [KioskRoute.PaymentOptions] = (typeof(PaymentOptionView), SlideNavigationTransitionEffect.FromRight),
            [KioskRoute.QRPayment] = (typeof(QRPaymentView), SlideNavigationTransitionEffect.FromRight),
            [KioskRoute.CashIngestion] = (typeof(IngestionProgressView), SlideNavigationTransitionEffect.FromRight),
            [KioskRoute.Success] = (typeof(SuccessView), SlideNavigationTransitionEffect.FromRight),
            [KioskRoute.AdminLogin] = (typeof(AdminLoginView), SlideNavigationTransitionEffect.FromRight),
            [KioskRoute.AdminDiagnostics] = (typeof(AdminDiagnosticsView), SlideNavigationTransitionEffect.FromRight),
            [KioskRoute.MediaBranding] = (typeof(MediaBrandingView), SlideNavigationTransitionEffect.FromRight)
        };

        private static readonly Dictionary<Type, KioskRoute> PageTypeToRoute = new()
        {
            [typeof(KioskBaseView)] = KioskRoute.Attract,
            [typeof(HomeView)] = KioskRoute.Home,
            [typeof(CartView)] = KioskRoute.Cart,
            [typeof(PaymentSelectionView)] = KioskRoute.PaymentSelection,
            [typeof(PaymentOptionView)] = KioskRoute.PaymentOptions,
            [typeof(QRPaymentView)] = KioskRoute.QRPayment,
            [typeof(IngestionProgressView)] = KioskRoute.CashIngestion,
            [typeof(SuccessView)] = KioskRoute.Success,
            [typeof(AdminLoginView)] = KioskRoute.AdminLogin,
            [typeof(AdminDiagnosticsView)] = KioskRoute.AdminDiagnostics,
            [typeof(MediaBrandingView)] = KioskRoute.MediaBranding
        };

        /// <summary>
        /// Retrieves the WinUI 3 Page Type mapped to the specified KioskRoute.
        /// </summary>
        public static Type GetPageType(KioskRoute route)
        {
            if (RouteTable.TryGetValue(route, out var entry))
            {
                return entry.PageType;
            }

            throw new KeyNotFoundException($"Route '{route}' is not registered in AppRoutes.");
        }

        /// <summary>
        /// Retrieves the default SlideNavigationTransitionEffect configured for the specified KioskRoute.
        /// </summary>
        public static SlideNavigationTransitionEffect GetDefaultTransition(KioskRoute route)
        {
            if (RouteTable.TryGetValue(route, out var entry))
            {
                return entry.DefaultTransition;
            }

            return SlideNavigationTransitionEffect.FromRight;
        }

        /// <summary>
        /// Attempts to resolve a KioskRoute from a Page Type.
        /// </summary>
        public static KioskRoute? GetRouteFromPageType(Type? pageType)
        {
            if (pageType != null && PageTypeToRoute.TryGetValue(pageType, out var route))
            {
                return route;
            }

            return null;
        }
    }
}
