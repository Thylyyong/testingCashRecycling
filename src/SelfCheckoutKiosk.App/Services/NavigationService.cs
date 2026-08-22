using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SelfCheckoutKiosk.App.Navigation;
using SelfCheckoutKiosk.App.Services.Audio;

namespace SelfCheckoutKiosk.App.Services
{
    public interface INavigationService
    {
        bool NavigateTo(
            Type pageType,
            object? parameter = null,
            NavigationTransitionInfo? transitionInfo = null
        );

        bool NavigateTo(
            Type pageType,
            object? parameter,
            SlideNavigationTransitionEffect effect
        );

        bool NavigateTo(
            KioskRoute route,
            object? parameter = null,
            SlideNavigationTransitionEffect? effect = null
        );

        bool NavigateTo(
            KioskRoute route,
            SlideNavigationTransitionEffect effect
        );

        bool NavigateTo(
            KioskRoute route,
            object? parameter,
            NavigationTransitionInfo? transitionInfo
        );

        bool GoBack(NavigationTransitionInfo? transitionInfo = null);

        bool GoBack(SlideNavigationTransitionEffect effect);

        bool NavigateBackToCustomer(SlideNavigationTransitionEffect effect = SlideNavigationTransitionEffect.FromLeft);

        Type? CurrentPageType { get; }
    }

    public class NavigationService : INavigationService
    {
        private readonly Frame? _frame;

        // Fallback to finding the root frame via MainWindowInstance
        private Frame? TargetFrame => _frame ?? App.MainWindowInstance?.MainRootFrame;

        public NavigationService(Frame? frame = null)
        {
            _frame = frame;
        }

        public Type? CurrentPageType => TargetFrame?.CurrentSourcePageType;

        public bool NavigateTo(
            Type pageType,
            object? parameter = null,
            NavigationTransitionInfo? transitionInfo = null)
        {
            var frame = TargetFrame;

            if (frame == null)
                return false;

            if (frame.CurrentSourcePageType == pageType)
                return false;

            // Cut off any active voice prompt when transitioning between screens
            AppSound.StopVoice();

            if (transitionInfo != null)
            {
                try
                {
                    return frame.Navigate(
                        pageType,
                        parameter,
                        transitionInfo
                    );
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[NavigationService] Transition navigation fallback: {ex.Message}");
                    return frame.Navigate(pageType, parameter);
                }
            }

            return frame.Navigate(
                pageType,
                parameter
            );
        }
        public bool NavigateTo(
            Type pageType,
            object? parameter,
            SlideNavigationTransitionEffect effect)
        {
            // Note: SlideNavigationTransitionInfo in WinUI 3 has a known compositor clipping bug in Release mode
            // that causes pages to render offset/truncated. EntranceNavigationTransitionInfo provides a clean, glitch-free transition.
            return NavigateTo(
                pageType,
                parameter,
                new EntranceNavigationTransitionInfo()
            );
        }

        public bool NavigateTo(
            KioskRoute route,
            object? parameter = null,
            SlideNavigationTransitionEffect? effect = null)
        {
            var pageType = AppRoutes.GetPageType(route);
            var transition = effect ?? AppRoutes.GetDefaultTransition(route);
            return NavigateTo(pageType, parameter, transition);
        }

        public bool NavigateTo(
            KioskRoute route,
            SlideNavigationTransitionEffect effect)
        {
            return NavigateTo(route, null, effect);
        }

        public bool NavigateTo(
            KioskRoute route,
            object? parameter,
            NavigationTransitionInfo? transitionInfo)
        {
            var pageType = AppRoutes.GetPageType(route);
            return NavigateTo(pageType, parameter, transitionInfo);
        }

        public bool GoBack(
            NavigationTransitionInfo? transitionInfo = null)
        {
            var frame = TargetFrame;

            if (frame == null || !frame.CanGoBack)
                return false;

            // Cut off any active voice prompt on back navigation
            AppSound.StopVoice();

            if (transitionInfo != null)
            {
                try
                {
                    frame.GoBack(transitionInfo);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[NavigationService] Transition GoBack fallback: {ex.Message}");
                    frame.GoBack();
                }
            }
            else
            {
                frame.GoBack();
            }

            return true;
        }

        public bool GoBack(
            SlideNavigationTransitionEffect effect)
        {
            return GoBack(
                new EntranceNavigationTransitionInfo()
            );
        }

        public bool NavigateBackToCustomer(
            SlideNavigationTransitionEffect effect = SlideNavigationTransitionEffect.FromLeft)
        {
            var frame = TargetFrame;
            if (frame == null)
                return false;

            // Prune admin views from the top of the back stack so we return to the caller customer page
            while (frame.CanGoBack && (
                frame.BackStack.LastOrDefault()?.SourcePageType.Namespace?.Contains("Admin") == true ||
                frame.BackStack.LastOrDefault()?.SourcePageType == typeof(Views.Admin.AdminLoginView) ||
                frame.BackStack.LastOrDefault()?.SourcePageType == typeof(Views.Admin.AdminDiagnosticsView) ||
                frame.BackStack.LastOrDefault()?.SourcePageType == typeof(Views.Admin.MediaBrandingView)))
            {
                frame.BackStack.RemoveAt(frame.BackStack.Count - 1);
            }

            if (frame.CanGoBack)
            {
                return GoBack(effect);
            }

            return NavigateTo(
                KioskRoute.Attract,
                effect
            );
        }
    }
}
