using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace JudasEncodingManager.Services
{
    /// <summary>
    /// Wraps Crunchy-DL (CRD.exe) — launch, update, and output-folder monitoring.
    /// CRD is a GUI application; JEM integrates with it by watching the configured
    /// output folder for newly downloaded episode files.
    /// </summary>
    public class CRDService
    {
        public event EventHandler<string>? LogMessage;

        private string _crdDir = "";

        public void Configure(string crdDir)
        {
            _crdDir = crdDir;
        }

        // ==================== INSTALLATION ====================

        public string ExecutablePath => Path.Combine(_crdDir, "CRD.exe");
        public string UpdaterPath    => Path.Combine(_crdDir, "Updater.exe");

        public bool IsInstalled() => File.Exists(ExecutablePath);

        /// <summary>
        /// Reads the version from the CHANGELOG.md bundled with CRD.
        /// Falls back to file-version if the changelog is missing.
        /// </summary>
        public string GetInstalledVersion()
        {
            try
            {
                var changelog = Path.Combine(_crdDir, "CHANGELOG.md");
                if (File.Exists(changelog))
                {
                    foreach (var line in File.ReadLines(changelog))
                    {
                        // Matches "## [v1.6.14] - 2026-06-26"
                        var m = Regex.Match(line, @"\[v?([\d\.]+)\]");
                        if (m.Success)
                            return "v" + m.Groups[1].Value;
                    }
                }

                if (File.Exists(ExecutablePath))
                {
                    var info = FileVersionInfo.GetVersionInfo(ExecutablePath);
                    if (!string.IsNullOrEmpty(info.FileVersion))
                        return "v" + info.FileVersion;
                }
            }
            catch { }

            return "Unknown";
        }

        // ==================== PROCESS ====================

        /// <summary>Returns true if a CRD.exe process is currently running.</summary>
        public bool IsRunning()
        {
            try
            {
                return Process.GetProcessesByName("CRD").Length > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Launches CRD.exe as a separate GUI process.</summary>
        public bool Launch()
        {
            if (!IsInstalled())
            {
                Log("[CRD] CRD.exe not found at configured path.");
                return false;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName         = ExecutablePath,
                    WorkingDirectory = _crdDir,
                    UseShellExecute  = true
                });
                Log("[CRD] Launched CRD.exe.");
                return true;
            }
            catch (Exception ex)
            {
                Log($"[CRD] Failed to launch: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Runs Updater.exe — CRD's built-in updater handles its own update lifecycle.
        /// </summary>
        public bool RunUpdater()
        {
            if (!File.Exists(UpdaterPath))
            {
                Log("[CRD] Updater.exe not found.");
                return false;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName         = UpdaterPath,
                    WorkingDirectory = _crdDir,
                    UseShellExecute  = true
                });
                Log("[CRD] Launched Updater.exe.");
                return true;
            }
            catch (Exception ex)
            {
                Log($"[CRD] Failed to launch updater: {ex.Message}");
                return false;
            }
        }

        // ==================== OUTPUT FOLDER MONITORING ====================

        /// <summary>
        /// Checks the show's CRD output folder for an episode file matching
        /// <paramref name="expectedEpisode"/>. Returns the full path of the
        /// matched file, or null if not found.
        /// </summary>
        public string? FindEpisodeFile(string outputFolder, string filePattern, int expectedEpisode)
        {
            if (string.IsNullOrWhiteSpace(outputFolder) || !Directory.Exists(outputFolder))
                return null;

            try
            {
                var searchPattern = string.IsNullOrWhiteSpace(filePattern) ? "*.mkv" : filePattern;

                foreach (var file in Directory.GetFiles(outputFolder, searchPattern, SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileNameWithoutExtension(file);

                    // Match common episode number patterns: E01, EP01, Episode 1, - 01, S01E01
                    var patterns = new[]
                    {
                        $@"[Ee](?:pisode\s*)?{expectedEpisode:D2}(?!\d)",
                        $@"[Ee][Pp]?{expectedEpisode:D2}(?!\d)",
                        $@"[Ss]\d+[Ee]{expectedEpisode:D2}(?!\d)",
                        $@"[-_ \.]{expectedEpisode:D2}(?!\d)",
                        $@"[-_ \.]{expectedEpisode:D3}(?!\d)",
                    };

                    foreach (var p in patterns)
                    {
                        if (Regex.IsMatch(name, p, RegexOptions.IgnoreCase))
                        {
                            // Make sure the file is not still being written (size stable for 2s)
                            if (IsFileComplete(file))
                                return file;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[CRD] Error scanning folder '{outputFolder}': {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Returns true when a file's size has been stable for at least 2 seconds,
        /// indicating CRD has finished writing it.
        /// </summary>
        private static bool IsFileComplete(string path)
        {
            try
            {
                var size1 = new FileInfo(path).Length;
                System.Threading.Thread.Sleep(2000);
                var size2 = new FileInfo(path).Length;
                return size1 == size2 && size1 > 0;
            }
            catch
            {
                return false;
            }
        }

        // ==================== HELPERS ====================

        private void Log(string message)
        {
            LogMessage?.Invoke(this, message);
        }
    }
}
