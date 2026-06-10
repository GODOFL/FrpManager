using FrpManager.Models;
using Newtonsoft.Json;
using System.IO;

namespace FrpManager.Helpers
{
    /// <summary>
    /// Static helper for loading and saving application settings.
    /// Settings are stored as JSON in %AppData%/FrpManager/settings.json.
    /// Also provides auto-scanning for frpc executable in common locations.
    /// </summary>
    public static class SettingsHelper
    {
        /// <summary>Settings directory: %AppData%/FrpManager</summary>
        private static readonly string Dir =
            Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData), "FrpManager");

        /// <summary>Settings file path: %AppData%/FrpManager/settings.json</summary>
        private static readonly string File =
            Path.Combine(Dir, "settings.json");

        /// <summary>
        /// Loads application settings from disk.
        /// Returns a new AppSettings with defaults if the file doesn't exist or is corrupted.
        /// </summary>
        /// <returns>Deserialized AppSettings instance (never null).</returns>
        public static AppSettings Load()
        {
            try
            {
                if (System.IO.File.Exists(File))
                    return JsonConvert.DeserializeObject<AppSettings>(
                        System.IO.File.ReadAllText(File)) ?? new();
            }
            catch { /* Corrupted file — fall through to return defaults */ }
            return new();
        }

        /// <summary>
        /// Saves application settings to disk as indented JSON.
        /// Creates the settings directory if it doesn't exist.
        /// Exceptions are silently caught to prevent crashes during auto-save.
        /// </summary>
        /// <param name="s">Settings object to persist.</param>
        public static void Save(AppSettings s)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                System.IO.File.WriteAllText(File,
                    JsonConvert.SerializeObject(s, Formatting.Indented));
            }
            catch { /* Permission denied or disk full — non-critical, silently ignore */ }
        }

        // ── Auto-scan for frpc executable ────────────────────────────────────
        /// <summary>
        /// Scans common directories and PATH entries for frpc.exe / frpc.
        /// Searches: current dir, app dir, Downloads, AppData, ProgramFiles,
        /// C:\frp, D:\frp, and all directories in the system PATH.
        /// </summary>
        /// <returns>List of unique frpc executable paths found.</returns>
        public static List<string> ScanForFrpc()
        {
            // Use HashSet for O(1) dedup (case-insensitive paths)
            var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Priority search directories — common installation locations
            var dirs = new List<string>
            {
                Environment.CurrentDirectory,
                AppDirHelper.BaseDirectory,
                Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile), "Downloads"),
                Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData), "frp"),
                Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData), "frp"),
                @"C:\frp",
                @"C:\frpc",
                @"D:\frp",
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "frp"),
            };

            // Also check all directories in the system PATH environment variable
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var p in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries))
                dirs.Add(p.Trim());

            // Search each directory for files matching "frpc*"
            foreach (var dir in dirs)
            {
                if (!Directory.Exists(dir)) continue;
                try
                {
                    foreach (var f in Directory.GetFiles(dir, "frpc*",
                        SearchOption.TopDirectoryOnly))
                    {
                        // Only match exact "frpc" or "frpc.exe" filenames (not "frpc_xxx")
                        var fname = Path.GetFileNameWithoutExtension(f);
                        if (fname.Equals("frpc", StringComparison.OrdinalIgnoreCase))
                            results.Add(f);
                    }
                }
                catch { /* Directory permission denied — skip and continue */ }
            }
            return results.ToList();
        }
    }
}
