using System.IO;
using System.Reflection;

namespace FrpManager.Helpers
{
    /// <summary>
    /// Provides centralized directory paths for the application.
    ///
    /// Handles two deployment scenarios where the runtime directory is not
    /// the user-facing application directory:
    ///   1. Single-file publish with IncludeNativeLibrariesForSelfExtract=true
    ///      → everything extracts to %TEMP%\.net\... on startup.
    ///   2. Framework-dependent dotnet run / MSIX packaging
    ///      → assemblies resolve to temp or a package cache.
    ///
    /// Strategy:
    ///   A) If we're NOT in a single-file bundle → use AppContext.BaseDirectory
    ///      (the real output/publish directory next to the exe/dll).
    ///   B) If we ARE in a single-file bundle AND the process IS running from
    ///      the real exe (not temp) → use the exe directory (AppContext).
    ///   C) If we ARE in a single-file bundle AND everything IS in temp
    ///      (self-extract) → we can't find the original host exe location
    ///      via managed APIs, so we fall back to the user's local app data.
    ///
    /// Case C should be avoided entirely by NOT publishing with
    /// IncludeNativeLibrariesForSelfExtract=true unless required for
    /// a platform that can't load native libs from a bundle. On Windows,
    /// a plain single-file publish keeps the exe in its real location.
    /// </summary>
    public static class AppDirHelper
    {
        private static string? _baseDirectory;

        /// <summary>
        /// Gets the application data base directory. All persistent app data
        /// (tomlset config library, FRP downloads) live under this path.
        /// </summary>
        public static string BaseDirectory
        {
            get
            {
                if (_baseDirectory != null)
                    return _baseDirectory;

                var exePath = Environment.ProcessPath;
                var exeDir = !string.IsNullOrEmpty(exePath)
                    ? Path.GetDirectoryName(exePath)!
                    : null;

                var appBase = AppContext.BaseDirectory.TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var tempPath = Path.GetTempPath().TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                // ── Detect single-file bundle (Assembly.Location is empty) ──
                bool isSingleFile = string.IsNullOrEmpty(
                    Assembly.GetEntryAssembly()?.Location);

                // ── Check if we're running from a temp extraction dir ──
                bool baseInTemp = appBase.StartsWith(tempPath,
                    StringComparison.OrdinalIgnoreCase);
                bool exeInTemp = exeDir != null && exeDir.StartsWith(tempPath,
                    StringComparison.OrdinalIgnoreCase);

                if (!isSingleFile && !baseInTemp)
                {
                    // Normal deployment: everything is in the real output dir
                    _baseDirectory = AppContext.BaseDirectory;
                }
                else if (isSingleFile && !baseInTemp)
                {
                    // Single-file NOT extracted to temp → running from real location
                    _baseDirectory = AppContext.BaseDirectory;
                }
                else
                {
                    // We're in a temp directory (either self-extract single-file
                    // or framework-dependent packaging). Fall back to a stable
                    // location under LocalAppData so user data isn't lost when
                    // temp is cleaned.
                    _baseDirectory = Path.Combine(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.LocalApplicationData),
                        "FrpManager");
                }

                // Ensure the directory exists
                try { Directory.CreateDirectory(_baseDirectory); }
                catch { /* Best effort — callers handle missing dirs */ }

                return _baseDirectory;
            }
        }

        /// <summary>TOML config file library directory: {BaseDir}/tomlset</summary>
        public static string TomlsetDir =>
            Path.Combine(BaseDirectory, "tomlset");

        /// <summary>FRP binary download directory: {BaseDir}/download</summary>
        public static string DownloadDir =>
            Path.Combine(BaseDirectory, "download");

        /// <summary>
        /// Preferred app icon path. The icon is packaged beside the executable,
        /// while persistent app data may live elsewhere in single-file mode.
        /// </summary>
        public static string AppIconPath
        {
            get
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exePath))
                {
                    var exeIcon = Path.Combine(Path.GetDirectoryName(exePath)!, "app.ico");
                    if (File.Exists(exeIcon))
                        return exeIcon;
                }

                return Path.Combine(BaseDirectory, "app.ico");
            }
        }

        /// <summary>
        /// Resets the cached base directory. Call after initial setup if the
        /// deployment context changes (e.g., in test scenarios).
        /// </summary>
        public static void Reset() => _baseDirectory = null;
    }
}
