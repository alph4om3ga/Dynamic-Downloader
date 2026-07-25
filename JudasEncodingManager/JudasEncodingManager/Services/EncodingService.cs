using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using JudasEncodingManager.Models;
using JudasEncodingManager.Utilities;

namespace JudasEncodingManager.Services
{
    public class EncodingService
    {
        public event EventHandler<string>? OutputReceived;
        public event EventHandler<double>? ProgressChanged;

        private Process? _currentProcess;
        private bool _isCancelled;

        public string EncodingScriptsPath { get; set; } = "";
        public string OutputPath { get; set; } = "";

        public async Task<EncodingResult> EncodeAsync(QueueItem item, string workerConfigPath, CancellationToken cancellationToken)
        {
            var result = new EncodingResult();
            _isCancelled = false;

            try
            {
                var showConfigPath = Path.Combine(EncodingScriptsPath, item.Show.IniScriptName);
                
                if (!File.Exists(showConfigPath))
                {
                    result.Success = false;
                    result.ErrorMessage = $"INI file not found: {showConfigPath}";
                    return result;
                }

                if (!File.Exists(item.SourceFilePath))
                {
                    result.Success = false;
                    result.ErrorMessage = $"Source file not found: {item.SourceFilePath}";
                    return result;
                }

                // Determine output filename - output directly to Seeding folder (no subfolder)
                var outputFileName = $"{item.OutputFileName}.mkv";
                var outputFilePath = Path.Combine(OutputPath, outputFileName);

                // Ensure output directory exists
                if (!Directory.Exists(OutputPath))
                {
                    Directory.CreateDirectory(OutputPath);
                }

                // For test runs, we'll encode directly with ffmpeg/x265 for just 5 minutes
                if (item.IsTestRun)
                {
                    result = await EncodeTestRunAsync(item, outputFilePath, cancellationToken);
                }
                else
                {
                    result = await EncodeFullAsync(item, showConfigPath, workerConfigPath, outputFilePath, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }
            finally
            {
                _currentProcess?.Dispose();
                _currentProcess = null;
            }

            return result;
        }

        private async Task<EncodingResult> EncodeFullAsync(QueueItem item, string showConfigPath, string workerConfigPath, string outputFilePath, CancellationToken cancellationToken)
        {
            var result = new EncodingResult();

            // Build PowerShell command with NonInteractive to prevent any prompts
            var psScriptPath = Path.Combine(EncodingScriptsPath, "Start-BatchJudasEncode.ps1");
            var arguments = $"-ExecutionPolicy Bypass -NoProfile -NonInteractive -File \"{psScriptPath}\" " +
                           $"-ShowConfigPath \"{showConfigPath}\" " +
                           $"-WorkerConfigPath \"{workerConfigPath}\" " +
                           $"-Output \"{outputFilePath}\"";

            OutputReceived?.Invoke(this, $"[FULL ENCODE] ========================================");
            OutputReceived?.Invoke(this, $"[FULL ENCODE] Script: {psScriptPath}");
            OutputReceived?.Invoke(this, $"[FULL ENCODE] ShowConfig: {showConfigPath}");
            OutputReceived?.Invoke(this, $"[FULL ENCODE] WorkerConfig: {workerConfigPath}");
            OutputReceived?.Invoke(this, $"[FULL ENCODE] Output: {outputFilePath}");
            OutputReceived?.Invoke(this, $"[FULL ENCODE] WorkingDir: {EncodingScriptsPath}");
            OutputReceived?.Invoke(this, $"[FULL ENCODE] ========================================");

            // Verify all required files exist
            if (!File.Exists(psScriptPath))
            {
                result.Success = false;
                result.ErrorMessage = $"PowerShell script not found: {psScriptPath}";
                OutputReceived?.Invoke(this, $"[ERROR] {result.ErrorMessage}");
                return result;
            }
            OutputReceived?.Invoke(this, $"[CHECK] ✓ PowerShell script exists");

            if (!File.Exists(showConfigPath))
            {
                result.Success = false;
                result.ErrorMessage = $"Show config not found: {showConfigPath}";
                OutputReceived?.Invoke(this, $"[ERROR] {result.ErrorMessage}");
                return result;
            }
            OutputReceived?.Invoke(this, $"[CHECK] ✓ Show config exists");

            if (!File.Exists(workerConfigPath))
            {
                result.Success = false;
                result.ErrorMessage = $"Worker config not found: {workerConfigPath}";
                OutputReceived?.Invoke(this, $"[ERROR] {result.ErrorMessage}");
                return result;
            }
            OutputReceived?.Invoke(this, $"[CHECK] ✓ Worker config exists");

            // List video files in working directory
            var videoFiles = Directory.GetFiles(EncodingScriptsPath, "*.mkv")
                .Concat(Directory.GetFiles(EncodingScriptsPath, "*.m2ts"))
                .ToList();
            OutputReceived?.Invoke(this, $"[CHECK] Found {videoFiles.Count} video file(s) in encoding folder:");
            foreach (var vf in videoFiles)
            {
                OutputReceived?.Invoke(this, $"  - {Path.GetFileName(vf)}");
            }

            if (videoFiles.Count == 0)
            {
                result.Success = false;
                result.ErrorMessage = $"No video files (.mkv or .m2ts) found in encoding folder: {EncodingScriptsPath}";
                OutputReceived?.Invoke(this, $"[ERROR] {result.ErrorMessage}");
                return result;
            }

            // Check subdirectories
            var subDirs = new[] { "video", "audio-subs", "data", "done" };
            foreach (var subDir in subDirs)
            {
                var subPath = Path.Combine(EncodingScriptsPath, subDir);
                if (Directory.Exists(subPath))
                    OutputReceived?.Invoke(this, $"[CHECK] ✓ Subdirectory exists: {subDir}/");
                else
                    OutputReceived?.Invoke(this, $"[CHECK] ✗ Subdirectory missing: {subDir}/");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = arguments,
                WorkingDirectory = EncodingScriptsPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            OutputReceived?.Invoke(this, $"[FULL ENCODE] Starting PowerShell with args:");
            OutputReceived?.Invoke(this, $"  {arguments}");

            try
            {
                result.StartTime = DateTime.Now;
                _currentProcess = new Process { StartInfo = startInfo };
                _currentProcess.EnableRaisingEvents = true;

                _currentProcess.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        OutputReceived?.Invoke(this, e.Data);
                        ParseProgress(e.Data);
                    }
                };

                _currentProcess.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        OutputReceived?.Invoke(this, $"[STDERR] {e.Data}");
                        ParseProgress(e.Data);
                    }
                };

                _currentProcess.Start();
                OutputReceived?.Invoke(this, $"[FULL ENCODE] Process started with PID: {_currentProcess.Id}");
                
                _currentProcess.BeginOutputReadLine();
                _currentProcess.BeginErrorReadLine();

                // Wait for process to complete or cancellation
                var lastHeartbeat = DateTime.Now;
                var heartbeatInterval = TimeSpan.FromSeconds(30);
                
                while (!_currentProcess.HasExited)
                {
                    if (cancellationToken.IsCancellationRequested || _isCancelled)
                    {
                        OutputReceived?.Invoke(this, "[FULL ENCODE] Cancellation requested, killing process...");
                        try
                        {
                            _currentProcess.Kill(true);
                            _currentProcess.WaitForExit(5000);
                        }
                        catch { }
                        
                        result.Success = false;
                        result.ErrorMessage = "Encoding cancelled";
                        return result;
                    }
                    
                    // Periodic heartbeat to show process is still running
                    if (DateTime.Now - lastHeartbeat > heartbeatInterval)
                    {
                        var elapsed = DateTime.Now - result.StartTime;
                        OutputReceived?.Invoke(this, $"[HEARTBEAT] Process still running... ({elapsed.TotalMinutes:F1} min elapsed, PID: {_currentProcess.Id})");
                        lastHeartbeat = DateTime.Now;
                    }
                    
                    await Task.Delay(500);
                }

                // Important: Wait for async output to complete
                _currentProcess.WaitForExit();

                result.EndTime = DateTime.Now;
                result.ExitCode = _currentProcess.ExitCode;
                OutputReceived?.Invoke(this, $"[FULL ENCODE] Process exited with code: {result.ExitCode}");

                if (_currentProcess.ExitCode == 0 && File.Exists(outputFilePath))
                {
                    result.Success = true;
                    result.OutputFilePath = outputFilePath;
                    result.FileSize = new FileInfo(outputFilePath).Length;
                    OutputReceived?.Invoke(this, $"[FULL ENCODE] Success! Output: {outputFilePath} ({result.FileSizeFormatted})");
                }
                else
                {
                    result.Success = false;
                    result.ErrorMessage = $"Encoding failed with exit code {_currentProcess.ExitCode}";
                    if (!File.Exists(outputFilePath))
                    {
                        result.ErrorMessage += $" (output file not found: {outputFilePath})";
                    }
                    OutputReceived?.Invoke(this, $"[ERROR] {result.ErrorMessage}");
                }
            }
            finally
            {
                // Ensure process is fully disposed
                if (_currentProcess != null)
                {
                    try
                    {
                        if (!_currentProcess.HasExited)
                        {
                            _currentProcess.Kill(true);
                            _currentProcess.WaitForExit(5000);
                        }
                    }
                    catch { }
                    
                    _currentProcess.Dispose();
                    _currentProcess = null;
                }
            }

            return result;
        }

        private async Task<EncodingResult> EncodeTestRunAsync(QueueItem item, string outputFilePath, CancellationToken cancellationToken)
        {
            var result = new EncodingResult { StartTime = DateTime.Now };
            
            OutputReceived?.Invoke(this, $"[TEST RUN] Encoding {item.TestEncodeDurationSeconds} seconds only");

            // Use ffmpeg to encode only the first 5 minutes with x265
            // This is a simplified encode for testing the pipeline
            var duration = TimeSpan.FromSeconds(item.TestEncodeDurationSeconds);
            var durationStr = duration.ToString(@"hh\:mm\:ss");

            var arguments = $"-y -i \"{item.SourceFilePath}\" -t {durationStr} " +
                           $"-c:v libx265 -preset fast -crf 23 -pix_fmt yuv420p10le " +
                           $"-c:a libopus -b:a 128k -ac 2 " +
                           $"-c:s copy " +
                           $"\"{outputFilePath}\"";

            OutputReceived?.Invoke(this, $"[TEST RUN] Running: ffmpeg {arguments}");

            var startInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            try
            {
                _currentProcess = new Process { StartInfo = startInfo };
                _currentProcess.EnableRaisingEvents = true;

                var errorOutput = new System.Text.StringBuilder();

                _currentProcess.OutputDataReceived += (s, e) =>
                {
                    // FFmpeg doesn't really use stdout, but read it anyway to prevent deadlock
                };

                _currentProcess.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        errorOutput.AppendLine(e.Data);
                        OutputReceived?.Invoke(this, e.Data);
                        
                        // Parse ffmpeg progress
                        var timeMatch = Regex.Match(e.Data, @"time=(\d{2}):(\d{2}):(\d{2})");
                        if (timeMatch.Success)
                        {
                            var hours = int.Parse(timeMatch.Groups[1].Value);
                            var minutes = int.Parse(timeMatch.Groups[2].Value);
                            var seconds = int.Parse(timeMatch.Groups[3].Value);
                            var currentSeconds = hours * 3600 + minutes * 60 + seconds;
                            var progress = (double)currentSeconds / item.TestEncodeDurationSeconds;
                            ProgressChanged?.Invoke(this, Math.Min(progress, 1.0));
                        }
                    }
                };

                _currentProcess.Start();
                _currentProcess.BeginOutputReadLine();
                _currentProcess.BeginErrorReadLine();

                // Wait for process with cancellation support
                while (!_currentProcess.HasExited)
                {
                    if (cancellationToken.IsCancellationRequested || _isCancelled)
                    {
                        OutputReceived?.Invoke(this, "[TEST RUN] Cancelling...");
                        try 
                        { 
                            _currentProcess.Kill(true);
                            _currentProcess.WaitForExit(5000); // Wait up to 5 seconds for kill
                        } 
                        catch { }
                        
                        result.Success = false;
                        result.ErrorMessage = "Test encoding cancelled";
                        return result;
                    }
                    await Task.Delay(500);
                }

                // Important: Wait for async output to complete
                _currentProcess.WaitForExit();

                result.EndTime = DateTime.Now;
                result.ExitCode = _currentProcess.ExitCode;

                if (_currentProcess.ExitCode == 0 && File.Exists(outputFilePath))
                {
                    result.Success = true;
                    result.OutputFilePath = outputFilePath;
                    result.FileSize = new FileInfo(outputFilePath).Length;
                    OutputReceived?.Invoke(this, $"[TEST RUN] Complete! File size: {result.FileSizeFormatted}");
                }
                else
                {
                    result.Success = false;
                    result.ErrorMessage = $"Test encoding failed with exit code {_currentProcess.ExitCode}";
                    OutputReceived?.Invoke(this, $"[TEST RUN] Failed: {result.ErrorMessage}");
                }
            }
            finally
            {
                // Ensure process is fully disposed
                if (_currentProcess != null)
                {
                    try
                    {
                        if (!_currentProcess.HasExited)
                        {
                            _currentProcess.Kill(true);
                            _currentProcess.WaitForExit(5000);
                        }
                    }
                    catch { }
                    
                    _currentProcess.Dispose();
                    _currentProcess = null;
                }
            }

            return result;
        }

        public void Cancel()
        {
            _isCancelled = true;
            try
            {
                _currentProcess?.Kill(true);
            }
            catch { }
        }

        private void ParseProgress(string output)
        {
            // Parse x265 progress: [12.3%] 1234 frames
            var percentMatch = Regex.Match(output, @"\[(\d+\.?\d*)%\]");
            if (percentMatch.Success && double.TryParse(percentMatch.Groups[1].Value, out double percent))
            {
                ProgressChanged?.Invoke(this, percent / 100.0);
            }

            // Parse frame count progress
            var frameMatch = Regex.Match(output, @"(\d+)/(\d+) frames");
            if (frameMatch.Success)
            {
                if (int.TryParse(frameMatch.Groups[1].Value, out int current) &&
                    int.TryParse(frameMatch.Groups[2].Value, out int total) &&
                    total > 0)
                {
                    ProgressChanged?.Invoke(this, (double)current / total);
                }
            }
        }
    }

    public class EncodingResult
    {
        public bool Success { get; set; }
        public string OutputFilePath { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
        public int ExitCode { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public long FileSize { get; set; }
        public TimeSpan Duration => EndTime - StartTime;
        public string FileSizeFormatted => FileUtilities.FormatFileSize(FileSize);
    }
}
