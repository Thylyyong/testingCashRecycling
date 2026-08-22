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

        private static readonly string PrimarySettingFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_settings.json");
        private static readonly string FallbackSettingFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SelfCheckoutKiosk",
            "app_settings.json");

        public string CurrentLanguage { get; private set; } = "en";

        // Indexer for classical {Binding} usage
        public string this[string key] => GetString(key);

        // For WinUI 3 compiled bindings: {x:Bind Localizer.GetString('Key'), Mode=OneWay}
        public string GetString(string key)
        {
            return _dictionary.TryGetValue(key, out var val) ? val : key;
        }

        public string Get(string key) => GetString(key);

        public string GetPersistedLanguage()
        {
            try
            {
                if (AppInstanceIsPackaged())
                {
                    var pkgVal = Windows.Storage.ApplicationData.Current.LocalSettings.Values["AppLanguage"] as string;
                    if (!string.IsNullOrWhiteSpace(pkgVal)) return pkgVal;
                }
            }
            catch { }

            try
            {
                string? loadPath = null;
                if (File.Exists(PrimarySettingFile))
                {
                    loadPath = PrimarySettingFile;
                }
                else if (File.Exists(FallbackSettingFile))
                {
                    loadPath = FallbackSettingFile;
                }

                if (loadPath != null)
                {
                    string json = File.ReadAllText(loadPath);
                    var dict = JsonSerializer.Deserialize(
                        json,
                        LocalizationJsonContext.Default.DictionaryStringString);
                    if (dict != null && dict.TryGetValue("AppLanguage", out var langVal) && !string.IsNullOrWhiteSpace(langVal))
                    {
                        return langVal;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Localization] Could not read persisted language setting: {ex.Message}");
            }

            return "en";
        }

        public async Task SetLanguageAsync(string langCode)
        {
            CurrentLanguage = langCode;

            // 1. Safely save setting to persistent storage (Packaged + Unpackaged fallback)
            SaveLanguagePreference(langCode);

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

        private void SaveLanguagePreference(string langCode)
        {
            try
            {
                if (AppInstanceIsPackaged())
                {
                    Windows.Storage.ApplicationData.Current.LocalSettings.Values["AppLanguage"] = langCode;
                }
            }
            catch { }

            try
            {
                var settingsDict = new Dictionary<string, string>();
                string? existingPath = File.Exists(PrimarySettingFile) ? PrimarySettingFile
                    : File.Exists(FallbackSettingFile) ? FallbackSettingFile : null;

                if (existingPath != null)
                {
                    try
                    {
                        string existingJson = File.ReadAllText(existingPath);
                        var existing = JsonSerializer.Deserialize(
                            existingJson,
                            LocalizationJsonContext.Default.DictionaryStringString);
                        if (existing != null)
                        {
                            settingsDict = existing;
                        }
                    }
                    catch { }
                }

                settingsDict["AppLanguage"] = langCode;
                string updatedJson = JsonSerializer.Serialize(settingsDict, LocalizationJsonContext.Default.DictionaryStringString);

                try
                {
                    File.WriteAllText(PrimarySettingFile, updatedJson);
                    System.Diagnostics.Debug.WriteLine($"[Localization] Saved language preference to: {PrimarySettingFile}");
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
                {
                    string? dir = Path.GetDirectoryName(FallbackSettingFile);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    File.WriteAllText(FallbackSettingFile, updatedJson);
                    System.Diagnostics.Debug.WriteLine($"[Localization] Fallback: Saved language preference to: {FallbackSettingFile}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Localization] Failed to persist language preference: {ex.Message}");
            }
        }

        private static bool AppInstanceIsPackaged()
        {
            try
            {
                return Windows.ApplicationModel.Package.Current != null;
            }
            catch
            {
                return false;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}