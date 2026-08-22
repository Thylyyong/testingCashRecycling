using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SelfCheckoutKiosk.App.Models;
using SelfCheckoutKiosk.App.Services;
using SelfCheckoutKiosk.App.Services.Audio;
using SelfCheckoutKiosk.App.ViewModels.Customer;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;

namespace SelfCheckoutKiosk.App.Views.Customer
{
    public sealed partial class KioskBaseView : Page
    {
        public KioskBaseViewModel ViewModel { get; }
        public LocalizationService Localizer => LocalizationService.Instance;

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

            _slideTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(7)
            };
            _slideTimer.Tick += (s, e) => AdvanceBanner(forward: true);

            SetupVideoPlayer();

            LocalizationService.Instance.PropertyChanged += Localizer_PropertyChanged;
            Unloaded += KioskBaseView_Unloaded;
        }

        private void SetupVideoPlayer()
        {
            try
            {
                var player = BannerVideoPlayer.MediaPlayer;
                if (player != null)
                {
                    player.AutoPlay = true;
                    player.IsLoopingEnabled = false;
                    player.MediaOpened += MediaPlayer_MediaOpened;
                    player.MediaEnded += MediaPlayer_MediaEnded;
                    player.MediaFailed += MediaPlayer_MediaFailed;
                    player.PlaybackSession.PlaybackStateChanged += PlaybackSession_PlaybackStateChanged;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BannerVideo Setup ERROR] {ex.Message}");
            }
        }

        private void MediaPlayer_MediaOpened(MediaPlayer sender, object args)
        {
            try
            {
                Debug.WriteLine($"[BannerVideo MediaOpened] Video opened!");
                DispatcherQueue?.TryEnqueue(() =>
                {
                    try { sender.Play(); } catch { }
                });
            }
            catch { }
        }

        private void PlaybackSession_PlaybackStateChanged(MediaPlaybackSession sender, object args)
        {
            try
            {
                Debug.WriteLine($"[BannerVideo PlaybackState] State: {sender.PlaybackState}");
            }
            catch { }
        }

        private void MediaPlayer_MediaEnded(MediaPlayer sender, object args)
        {
            try
            {
                DispatcherQueue?.TryEnqueue(() =>
                {
                    AdvanceBanner(forward: true);
                });
            }
            catch { }
        }

        private void MediaPlayer_MediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
        {
            try
            {
                Debug.WriteLine($"[BannerVideo MediaFailed] Error: {args.Error}");
                DispatcherQueue?.TryEnqueue(() =>
                {
                    AdvanceBanner(forward: true);
                });
            }
            catch { }
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateIndicators();
            DisplayCurrentMedia(isInitial: true);
        }

        private void Localizer_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // {x:Bind Localizer.GetString(...)} refreshes automatically
        }

        private void TearDownVideoPlayer()
        {
            try
            {
                var player = BannerVideoPlayer.MediaPlayer;
                if (player != null)
                {
                    try { player.MediaOpened -= MediaPlayer_MediaOpened; } catch { }
                    try { player.MediaEnded -= MediaPlayer_MediaEnded; } catch { }
                    try { player.MediaFailed -= MediaPlayer_MediaFailed; } catch { }
                    try { player.PlaybackSession.PlaybackStateChanged -= PlaybackSession_PlaybackStateChanged; } catch { }
                    try { player.Pause(); } catch { }
                    try { player.Source = null; } catch { }
                }
                BannerVideoPlayer.Source = null;
            }
            catch { }
        }

        private void KioskBaseView_Unloaded(object sender, RoutedEventArgs e)
        {
            _slideTimer.Stop();
            TearDownVideoPlayer();
            LocalizationService.Instance.PropertyChanged -= Localizer_PropertyChanged;
        }

        private void BannerContainer_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _wasManipulated = false;
        }

        private void BannerContainer_ManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
        {
            _wasManipulated = true;
        }

        private void BannerContainer_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            const double SwipeThreshold = 40;
            double totalX = e.Cumulative.Translation.X;

            if (Math.Abs(totalX) >= SwipeThreshold)
            {
                _wasManipulated = true;

                if (totalX < 0)
                    AdvanceBanner(forward: true);
                else
                    AdvanceBanner(forward: false);
            }
        }

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

                e.Handled = true;
            }
        }

        private void BannerContainer_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (!_wasManipulated)
            {
                _wasManipulated = true;
                _slideTimer.Stop();
                TearDownVideoPlayer();
                ViewModel.ProceedToHome();
                e.Handled = true;
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

            DisplayCurrentMedia(isInitial: false);
            UpdateIndicators();
        }

        private async Task<MediaSource?> CreateMediaSourceAsync(string path)
        {
            try
            {
                string localPath = path;
                if (path.StartsWith("ms-appx:///", StringComparison.OrdinalIgnoreCase))
                {
                    string relative = path.Substring("ms-appx:///".Length).Replace('/', System.IO.Path.DirectorySeparatorChar);
                    localPath = System.IO.Path.Combine(AppContext.BaseDirectory, relative);
                }
                else if (path.StartsWith("ms-appx://", StringComparison.OrdinalIgnoreCase))
                {
                    string relative = path.Substring("ms-appx://".Length).Replace('/', System.IO.Path.DirectorySeparatorChar);
                    localPath = System.IO.Path.Combine(AppContext.BaseDirectory, relative);
                }

                Debug.WriteLine($"[BannerVideo] Loading video from path: '{localPath}' (File exists: {System.IO.File.Exists(localPath)})");

                if (System.IO.File.Exists(localPath))
                {
                    var file = await StorageFile.GetFileFromPathAsync(System.IO.Path.GetFullPath(localPath));
                    return MediaSource.CreateFromStorageFile(file);
                }

                if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
                {
                    return MediaSource.CreateFromUri(uri);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BannerVideo CreateMediaSource ERROR] {ex}");
            }

            return null;
        }

        private async void DisplayCurrentMedia(bool isInitial = false)
        {
            if (ViewModel.BannerMediaPaths.Count == 0) return;

            var current = ViewModel.BannerMediaPaths[ViewModel.CurrentBannerIndex];
            Debug.WriteLine($"[Banner] Displaying index {ViewModel.CurrentBannerIndex}: '{current.Path}' (IsVideo: {current.IsVideo})");

            if (current.IsVideo)
            {
                // Stop image slide timer while video plays
                _slideTimer.Stop();

                var mediaSource = await CreateMediaSourceAsync(current.Path);
                if (mediaSource != null)
                {
                    BannerVideoPlayer.Source = mediaSource;
                    BannerVideoPlayer.Opacity = 1.0;

                    var player = BannerVideoPlayer.MediaPlayer;
                    if (player != null)
                    {
                        player.IsMuted = !current.IsAudioEnabled;
                        player.Volume = current.IsAudioEnabled ? 1.0 : 0.0;
                        player.IsLoopingEnabled = false;
                        player.Play();
                    }

                    AnimateOpacity(BannerImageA, 0.0, 300);
                    AnimateOpacity(BannerImageB, 0.0, 300, () => _isTransitioning = false);
                }
                else
                {
                    Debug.WriteLine($"[BannerVideo] Video source not found for '{current.Path}'. Advancing...");
                    _isTransitioning = false;
                    AdvanceBanner(forward: true);
                }
            }
            else
            {
                // Pause and fade out video player when displaying an image
                try
                {
                    var player = BannerVideoPlayer.MediaPlayer;
                    player?.Pause();
                }
                catch { }

                var incoming = _showingA ? BannerImageB : BannerImageA;
                var outgoing = _showingA ? BannerImageA : BannerImageB;

                try
                {
                    incoming.Source = new BitmapImage(GetMediaUri(current.Path));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[BannerImage ERROR] Failed to load image '{current.Path}': {ex.Message}");
                }

                AnimateOpacity(BannerVideoPlayer, 0.0, 300);
                AnimateOpacity(outgoing, 0.0, 400);
                AnimateOpacity(incoming, 1.0, 400, () =>
                {
                    _showingA = !_showingA;
                    _isTransitioning = false;
                });

                // Start timer for image duration
                int duration = current.DurationSeconds > 0 ? current.DurationSeconds : 7;
                _slideTimer.Interval = TimeSpan.FromSeconds(duration);
                _slideTimer.Stop();
                _slideTimer.Start();
            }
        }

        private Uri GetMediaUri(string path)
        {
            try
            {
                if (path.StartsWith("ms-appx:///", StringComparison.OrdinalIgnoreCase))
                {
                    string relative = path.Substring("ms-appx:///".Length).Replace('/', System.IO.Path.DirectorySeparatorChar);
                    string localPath = System.IO.Path.Combine(AppContext.BaseDirectory, relative);
                    if (System.IO.File.Exists(localPath))
                    {
                        return new Uri(localPath);
                    }
                }
                return new Uri(path);
            }
            catch
            {
                return new Uri(path, UriKind.RelativeOrAbsolute);
            }
        }

        private void AnimateOpacity(UIElement target, double toOpacity, double durationMs, Action? onCompleted = null)
        {
            var anim = new DoubleAnimation
            {
                To = toOpacity,
                Duration = TimeSpan.FromMilliseconds(durationMs)
            };

            Storyboard.SetTarget(anim, target);
            Storyboard.SetTargetProperty(anim, "Opacity");

            var sb = new Storyboard();
            sb.Children.Add(anim);

            if (onCompleted != null)
            {
                sb.Completed += (s, e) => onCompleted();
            }

            sb.Begin();
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

                        var widthAnim = new DoubleAnimation
                        {
                            To = targetWidth,
                            Duration = TimeSpan.FromMilliseconds(200),
                            EnableDependentAnimation = true
                        };

                        Storyboard.SetTarget(widthAnim, rect);
                        Storyboard.SetTargetProperty(widthAnim, "Width");

                        var opacityAnim = new DoubleAnimation
                        {
                            To = targetOpacity,
                            Duration = TimeSpan.FromMilliseconds(200)
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