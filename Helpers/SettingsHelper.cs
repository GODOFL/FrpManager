using FrpManager.Models;
using Newtonsoft.Json;
using System.IO;

namespace FrpManager.Helpers
{
    public static class SettingsHelper
    {
        private static readonly string Dir =
            Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData), "FrpManager");
        private static readonly string File =
            Path.Combine(Dir, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (System.IO.File.Exists(File))
                    return JsonConvert.DeserializeObject<AppSettings>(
                        System.IO.File.ReadAllText(File)) ?? new();
            }
            catch { }
            return new();
        }

        public static void Save(AppSettings s)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                System.IO.File.WriteAllText(File,
                    JsonConvert.SerializeObject(s, Formatting.Indented));
            }
            catch { }
        }

        // ── Auto-scan for frpc executable ─────────────────────────────────
        public static List<string> ScanForFrpc()
        {
            var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dirs = new List<string>
            {
                Environment.CurrentDirectory,
                AppDomain.CurrentDomain.BaseDirectory,
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

            // Also check PATH entries
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var p in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries))
                dirs.Add(p.Trim());

            foreach (var dir in dirs)
            {
                if (!Directory.Exists(dir)) continue;
                try
                {
                    foreach (var f in Directory.GetFiles(dir, "frpc*",
                        SearchOption.TopDirectoryOnly))
                    {
                        var fname = Path.GetFileNameWithoutExtension(f);
                        if (fname.Equals("frpc", StringComparison.OrdinalIgnoreCase))
                            results.Add(f);
                    }
                }
                catch { /* no permission */ }
            }
            return results.ToList();
        }
    }
}
