namespace SelfCheckoutKiosk.App.Models
{
    public class BannerMedia
    {
        public string Path { get; set; } = string.Empty;
        public bool IsVideo { get; set; }
        public bool IsCurrent { get; set; }
        public int DurationSeconds { get; set; } = 7;
    }
}