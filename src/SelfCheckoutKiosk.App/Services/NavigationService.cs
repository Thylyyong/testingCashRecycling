using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        bool GoBack(NavigationTransitionInfo? transitionInfo = null);

        bool GoBack(SlideNavigationTransitionEffect effect);

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

            if (transitionInfo != null)
            {
                return frame.Navigate(
                    pageType,
                    parameter,
                    transitionInfo
                );
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
            return NavigateTo(
                pageType,
                parameter,
                new SlideNavigationTransitionInfo
                {
                    Effect = effect
                }
            );
        }

        public bool GoBack(
            NavigationTransitionInfo? transitionInfo = null)
        {
            var frame = TargetFrame;

            if (frame == null || !frame.CanGoBack)
                return false;

            if (transitionInfo != null)
            {
                frame.GoBack(transitionInfo);
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
                new SlideNavigationTransitionInfo
                {
                    Effect = effect
                }
            );
        }
    }
}
