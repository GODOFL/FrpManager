using System.Windows;
using Application = System.Windows.Application;

namespace FrpManager.Helpers
{
    public class LocalizationService
    {
        public string CurrentLanguage { get; private set; } = "zh-CN";
        public event Action? LanguageChanged;

        /// <summary>Load the saved language or fall back to zh-CN.</summary>
        public void Initialize(string savedLang)
        {
            if (savedLang is "en-US" or "zh-CN")
            {
                CurrentLanguage = savedLang;
                LoadResourceDictionary(savedLang);
            }
            else
            {
                CurrentLanguage = "zh-CN";
            }
        }

        /// <summary>Toggle between zh-CN and en-US. Returns the new language code.</summary>
        public string Toggle()
        {
            CurrentLanguage = CurrentLanguage == "zh-CN" ? "en-US" : "zh-CN";
            LoadResourceDictionary(CurrentLanguage);
            LanguageChanged?.Invoke();
            return CurrentLanguage;
        }

        /// <summary>Look up a localized string by resource key.</summary>
        public string Get(string key)
        {
            return Application.Current.TryFindResource(key) as string ?? key;
        }

        private static void LoadResourceDictionary(string lang)
        {
            var dicts = Application.Current.Resources.MergedDictionaries;
            var existing = dicts.FirstOrDefault(d =>
                d.Source?.OriginalString.Contains("Localization") == true);
            if (existing != null) dicts.Remove(existing);

            var uri = new Uri($"Localization/Strings.{lang}.xaml", UriKind.Relative);
            dicts.Add(new ResourceDictionary { Source = uri });
        }
    }
}
