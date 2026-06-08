using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace FrpManager.Helpers
{
    public static class DownloadHelper
    {
        // 下载目录：软件所在目录/download/
        public static string DownloadDir =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "download");

        // 从文件名解析版本号，如 frp_0.68.1_windows_amd64.zip → 0.68.1
        public static string ParseVersion(string assetName)
        {
            var m = Regex.Match(assetName, @"(\d+\.\d+\.\d+)");
            return m.Success ? m.Groups[1].Value : "unknown";
        }

        // 解压 zip，重命名为 frp-版本号，删除压缩包，返回解压目录
        public static string ExtractAndCleanup(string zipPath, string version)
        {
            string targetDir = Path.Combine(DownloadDir, $"frp-{version}");

            // 若已存在同版本则先删除
            if (Directory.Exists(targetDir))
                Directory.Delete(targetDir, true);

            // 解压到临时目录
            string tempDir = Path.Combine(DownloadDir, "_tmp_extract");
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            ZipFile.ExtractToDirectory(zipPath, tempDir);

            // zip 内通常只有一个子文件夹，把它移动到目标位置
            var inner = Directory.GetDirectories(tempDir).FirstOrDefault();
            if (inner != null)
                Directory.Move(inner, targetDir);
            else
                Directory.Move(tempDir, targetDir);

            // 清理临时目录和压缩包
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            File.Delete(zipPath);

            return targetDir;
        }

        // 扫描 download 目录，返回最新版本的 frpc.exe 路径
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

        static Version ParseDirVersion(string dirName)
        {
            var m = Regex.Match(dirName, @"frp-(\d+\.\d+\.\d+)");
            return m.Success && Version.TryParse(m.Groups[1].Value, out var v)
                ? v : new Version(0, 0, 0);
        }
    }
}