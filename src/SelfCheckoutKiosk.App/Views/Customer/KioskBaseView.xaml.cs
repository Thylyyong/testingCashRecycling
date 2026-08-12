using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using SelfCheckoutKiosk.App.Services;
using SelfCheckoutKiosk.App.ViewModels.Customer;
using System;

namespace SelfCheckoutKiosk.App.Views.Customer
{
    public sealed partial class KioskBaseView : Page
    {
        public KioskBaseViewModel ViewModel { get; }

        private readonly DispatcherTimer _slideTimer;
        private bool _showingA = true;
        private bool _isTransitioning = false;
        private bool _wasManipulated = false;

        public KioskBaseView()
        {
            InitializeComponent();

            INavigationService navigationService;

            if (App.MainWindowInstance?.NavigationService != null)
            {
                navigationService = App.MainWindowInstance.NavigationService;
            }
            else
            {
                navigationService = new NavigationService(Frame);
            }

            ViewModel = new KioskBaseViewModel(navigationService);

            // Load initial banner
            if (ViewModel.BannerMediaPaths.Count > 0)
            {
                BannerImageA.Source = new BitmapImage(new Uri(ViewModel.BannerMediaPaths[0].Path));
            }

            // Auto-advance banner carousel timer
            _slideTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10)
            };
            _slideTimer.Tick += (s, e) => AdvanceBanner();
            _slideTimer.Start();

            this.Unloaded += (s, e) => _slideTimer.Stop();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateIndicators();
        }

        // Reset state on initial touch/click
        private void BannerContainer_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _wasManipulated = false;
        }

        // Flag when user starts dragging/swiping
        private void BannerContainer_ManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
        {
            _wasManipulated = true;
        }

        // Process swipe gesture
        private void BannerContainer_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            const double SwipeThreshold = 40; // Pixels required to trigger banner advance

            double totalX = e.Cumulative.Translation.X;

            if (Math.Abs(totalX) >= SwipeThreshold)
            {
                _wasManipulated = true;

                if (totalX < 0)
                {
                    AdvanceBanner(forward: true);   // Swiped left -> Next
                }
                else
                {
                    AdvanceBanner(forward: false);  // Swiped right -> Previous
                }

                ResetTimer();
            }
        }

        // Process trackpad / mouse scroll wheel
        private void BannerContainer_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var props = e.GetCurrentPoint(BannerContainer).Properties;
            int delta = props.MouseWheelDelta;

            if (delta != 0)
            {
                _wasManipulated = true;

                if (delta < 0)
                    AdvanceBanner(forward: false);
                else
                    AdvanceBanner(forward: true);

                ResetTimer();
                e.Handled = true;
            }
        }

        // Single tap or click triggers navigation ONLY if no drag/scroll occurred
        private void BannerContainer_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (!_wasManipulated)
            {
                ViewModel.ProceedToHome();
            }
        }

        private void AdvanceBanner(bool forward = true)
        {
            if (_isTransitioning || ViewModel.BannerMediaPaths.Count == 0)
                return;

            _isTransitioning = true;

            if (forward)
                ViewModel.NextBanner();
            else
                ViewModel.PreviousBanner();

            var nextPath = ViewModel.BannerMediaPaths[ViewModel.CurrentBannerIndex].Path;

            var incoming = _showingA ? BannerImageB : BannerImageA;
            var outgoing = _showingA ? BannerImageA : BannerImageB;

            incoming.Source = new BitmapImage(new Uri(nextPath));

            var fadeOut = new DoubleAnimation { From = 1.0, To = 0.0, Duration = TimeSpan.FromMilliseconds(600) };
            Storyboard.SetTarget(fadeOut, outgoing);
            Storyboard.SetTargetProperty(fadeOut, "Opacity");

            var fadeIn = new DoubleAnimation { From = 0.0, To = 1.0, Duration = TimeSpan.FromMilliseconds(600) };
            Storyboard.SetTarget(fadeIn, incoming);
            Storyboard.SetTargetProperty(fadeIn, "Opacity");

            var sb = new Storyboard();
            sb.Children.Add(fadeOut);
            sb.Children.Add(fadeIn);

            sb.Completed += (s, args) =>
            {
                _showingA = !_showingA;
                _isTransitioning = false;
            };

            sb.Begin();

            UpdateIndicators();
        }

        private void ResetTimer()
        {
            _slideTimer.Stop();
            _slideTimer.Start();
        }

        private void UpdateIndicators()
        {
            for (int i = 0; i < IndicatorDots.Items.Count; i++)
            {
                if (IndicatorDots.ContainerFromIndex(i) is ContentPresenter presenter)
                {
                    if (VisualTreeHelper.GetChild(presenter, 0) is Rectangle rect)
                    {
                        bool current = i == ViewModel.CurrentBannerIndex;

                        double targetWidth = current ? 24 : 7;
                        double targetOpacity = current ? 1.0 : 0.35;
                        TimeSpan duration = TimeSpan.FromMilliseconds(200);

                        var widthAnim = new DoubleAnimation
                        {
                            To = targetWidth,
                            Duration = duration,
                            EnableDependentAnimation = true
                        };
                        Storyboard.SetTarget(widthAnim, rect);
                        Storyboard.SetTargetProperty(widthAnim, "Width");

                        var opacityAnim = new DoubleAnimation
                        {
                            To = targetOpacity,
                            Duration = duration
                        };
                        Storyboard.SetTarget(opacityAnim, rect);
                        Storyboard.SetTargetProperty(opacityAnim, "Opacity");

                        var sb = new Storyboard();
                        sb.Children.Add(widthAnim);
                        sb.Children.Add(opacityAnim);
                        sb.Begin();

                        rect.RadiusX = 3.5;
                        rect.RadiusY = 3.5;
                    }
                }
            }
        }
    }
}