using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace JudasEncodingManager.Services
{
    public class AniDLReleaseInfo
    {
        public string TagName { get; set; } = "";
        public string Version => TagName.TrimStart('v');
        public string DownloadUrl { get; set; } = "";
        public string HtmlUrl { get; set; } = "";
        public DateTime PublishedAt { get; set; }
    }

    public class AniDLUpdateService
    {
        private static readonly HttpClient _http;
        private const string GH_API = "https://api.github.com/repos/anidl/multi-downloader-nx/releases/latest";
        private const string ASSET_KEYWORD = "windows-x64-cli";

        static AniDLUpdateService()
        {
            _http = new HttpClient();
            _http.DefaultRequestHeaders.Add("User-Agent", "JudasEncodingManager/1.0");
            _http.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
            _http.Timeout = TimeSpan.FromSeconds(30);
        }

        public event EventHandler<string>? StatusChanged;

        /// <summary>
        /// Checks GitHub for the latest release and returns its info.
        /// Returns null on failure.
        /// </summary>
        public async Task<AniDLReleaseInfo?> GetLatestReleaseAsync(CancellationToken ct = default)
        {
            try
            {
                StatusChanged?.Invoke(this, "Checking GitHub for latest aniDL release...");

                var response = await _http.GetAsync(GH_API, ct);
                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                var root = doc.RootElement;

                var info = new AniDLReleaseInfo
                {
                    TagName = root.GetProperty("tag_name").GetString() ?? "",
                    HtmlUrl = root.GetProperty("html_url").GetString() ?? "",
                    PublishedAt = root.GetProperty("published_at").GetDateTime()
                };

                // Find the windows-x64-cli asset in the assets array
                if (root.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var name = asset.GetProperty("name").GetString() ?? "";
                        if (name.Contains(ASSET_KEYWORD, StringComparison.OrdinalIgnoreCase))
                        {
                            info.DownloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                            StatusChanged?.Invoke(this, $"Latest release: {info.TagName} (asset: {name})");
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(info.DownloadUrl))
                {
                    StatusChanged?.Invoke(this, $"[WARN] No {ASSET_KEYWORD} asset found in release {info.TagName}. Download manually from: {info.HtmlUrl}");
                }

                return info;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"[ERROR] Failed to check GitHub: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Downloads the windows-x64-cli zip from GitHub and overwrites the existing
        /// aniDL installation directory. All existing files are replaced.
        /// </summary>
        public async Task<bool> DownloadAndOverwriteAsync(
            string downloadUrl,
            string aniDLDir,
            IProgress<double>? progress = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                StatusChanged?.Invoke(this, "[ERROR] No download URL provided.");
                return false;
            }

            var tempZip = Path.Combine(Path.GetTempPath(), $"anidl-update-{Guid.NewGuid():N}.zip");
            var tempExtract = Path.Combine(Path.GetTempPath(), $"anidl-extract-{Guid.NewGuid():N}");

            try
            {
                // --- Phase 1: Download (0 → 70%) ---
                StatusChanged?.Invoke(this, $"Downloading from {downloadUrl}...");

                using var response = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? 0L;
                var bytesRead = 0L;

                using (var contentStream = await response.Content.ReadAsStreamAsync(ct))
                using (var fileStream = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true))
                {
                    var buffer = new byte[8192];
                    int read;
                    while ((read = await contentStream.ReadAsync(buffer, ct)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                        bytesRead += read;
                        if (totalBytes > 0)
                            progress?.Report((double)bytesRead / totalBytes * 0.70);
                    }
                }

                StatusChanged?.Invoke(this, $"Download complete ({FormatBytes(bytesRead)}). Extracting...");
                progress?.Report(0.75);

                // --- Phase 2: Extract to temp folder (70 → 85%) ---
                Directory.CreateDirectory(tempExtract);
                ZipFile.ExtractToDirectory(tempZip, tempExtract, overwriteFiles: true);
                progress?.Report(0.85);

                // The zip typically contains one top-level folder
                var sourceDir = tempExtract;
                var subDirs = Directory.GetDirectories(tempExtract);
                if (subDirs.Length == 1)
                    sourceDir = subDirs[0];

                // --- Phase 3: Overwrite aniDL directory (85 → 100%) ---
                StatusChanged?.Invoke(this, $"Overwriting files in {aniDLDir}...");
                Directory.CreateDirectory(aniDLDir);
                CopyDirectory(sourceDir, aniDLDir);
                progress?.Report(1.0);

                StatusChanged?.Invoke(this, "✅ Update applied successfully. All files overwritten.");
                return true;
            }
            catch (OperationCanceledException)
            {
                StatusChanged?.Invoke(this, "[WARN] Update cancelled.");
                return false;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"[ERROR] Update failed: {ex.Message}");
                return false;
            }
            finally
            {
                try { File.Delete(tempZip); } catch { }
                try { Directory.Delete(tempExtract, recursive: true); } catch { }
            }
        }

        /// <summary>
        /// Convenience overload: downloads the asset from <paramref name="release"/> and
        /// overwrites the aniDL installation at <paramref name="aniDLDir"/>.
        /// </summary>
        public Task<bool> DownloadAndInstallAsync(
            AniDLReleaseInfo release,
            string aniDLDir,
            IProgress<double>? progress = null,
            CancellationToken ct = default)
        {
            return DownloadAndOverwriteAsync(release.DownloadUrl, aniDLDir, progress, ct);
        }

        private static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (var file in Directory.GetFiles(sourceDir))
                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);

            foreach (var sub in Directory.GetDirectories(sourceDir))
                CopyDirectory(sub, Path.Combine(destDir, Path.GetFileName(sub)));
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F1} MB";
            if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes} B";
        }
    }
}
