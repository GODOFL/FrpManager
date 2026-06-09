using Microsoft.Win32;

namespace FrpManager.Helpers
{
    /// <summary>
    /// Manages Windows auto-start registration via the registry Run key.
    /// When enabled, FrpManager launches with the --autostart flag at Windows login,
    /// which triggers silent background mode (hidden window, tray icon only).
    /// </summary>
    public static class AutoStartHelper
    {
        /// <summary>Registry path for per-user startup programs.</summary>
        private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        /// <summary>Registry value name for FrpManager.</summary>
        private const string AppName = "FrpManager";

        /// <summary>
        /// Checks whether FrpManager is currently registered for auto-start.
        /// </summary>
        /// <returns>True if the registry entry exists; false otherwise or on error.</returns>
        public static bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                return key?.GetValue(AppName) != null;
            }
            catch { return false; }
        }

        /// <summary>
        /// Registers FrpManager in the Windows Run key for auto-start.
        /// The entry launches the app with the --autostart flag for silent mode.
        /// Failures are silently ignored (e.g., insufficient permissions).
        /// </summary>
        public static void Enable()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                // Use Environment.ProcessPath for the current executable path,
                // falling back to AppContext.BaseDirectory
                var exePath = Environment.ProcessPath
                    ?? System.AppContext.BaseDirectory;
                // Quote the path to handle spaces, append --autostart flag
                key?.SetValue(AppName, $"\"{exePath}\" --autostart");
            }
            catch { /* Silently fail — user may not have registry write permissions */ }
        }

        /// <summary>
        /// Removes FrpManager from the Windows Run key.
        /// Failures are silently ignored.
        /// </summary>
        public static void Disable()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                // throwOnMissingValue: false prevents exception if entry doesn't exist
                key?.DeleteValue(AppName, throwOnMissingValue: false);
            }
            catch { }
        }

        /// <summary>
        /// Determines whether the current application launch was triggered by auto-start.
        /// Checks for the --autostart flag in command-line arguments.
        /// </summary>
        /// <returns>True if the app was launched with --autostart.</returns>
        public static bool IsAutoStartLaunch()
        {
            var args = Environment.GetCommandLineArgs();
            return args.Contains("--autostart", StringComparer.OrdinalIgnoreCase);
        }
    }
}
