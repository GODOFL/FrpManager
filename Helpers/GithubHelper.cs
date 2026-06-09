using FrpManager.Models;
using Newtonsoft.Json;
using System.Net.Http;
using System.Net.Http.Headers;

namespace FrpManager.Helpers
{
    /// <summary>
    /// Static helper for interacting with the GitHub Releases API.
    /// Fetches FRP release metadata and downloads release asset files.
    /// Uses a shared HttpClient with User-Agent header for API compliance.
    /// </summary>
    public static class GithubHelper
    {
        /// <summary>
        /// Shared HttpClient with a 30s timeout and FrpManager User-Agent.
        /// GitHub API requires a User-Agent header; requests without one are rejected.
        /// </summary>
        private static readonly HttpClient _http = CreateClient();

        private static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FrpManager", "2.0"));
            return c;
        }

        /// <summary>
        /// Fetches the most recent releases from the fatedier/frp GitHub repository.
        /// </summary>
        /// <param name="count">Number of releases to fetch (default 6).</param>
        /// <returns>List of GitHubRelease objects, or empty list on failure.</returns>
        public static async Task<List<GitHubRelease>> GetReleasesAsync(int count = 6)
        {
            var json = await _http.GetStringAsync(
                $"https://api.github.com/repos/fatedier/frp/releases?per_page={count}");
            return JsonConvert.DeserializeObject<List<GitHubRelease>>(json) ?? new();
        }

        /// <summary>
        /// Downloads a file from a URL with progress reporting and cancellation support.
        /// Streams the response directly to disk in 64KB chunks.
        /// </summary>
        /// <param name="url">The download URL (typically GitHub asset browser_download_url).</param>
        /// <param name="savePath">Full path where the file should be saved.</param>
        /// <param name="progress">Optional progress reporter (0-100 as double).</param>
        /// <param name="ct">Cancellation token to abort the download.</param>
        public static async Task DownloadAsync(
            string url, string savePath,
            IProgress<double>? progress = null,
            CancellationToken ct = default)
        {
            using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            // Get total file size for progress calculation (-1 if unknown)
            var total = resp.Content.Headers.ContentLength ?? -1L;

            // Ensure the target directory exists
            var dir = System.IO.Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);

            // Stream download in 64KB chunks with progress reporting
            await using var src  = await resp.Content.ReadAsStreamAsync(ct);
            await using var dest = new System.IO.FileStream(savePath, System.IO.FileMode.Create);

            var buf = new byte[65536]; // 64KB buffer
            long done = 0;
            int  read;
            while ((read = await src.ReadAsync(buf, ct)) > 0)
            {
                await dest.WriteAsync(buf.AsMemory(0, read), ct);
                done += read;
                if (total > 0) progress?.Report(done * 100.0 / total);
            }
        }
    }
}
