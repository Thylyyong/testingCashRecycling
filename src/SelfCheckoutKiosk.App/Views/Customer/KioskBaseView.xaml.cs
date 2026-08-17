using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using SelfCheckoutKiosk.App.Services;
using SelfCheckoutKiosk.App.ViewModels.Customer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml.Media.Imaging;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SelfCheckoutKiosk.App.Views.Customer
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class KioskBaseView : Page
    {
        public KioskBaseViewModel ViewModel { get; }
        private DispatcherTimer _slideTimer;
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
                // Fallback: If initialized early during window setup, construct service from current frame
                navigationService = new NavigationService(Frame);
            }

            ViewModel = new KioskBaseViewModel(navigationService);

            // Set up auto-slide timer
            _slideTimer = new DispatcherTimer();
            _slideTimer.Interval = TimeSpan.FromSeconds(10);

            _slideTimer.Tick += (s, e) =>
            {
                ViewModel.NextBanner();
                BannerFlipView.SelectedIndex = ViewModel.CurrentBannerIndex;
                UpdateIndicators();
            };

            _slideTimer.Start();

            // Clean up timer when leaving page
            this.Unloaded += (s, e) => _slideTimer.Stop();

            // Track manual index changes made by the user swiping
            BannerFlipView.SelectionChanged += BannerFlipView_SelectionChanged;

            // Track user touch/hold/interaction to pause the auto-slide
            BannerFlipView.PointerPressed += (s, e) => ResetTimer();
            BannerFlipView.PointerReleased += (s, e) => ResetTimer();
            BannerFlipView.PointerCanceled += (s, e) => ResetTimer();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateIndicators();
        }

        private int _previousBannerIndex = 0;
        private bool _isCrossfading = false;

        private void BannerFlipView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isCrossfading) return; // Prevent re-entrant triggers during the crossfade

            if (BannerFlipView.SelectedIndex != -1)
            {
                int newIndex = BannerFlipView.SelectedIndex;
                int totalItems = IndicatorDots.Items.Count;

                // Detect if we are wrapping from Last -> First
                bool isWrapToStart = (_previousBannerIndex == totalItems - 1 && newIndex == 0);

                if (isWrapToStart && totalItems > 1)
                {
                    _isCrossfading = true;
                    var lastBannerItem = ViewModel.BannerMediaPaths[_previousBannerIndex];
                    string path = lastBannerItem.Path;

                    if (!string.IsNullOrEmpty(path))
                    {
                        // 1. Snapshot the last image onto the overlay right on top
                        CrossfadeOverlay.Source = new BitmapImage(new Uri(path));
                        CrossfadeOverlay.Opacity = 1.0;

                        // 2. Temporarily set FlipView opacity to 0 safely without forcing layout shifts yet
                        BannerFlipView.Opacity = 0;

                        // 3. Update index
                        ViewModel.CurrentBannerIndex = 0;
                        BannerFlipView.SelectedIndex = 0;

                        // 4. Smooth crossfade duration (make it slightly longer e.g., 400ms for extra smoothness)
                        DoubleAnimation fadeOut = new DoubleAnimation
                        {
                            From = 1.0,
                            To = 0.0,
                            Duration = TimeSpan.FromMilliseconds(400)
                        };
                        Storyboard.SetTarget(fadeOut, CrossfadeOverlay);
                        Storyboard.SetTargetProperty(fadeOut, "Opacity");

                        Storyboard sb = new Storyboard();
                        sb.Children.Add(fadeOut);

                        sb.Completed += (s, args) =>
                        {
                            BannerFlipView.Opacity = 1.0;
                            CrossfadeOverlay.Source = null;
                            _isCrossfading = false;
                        };

                        sb.Begin();
                    }
                    else
                    {
                        _isCrossfading = false;
                    }
                }
                else
                {
                    ViewModel.CurrentBannerIndex = newIndex;
                }

                _previousBannerIndex = BannerFlipView.SelectedIndex;
                ResetTimer();
                UpdateIndicators();
            }
        }

        private void ResetTimer()
        {
            _slideTimer.Stop();
            _slideTimer.Start();
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ProceedToCart();
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
                        TimeSpan duration = TimeSpan.FromMilliseconds(200); // Animation speed

                        // 1. Animate Width (requires EnableDependentAnimation = true)
                        DoubleAnimation widthAnim = new DoubleAnimation
                        {
                            To = targetWidth,
                            Duration = duration,
                            EnableDependentAnimation = true
                        };
                        Storyboard.SetTarget(widthAnim, rect);
                        Storyboard.SetTargetProperty(widthAnim, "Width");

                        // 2. Animate Opacity
                        DoubleAnimation opacityAnim = new DoubleAnimation
                        {
                            To = targetOpacity,
                            Duration = duration
                        };
                        Storyboard.SetTarget(opacityAnim, rect);
                        Storyboard.SetTargetProperty(opacityAnim, "Opacity");

                        // 3. Play both animations together
                        Storyboard sb = new Storyboard();
                        sb.Children.Add(widthAnim);
                        sb.Children.Add(opacityAnim);
                        sb.Begin();

                        // Keep corner radius consistent
                        rect.RadiusX = 3.5;
                        rect.RadiusY = 3.5;
                    }
                }
            }
        }
    }
}
