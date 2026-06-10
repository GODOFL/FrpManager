using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace FrpManager.Helpers
{
    /// <summary>
    /// Handles downloading and extracting FRP release archives from GitHub.
    /// Manages the download/ directory, ZIP extraction, version parsing,
    /// and finding the latest downloaded frpc executable.
    /// </summary>
    public static class DownloadHelper
    {
        /// <summary>
        /// Download directory path: {appBaseDir}/download/
        /// Uses AppDirHelper to correctly resolve the path even for
        /// single-file published apps (avoids temp extraction dir on C:\).
        /// </summary>
        public static string DownloadDir => AppDirHelper.DownloadDir;

        /// <summary>
        /// Extracts the semantic version from a FRP asset filename.
        /// Example: "frp_0.68.1_windows_amd64.zip" → "0.68.1"
        /// </summary>
        /// <param name="assetName">The GitHub asset filename.</param>
        /// <returns>Version string like "0.68.1", or "unknown" if parsing fails.</returns>
        public static string ParseVersion(string assetName)
        {
            var m = Regex.Match(assetName, @"(\d+\.\d+\.\d+)");
            return m.Success ? m.Groups[1].Value : "unknown";
        }

        /// <summary>
        /// Extracts a FRP ZIP archive, renames the folder to frp-{version},
        /// and cleans up the original ZIP file.
        /// </summary>
        /// <param name="zipPath">Full path to the downloaded ZIP file.</param>
        /// <param name="version">FRP version string (e.g., "0.68.1").</param>
        /// <returns>The path to the extracted directory.</returns>
        public static string ExtractAndCleanup(string zipPath, string version)
        {
            string targetDir = Path.Combine(DownloadDir, $"frp-{version}");

            // Remove existing directory for the same version (clean re-extract)
            if (Directory.Exists(targetDir))
                Directory.Delete(targetDir, true);

            // Extract to a temporary directory first
            // The ZIP typically contains a single subfolder (e.g., "frp_0.68.1_windows_amd64/")
            string tempDir = Path.Combine(DownloadDir, "_tmp_extract");
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            ZipFile.ExtractToDirectory(zipPath, tempDir);

            // Move the inner folder to the target name, or move temp if no inner folder
            var inner = Directory.GetDirectories(tempDir).FirstOrDefault();
            if (inner != null)
                Directory.Move(inner, targetDir);
            else
                Directory.Move(tempDir, targetDir);

            // Clean up temporary directory and original ZIP
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            File.Delete(zipPath);

            return targetDir;
        }

        /// <summary>
        /// Scans the download directory for the latest version of frpc.exe.
        /// Versions are compared semantically (e.g., 0.68.1 > 0.67.0).
        /// </summary>
        /// <returns>Path to the latest frpc.exe, or null if none found.</returns>
        public static string? FindLatestFrpc()
        {
            if (!Directory.Exists(DownloadDir)) return null;

            return Directory.GetDirectories(DownloadDir, "frp-*")
                .Select(dir => new
                {
                    Frpc = Path.Combine(dir, "frpc.exe"),
                    Version = ParseDirVersion(Path.GetFileName(dir))
                })
                .Where(x => File.Exists(x.Frpc))
                .OrderByDescending(x => x.Version)
                .FirstOrDefault()?.Frpc;
        }

        /// <summary>
        /// Parses a directory name like "frp-0.68.1" into a Version object for comparison.
        /// </summary>
        static Version ParseDirVersion(string dirName)
        {
            var m = Regex.Match(dirName, @"frp-(\d+\.\d+\.\d+)");
            return m.Success && Version.TryParse(m.Groups[1].Value, out var v)
                ? v : new Version(0, 0, 0);
        }
    }
}
