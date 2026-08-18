using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace SelfCheckoutKiosk.App.Services
{
    public class LocalizationService : INotifyPropertyChanged
    {
        public static LocalizationService Instance { get; } = new();

        private Dictionary<string, string> _dictionary = new();

        public string CurrentLanguage { get; private set; } = "en";

        // Indexer for classical {Binding} usage
        public string this[string key] => GetString(key);

        // For WinUI 3 compiled bindings: {x:Bind Localizer.GetString('Key'), Mode=OneWay}
        public string GetString(string key)
        {
            return _dictionary.TryGetValue(key, out var val) ? val : key;
        }

        public string Get(string key) => GetString(key);

        public async Task SetLanguageAsync(string langCode)
        {
            CurrentLanguage = langCode;

            // 1. Safely save setting (prevents crash in unpackaged builds)
            try
            {
                if (Windows.ApplicationModel.Package.Current != null)
                {
                    Windows.Storage.ApplicationData.Current.LocalSettings.Values["AppLanguage"] = langCode;
                }
            }
            catch
            {
                // App is running unpackaged, ignore or write to a local config file if needed
            }

            // 2. Read JSON file using standard System.IO (Works everywhere in Release/Publish)
            try
            {
                string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "i18n", $"{langCode}.json");

                if (File.Exists(filePath))
                {
                    string json = await File.ReadAllTextAsync(filePath);

                    _dictionary = JsonSerializer.Deserialize(
                        json,
                        LocalizationJsonContext.Default.DictionaryStringString) ?? new();
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[Localization] File not found at: {filePath}");
                    _dictionary = new();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Localization Error] {ex.Message}");
                _dictionary = new();
            }

            // 3. Notify XAML bindings to refresh text
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}