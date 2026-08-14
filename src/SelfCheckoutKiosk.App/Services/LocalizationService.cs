using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;

namespace SelfCheckoutKiosk.App.Services
{
    public class LocalizationService : INotifyPropertyChanged
    {
        public static LocalizationService Instance { get; } = new();

        private Dictionary<string, string> _dictionary = new();

        public string CurrentLanguage { get; private set; } = "km";

        // Indexer retained for code-behind or classical {Binding} usage
        public string this[string key] => GetString(key);

        // Required for WinUI 3 compiled bindings: {x:Bind Localizer.GetString('Key'), Mode=OneWay}
        public string GetString(string key)
        {
            return _dictionary.TryGetValue(key, out var val) ? val : key;
        }

        // Short alias if you prefer: {x:Bind Localizer.Get('Key'), Mode=OneWay}
        public string Get(string key) => GetString(key);

        public async Task SetLanguageAsync(string langCode)
        {
            CurrentLanguage = langCode;

            // Persist user selection globally
            ApplicationData.Current.LocalSettings.Values["AppLanguage"] = langCode;

            try
            {
                var uri = new Uri($"ms-appx:///Assets/i18n/{langCode}.json");
                var file = await StorageFile.GetFileFromApplicationUriAsync(uri);
                var json = await FileIO.ReadTextAsync(file);

                // Uses generated JsonTypeInfo for AOT/Trimming safety
                _dictionary = JsonSerializer.Deserialize(
                    json,
                    LocalizationJsonContext.Default.DictionaryStringString) ?? new();
            }
            catch
            {
                _dictionary = new();
            }

            // Raising PropertyChanged with string.Empty triggers XAML to re-evaluate all method bindings
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}