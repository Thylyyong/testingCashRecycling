using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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
    public sealed partial class KioskBaseView2 : Page
    {
        public KioskBaseViewModel ViewModel { get; }

        private DispatcherTimer _slideTimer;
        private bool _showingA = true;
        private bool _isTransitioning = false;

        public KioskBaseView2()
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

            // Show first banner immediately
            if (ViewModel.BannerMediaPaths.Count > 0)
            {
                BannerImageA.Source = new BitmapImage(new Uri(ViewModel.BannerMediaPaths[0].Path));
            }

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

        private void BannerContainer_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var props = e.GetCurrentPoint(BannerContainer).Properties;

            // Horizontal wheel delta = two-finger trackpad swipe (or shift+scroll on a mouse)
            int delta = props.MouseWheelDelta;
            bool isHorizontal = props.IsHorizontalMouseWheel;

            if (!isHorizontal)
                return; // ignore normal vertical scroll

            if (delta < 0)
                AdvanceBanner(forward: false);
            else
                AdvanceBanner(forward: true);

            ResetTimer();
            e.Handled = true;
        }

        private void BannerContainer_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            const double SwipeThreshold = 50; // pixels

            double totalX = e.Cumulative.Translation.X;

            if (Math.Abs(totalX) < SwipeThreshold)
                return; // treat as a tap, not a swipe

            if (totalX < 0)
            {
                AdvanceBanner(forward: true);   // swiped left -> next
            }
            else
            {
                AdvanceBanner(forward: false);  // swiped right -> previous
            }

            ResetTimer();
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

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ProceedToHome();
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

                        double targetWidth = current ? 20 : 7;
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