using System.Windows;
using Application = System.Windows.Application;

namespace FrpManager.Helpers
{
    /// <summary>
    /// Manages UI localization (i18n) for the application.
    /// Supports Chinese (zh-CN) and English (en-US) via WPF ResourceDictionaries.
    /// Switches language at runtime by replacing the merged dictionary.
    /// </summary>
    public class LocalizationService
    {
        /// <summary>The currently active language code.</summary>
        public string CurrentLanguage { get; private set; } = "zh-CN";

        /// <summary>
        /// Fired when the language is changed, so UI elements can refresh their labels.
        /// </summary>
        public event Action? LanguageChanged;

        /// <summary>
        /// Initializes the localization service with a saved language preference.
        /// If the saved language is not recognized, falls back to zh-CN (Chinese).
        /// </summary>
        /// <param name="savedLang">Language code from settings (zh-CN or en-US).</param>
        public void Initialize(string savedLang)
        {
            if (savedLang is "en-US" or "zh-CN")
            {
                CurrentLanguage = savedLang;
                LoadResourceDictionary(savedLang);
            }
            else
            {
                // Unknown language — default to Chinese
                CurrentLanguage = "zh-CN";
            }
        }

        /// <summary>
        /// Toggles the current language between zh-CN and en-US.
        /// Reloads the resource dictionary and notifies listeners.
        /// </summary>
        /// <returns>The new language code after toggling.</returns>
        public string Toggle()
        {
            CurrentLanguage = CurrentLanguage == "zh-CN" ? "en-US" : "zh-CN";
            LoadResourceDictionary(CurrentLanguage);
            LanguageChanged?.Invoke();
            return CurrentLanguage;
        }

        /// <summary>
        /// Looks up a localized string by its resource key.
        /// Returns the key itself if the resource is not found (graceful degradation).
        /// </summary>
        /// <param name="key">Resource key (e.g., "S_StartFrpc").</param>
        /// <returns>The localized string, or the key if not found.</returns>
        public string Get(string key)
        {
            return Application.Current.TryFindResource(key) as string ?? key;
        }

        /// <summary>
        /// Swaps the current localization resource dictionary with the one for the target language.
        /// Removes any existing localization dictionary before adding the new one.
        /// </summary>
        /// <param name="lang">Language code: "zh-CN" or "en-US".</param>
        private static void LoadResourceDictionary(string lang)
        {
            var dicts = Application.Current.Resources.MergedDictionaries;

            // Remove any existing localization dictionary
            var existing = dicts.FirstOrDefault(d =>
                d.Source?.OriginalString.Contains("Localization") == true);
            if (existing != null) dicts.Remove(existing);

            // Add the new localization dictionary
            var uri = new Uri($"Localization/Strings.{lang}.xaml", UriKind.Relative);
            dicts.Add(new ResourceDictionary { Source = uri });
        }
    }
}
