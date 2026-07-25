using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace JudasEncodingManager.Services
{
    public class AniDLSearchResult
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Type { get; set; } = "";
        public string SeasonCount { get; set; } = "";

        public override string ToString() => string.IsNullOrEmpty(Id) ? Title : $"[{Id}] {Title}";
    }

    public class AniDLService
    {
        public event EventHandler<string>? OutputReceived;

        private string _aniDLDir = "";

        public void Configure(string aniDLDir)
        {
            _aniDLDir = aniDLDir;
        }

        public string GetExecutablePath() => Path.Combine(_aniDLDir, "aniDL.exe");

        /// <summary>
        /// Reads the version from package.json in the aniDL directory.
        /// </summary>
        public string GetInstalledVersion()
        {
            return GetInstalledVersion(_aniDLDir);
        }

        /// <summary>
        /// Reads the version from package.json in the given directory (path override).
        /// </summary>
        public string GetInstalledVersion(string dirPath)
        {
            try
            {
                var pkgPath = Path.Combine(dirPath, "package.json");
                if (!File.Exists(pkgPath)) return "Not found";

                var json = File.ReadAllText(pkgPath);
                var match = Regex.Match(json, "\"version\"\\s*:\\s*\"([^\"]+)\"");
                return match.Success ? match.Groups[1].Value : "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        /// <summary>
        /// Returns true if aniDL.exe exists in the configured directory.
        /// </summary>
        public bool IsInstalled() => File.Exists(GetExecutablePath());

        /// <summary>
        /// Returns true if aniDL.exe exists in the given directory (path override).
        /// </summary>
        public bool IsInstalled(string dirPath) => File.Exists(Path.Combine(dirPath, "aniDL.exe"));

        /// <summary>
        /// Searches a service for anime. Returns raw output lines and any parsed results.
        /// </summary>
        public async Task<(List<AniDLSearchResult> Results, string RawOutput)> SearchAsync(
            string service,
            string query,
            Action<string>? onOutput = null,
            CancellationToken ct = default)
        {
            var results = new List<AniDLSearchResult>();
            var raw = await RunCommandAsync($"--service {service} --search \"{query}\"", onOutput, ct);

            // Parse lines like: G6Q5Y  Tower of God Season 2   ...
            // or:               [1234]  Show Name  (2024)
            foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (trimmed.Length < 3) continue;

                // Skip log/header lines
                if (trimmed.StartsWith("[INFO]") || trimmed.StartsWith("[WARN]") ||
                    trimmed.StartsWith("[ERROR]") || trimmed.StartsWith("Search") ||
                    trimmed.StartsWith("Found") || trimmed.StartsWith("--") ||
                    trimmed.StartsWith("aniDL"))
                    continue;

                // Match ID patterns: alphanumeric IDs at start of line
                // e.g. "G6Q5Y  Tower of God" or "S4     Some Show (Movie)"
                var match = Regex.Match(trimmed, @"^([A-Za-z0-9]{4,10})\s{2,}(.+)$");
                if (match.Success)
                {
                    var id = match.Groups[1].Value;
                    var rest = match.Groups[2].Value.Trim();

                    // Extract type if present: "Show Name (Movie, 2024)" or "Show Name S3"
                    var typeMatch = Regex.Match(rest, @"^(.*?)\s*\((.*?)\)\s*$");
                    if (typeMatch.Success)
                    {
                        results.Add(new AniDLSearchResult
                        {
                            Id = id,
                            Title = typeMatch.Groups[1].Value.Trim(),
                            Type = typeMatch.Groups[2].Value.Trim()
                        });
                    }
                    else
                    {
                        results.Add(new AniDLSearchResult { Id = id, Title = rest });
                    }
                }
            }

            return (results, raw);
        }

        /// <summary>
        /// Downloads episodes for a given season ID.
        /// </summary>
        public async Task<bool> DownloadAsync(
            string service,
            string seasonId,
            string episodes,
            Action<string>? onOutput = null,
            CancellationToken ct = default)
        {
            if (!IsInstalled())
            {
                onOutput?.Invoke("[ERROR] aniDL.exe not found. Please configure the aniDL path.");
                return false;
            }

            var args = $"--service {service} -s \"{seasonId}\"";
            if (!string.IsNullOrWhiteSpace(episodes))
                args += $" -e {episodes}";

            var output = await RunCommandAsync(args, onOutput, ct);

            var failed = output.Contains("[ERROR]") || output.Contains("Error:") ||
                         output.Contains("error:") || output.Contains("failed");
            return !failed;
        }

        /// <summary>
        /// Opens an interactive console window for authentication.
        /// </summary>
        public void LaunchAuth(string service)
        {
            var exe = GetExecutablePath();
            if (!File.Exists(exe)) return;

            var startInfo = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"--service {service} --auth",
                WorkingDirectory = _aniDLDir,
                UseShellExecute = true // Keep console visible for interactive login
            };

            Process.Start(startInfo);
        }

        private async Task<string> RunCommandAsync(
            string args,
            Action<string>? onOutput,
            CancellationToken ct)
        {
            var exe = GetExecutablePath();
            if (!File.Exists(exe))
            {
                var msg = $"[ERROR] aniDL.exe not found at: {exe}";
                OutputReceived?.Invoke(this, msg);
                onOutput?.Invoke(msg);
                return msg;
            }

            var sb = new StringBuilder();

            var startInfo = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                WorkingDirectory = _aniDLDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.EnableRaisingEvents = true;

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                // Strip ANSI escape codes
                var clean = Regex.Replace(e.Data, @"\x1b\[[0-9;]*[mK]", "");
                sb.AppendLine(clean);
                OutputReceived?.Invoke(this, clean);
                onOutput?.Invoke(clean);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                var clean = Regex.Replace(e.Data, @"\x1b\[[0-9;]*[mK]", "");
                sb.AppendLine(clean);
                OutputReceived?.Invoke(this, $"[ERR] {clean}");
                onOutput?.Invoke($"[ERR] {clean}");
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await process.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); } catch { }
            }

            return sb.ToString();
        }
    }
}
