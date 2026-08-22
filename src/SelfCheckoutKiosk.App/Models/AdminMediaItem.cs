using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace SelfCheckoutKiosk.App.Models
{
    public class AdminMediaItem : INotifyPropertyChanged
    {
        private string _id = Guid.NewGuid().ToString("N");
        private string _fileName = string.Empty;
        private string _mediaType = "Image"; // "Image" or "Video"
        private int _durationSeconds = 10;
        private bool _isActive = true;
        private bool _isAudioEnabled = true;
        private int _sortOrder = 0;

        public string Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        public string FileName
        {
            get => _fileName;
            set { _fileName = value; OnPropertyChanged(); }
        }

        public string MediaType
        {
            get => _mediaType;
            set
            {
                _mediaType = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsVideo));
                OnPropertyChanged(nameof(TypeGlyph));
                OnPropertyChanged(nameof(VideoVisibility));
            }
        }

        public bool IsVideo => string.Equals(_mediaType, "Video", StringComparison.OrdinalIgnoreCase);

        public Visibility VideoVisibility => IsVideo ? Visibility.Visible : Visibility.Collapsed;

        public string TypeGlyph => IsVideo ? "\uE714" : "\uEB9F"; // Video icon vs Image icon

        public string UriPath
        {
            get
            {
                if (string.IsNullOrWhiteSpace(FileName))
                    return "ms-appx:///Assets/Images/Banner/banner1.jpg";

                if (FileName.StartsWith("ms-appx://", StringComparison.OrdinalIgnoreCase) ||
                    FileName.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
                    FileName.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    FileName.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    return FileName;
                }

                return IsVideo
                    ? $"ms-appx:///Assets/Videos/{FileName}"
                    : $"ms-appx:///Assets/Images/Banner/{FileName}";
            }
        }

        public int DurationSeconds
        {
            get => _durationSeconds;
            set { _durationSeconds = value; OnPropertyChanged(); OnPropertyChanged(nameof(DurationText)); }
        }

        public string DurationText => IsVideo ? "Full Video Length" : $"{DurationSeconds}s";

        public bool IsActive
        {
            get => _isActive;
            set { _isActive = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); }
        }

        public string StatusText => IsActive ? "Active" : "Inactive";

        public bool IsAudioEnabled
        {
            get => _isAudioEnabled;
            set
            {
                if (_isAudioEnabled != value)
                {
                    _isAudioEnabled = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SoundGlyph));
                    OnPropertyChanged(nameof(SoundTooltip));
                    OnPropertyChanged(nameof(SoundForegroundBrush));
                }
            }
        }

        public string SoundGlyph => IsAudioEnabled ? "\uE767" : "\uE74F"; // Volume vs Mute

        public string SoundTooltip => IsAudioEnabled ? "Audio: ON (Sound enabled)" : "Audio: OFF (Muted)";

        public Brush SoundForegroundBrush => IsAudioEnabled
            ? new SolidColorBrush(Color.FromArgb(255, 37, 99, 235))   // Blue #2563EB
            : new SolidColorBrush(Color.FromArgb(255, 148, 163, 184)); // Muted slate #94A3B8

        public int SortOrder
        {
            get => _sortOrder;
            set { _sortOrder = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
