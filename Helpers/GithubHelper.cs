using FrpManager.Models;
using Newtonsoft.Json;
using System.Net.Http;
using System.Net.Http.Headers;

namespace FrpManager.Helpers
{
    public static class GithubHelper
    {
        private static readonly HttpClient _http = CreateClient();

        private static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FrpManager", "2.0"));
            return c;
        }

        public static async Task<List<GitHubRelease>> GetReleasesAsync(int count = 6)
        {
            var json = await _http.GetStringAsync(
                $"https://api.github.com/repos/fatedier/frp/releases?per_page={count}");
            return JsonConvert.DeserializeObject<List<GitHubRelease>>(json) ?? new();
        }

        public static async Task DownloadAsync(
            string url, string savePath,
            IProgress<double>? progress = null,
            CancellationToken ct = default)
        {
            using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? -1L;

            var dir = System.IO.Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);

            await using var src  = await resp.Content.ReadAsStreamAsync(ct);
            await using var dest = new System.IO.FileStream(savePath, System.IO.FileMode.Create);

            var buf = new byte[65536];
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
