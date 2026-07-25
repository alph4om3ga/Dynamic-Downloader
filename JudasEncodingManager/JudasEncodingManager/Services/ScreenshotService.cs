using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace JudasEncodingManager.Services
{
    public class ScreenshotService
    {
        private readonly HttpClient _httpClient;
        private string _ffmpegPath = "ffmpeg";
        private string _imgbbApiKey = "";
        private bool _isCancelled;
        private Process? _currentProcess;

        public string OutputFolder { get; set; } = "";
        
        public event EventHandler<string>? LogMessage;

        public ScreenshotService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(2);
        }

        public void Configure(string ffmpegPath, string imgbbApiKey)
        {
            // Normalize path - replace forward slashes with backslashes
            if (!string.IsNullOrEmpty(ffmpegPath))
            {
                _ffmpegPath = ffmpegPath.Replace("/", "\\");
            }
            _imgbbApiKey = imgbbApiKey;
            
            Log($"ScreenshotService configured: ffmpeg={_ffmpegPath}, imgbb key={(_imgbbApiKey?.Length > 0 ? "set" : "not set")}");
        }

        public void Cancel()
        {
            _isCancelled = true;
            try
            {
                _currentProcess?.Kill();
            }
            catch { }
            Log("Screenshot operation cancelled");
        }

        private void Log(string message)
        {
            LogMessage?.Invoke(this, message);
        }

        public async Task<List<string>> TakeScreenshotsAsync(string videoPath, int count = 3)
        {
            var screenshots = new List<string>();
            _isCancelled = false;

            try
            {
                Log($"📸 Taking {count} screenshots from: {videoPath}");
                
                // Check if video file exists
                if (!File.Exists(videoPath))
                {
                    Log($"❌ Video file not found: {videoPath}");
                    return screenshots;
                }

                // Check if ffmpeg exists
                var ffmpegExists = File.Exists(_ffmpegPath);
                if (!ffmpegExists)
                {
                    // Try to find ffmpeg in PATH
                    try
                    {
                        var testProcess = Process.Start(new ProcessStartInfo
                        {
                            FileName = "ffmpeg",
                            Arguments = "-version",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            CreateNoWindow = true
                        });
                        testProcess?.WaitForExit(5000);
                        if (testProcess?.ExitCode == 0)
                        {
                            _ffmpegPath = "ffmpeg";
                            ffmpegExists = true;
                            Log("✅ Found ffmpeg in PATH");
                        }
                    }
                    catch
                    {
                        Log($"❌ ffmpeg not found at: {_ffmpegPath} and not in PATH");
                        return screenshots;
                    }
                }
                else
                {
                    Log($"✅ ffmpeg found at: {_ffmpegPath}");
                }

                // Get video duration
                Log("Getting video duration...");
                var duration = await GetVideoDurationAsync(videoPath);
                if (duration <= TimeSpan.Zero)
                {
                    Log("⚠️ Could not determine video duration, using 30 minutes as fallback");
                    duration = TimeSpan.FromMinutes(30);
                }
                else
                {
                    Log($"✅ Video duration: {duration}");
                }

                // Calculate random timestamps within first 25% of the video
                var maxTime = duration.TotalSeconds * 0.25;
                var random = new Random();
                var timestamps = new List<double>();

                for (int i = 0; i < count; i++)
                {
                    // Ensure minimum 10 seconds into the video to avoid black frames
                    var minTime = Math.Min(10, maxTime * 0.1);
                    var timestamp = minTime + (random.NextDouble() * (maxTime - minTime));
                    timestamps.Add(timestamp);
                }

                // Sort timestamps so screenshots are in order
                timestamps.Sort();
                Log($"Screenshot timestamps: {string.Join(", ", timestamps.ConvertAll(t => TimeSpan.FromSeconds(t).ToString(@"mm\:ss")))}");

                // Use OutputFolder if set, otherwise use temp directory
                var outputDir = !string.IsNullOrEmpty(OutputFolder) && Directory.Exists(OutputFolder)
                    ? OutputFolder
                    : Path.Combine(Path.GetTempPath(), "JudasScreenshots");
                
                Directory.CreateDirectory(outputDir);
                Log($"Screenshot output folder: {outputDir}");

                // Generate unique filename prefix
                var filePrefix = Guid.NewGuid().ToString("N").Substring(0, 12);

                for (int i = 0; i < timestamps.Count; i++)
                {
                    var timestamp = TimeSpan.FromSeconds(timestamps[i]);
                    var outputPath = Path.Combine(outputDir, $"{filePrefix}_ss{i + 1}.png");

                    Log($"Taking screenshot {i + 1}/{count} at {timestamp:mm\\:ss}...");
                    var success = await TakeScreenshotAtAsync(videoPath, timestamp, outputPath);
                    
                    if (success && File.Exists(outputPath))
                    {
                        var fileSize = new FileInfo(outputPath).Length / 1024;
                        Log($"✅ Screenshot saved: {Path.GetFileName(outputPath)} ({fileSize} KB)");
                        screenshots.Add(outputPath);
                    }
                    else
                    {
                        Log($"❌ Failed to take screenshot {i + 1}");
                    }
                }

                Log($"📸 Completed: {screenshots.Count}/{count} screenshots taken");
            }
            catch (Exception ex)
            {
                Log($"❌ Screenshot error: {ex.Message}");
            }

            return screenshots;
        }

        private async Task<TimeSpan> GetVideoDurationAsync(string videoPath)
        {
            try
            {
                // Try ffprobe first (more reliable for duration)
                var ffprobePath = _ffmpegPath.Replace("ffmpeg", "ffprobe");
                var useProbe = File.Exists(ffprobePath);
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = useProbe ? ffprobePath : _ffmpegPath,
                    Arguments = useProbe 
                        ? $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{videoPath}\""
                        : $"-i \"{videoPath}\" -hide_banner",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) return TimeSpan.Zero;

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                
                // Wait with timeout
                if (!process.WaitForExit(30000))
                {
                    process.Kill();
                    Log("⚠️ Duration check timed out");
                    return TimeSpan.Zero;
                }

                var output = await outputTask;
                var error = await errorTask;

                if (useProbe && double.TryParse(output.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double seconds))
                {
                    return TimeSpan.FromSeconds(seconds);
                }

                // Parse duration from ffmpeg output
                var match = Regex.Match(error, @"Duration: (\d{2}):(\d{2}):(\d{2})\.(\d{2})");
                if (match.Success)
                {
                    return new TimeSpan(
                        0,
                        int.Parse(match.Groups[1].Value),
                        int.Parse(match.Groups[2].Value),
                        int.Parse(match.Groups[3].Value),
                        int.Parse(match.Groups[4].Value) * 10);
                }
            }
            catch (Exception ex)
            {
                Log($"⚠️ Error getting duration: {ex.Message}");
            }

            return TimeSpan.Zero;
        }

        private async Task<bool> TakeScreenshotAtAsync(string videoPath, TimeSpan timestamp, string outputPath)
        {
            if (_isCancelled) return false;
            
            try
            {
                var timeString = timestamp.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = _ffmpegPath,
                    Arguments = $"-y -ss {timeString} -i \"{videoPath}\" -vframes 1 -q:v 2 \"{outputPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                _currentProcess = Process.Start(startInfo);
                if (_currentProcess == null) 
                {
                    Log("❌ Failed to start ffmpeg process");
                    return false;
                }

                // IMPORTANT: Read stderr BEFORE WaitForExit to avoid deadlock
                var stderrTask = _currentProcess.StandardError.ReadToEndAsync();
                var stdoutTask = _currentProcess.StandardOutput.ReadToEndAsync();
                
                // Wait with timeout (30 seconds per screenshot)
                var completed = _currentProcess.WaitForExit(30000);
                
                if (_isCancelled)
                {
                    try { _currentProcess.Kill(); } catch { }
                    return false;
                }
                
                if (!completed)
                {
                    try { _currentProcess.Kill(); } catch { }
                    Log("❌ Screenshot timed out after 30s");
                    return false;
                }
                
                var stderr = await stderrTask;
                
                if (_currentProcess.ExitCode != 0)
                {
                    var errorLines = stderr.Split('\n').TakeLast(3);
                    Log($"❌ ffmpeg error (exit {_currentProcess.ExitCode}): {string.Join(" | ", errorLines)}");
                    return false;
                }

                return File.Exists(outputPath);
            }
            catch (Exception ex)
            {
                Log($"❌ Screenshot exception: {ex.Message}");
                return false;
            }
        }

        public async Task<List<string>> UploadScreenshotsAsync(List<string> screenshotPaths)
        {
            var urls = new List<string>();

            if (string.IsNullOrEmpty(_imgbbApiKey))
            {
                Log("⚠️ ImgBB API key not set, skipping uploads");
                return urls;
            }

            foreach (var path in screenshotPaths)
            {
                try
                {
                    Log($"📤 Uploading: {Path.GetFileName(path)}...");
                    var url = await UploadToImgbbAsync(path);
                    if (!string.IsNullOrEmpty(url))
                    {
                        Log($"✅ Uploaded: {url}");
                        urls.Add(url);
                    }
                    else
                    {
                        Log($"❌ Failed to upload: {Path.GetFileName(path)}");
                    }
                }
                catch (Exception ex)
                {
                    Log($"❌ Upload error: {ex.Message}");
                }
            }

            Log($"📤 Uploaded {urls.Count}/{screenshotPaths.Count} screenshots");
            return urls;
        }

        private async Task<string?> UploadToImgbbAsync(string imagePath)
        {
            if (string.IsNullOrEmpty(_imgbbApiKey)) return null;

            try
            {
                var imageBytes = await File.ReadAllBytesAsync(imagePath);
                var base64 = Convert.ToBase64String(imageBytes);

                var content = new MultipartFormDataContent();
                content.Add(new StringContent(_imgbbApiKey), "key");
                content.Add(new StringContent(base64), "image");

                var response = await _httpClient.PostAsync("https://api.imgbb.com/1/upload", content);
                var json = await response.Content.ReadAsStringAsync();
                var result = JObject.Parse(json);

                if (result["success"]?.Value<bool>() == true)
                {
                    return result["data"]?["url"]?.Value<string>();
                }
                else
                {
                    Log($"ImgBB error: {result["error"]?["message"]}");
                }
            }
            catch (Exception ex)
            {
                Log($"ImgBB exception: {ex.Message}");
            }

            return null;
        }

        public void CleanupScreenshots(List<string> screenshotPaths)
        {
            foreach (var path in screenshotPaths)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch (Exception ex)
                {
                    // Log but don't fail - cleanup errors are non-fatal
                    System.Diagnostics.Debug.WriteLine($"Failed to delete screenshot {path}: {ex.Message}");
                }
            }
        }
    }
}
