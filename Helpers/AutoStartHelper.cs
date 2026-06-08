using Microsoft.Win32;

namespace FrpManager.Helpers
{
    public static class AutoStartHelper
    {
        private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "FrpManager";

        public static bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                return key?.GetValue(AppName) != null;
            }
            catch { return false; }
        }

        public static void Enable()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                var exePath = Environment.ProcessPath
                    ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
                key?.SetValue(AppName, $"\"{exePath}\" --autostart");
            }
            catch { /* silently fail — user may not have registry permissions */ }
        }

        public static void Disable()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                key?.DeleteValue(AppName, throwOnMissingValue: false);
            }
            catch { }
        }

        /// <summary>Returns true if the current launch was triggered by auto-start.</summary>
        public static bool IsAutoStartLaunch()
        {
            var args = Environment.GetCommandLineArgs();
            return args.Contains("--autostart", StringComparer.OrdinalIgnoreCase);
        }
    }
}
