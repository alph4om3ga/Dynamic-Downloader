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
using Newtonsoft.Json.Linq;

namespace JudasEncodingManager.Services
{
    public class MuxingService
    {
        private string _mkvmergePath = "mkvmerge";
        private string _ffprobePath = "ffprobe";
        private Process? _currentProcess;
        private bool _isCancelled;

        public event EventHandler<string>? LogMessage;
        public event EventHandler<double>? ProgressChanged;

        public void Configure(string mkvmergePath, string ffmpegPath)
        {
            if (!string.IsNullOrEmpty(mkvmergePath))
            {
                _mkvmergePath = mkvmergePath.Replace("/", "\\");
            }
            
            // Derive ffprobe path from ffmpeg path
            if (!string.IsNullOrEmpty(ffmpegPath))
            {
                _ffprobePath = ffmpegPath.Replace("ffmpeg", "ffprobe").Replace("/", "\\");
            }
            
            Log($"MuxingService configured: mkvmerge={_mkvmergePath}");
        }

        public void Cancel()
        {
            _isCancelled = true;
            try { _currentProcess?.Kill(); } catch { }
        }

        private void Log(string message)
        {
            LogMessage?.Invoke(this, message);
        }

        /// <summary>
        /// Analyzes the source file to extract track information
        /// </summary>
        public async Task<(List<AudioTrackInfo> Audio, List<SubtitleTrackInfo> Subtitles)> AnalyzeTracksAsync(string filePath)
        {
            var audioTracks = new List<AudioTrackInfo>();
            var subtitleTracks = new List<SubtitleTrackInfo>();

            if (!File.Exists(filePath))
            {
                Log($"❌ File not found for analysis: {filePath}");
                return (audioTracks, subtitleTracks);
            }

            Log($"🔍 Analyzing tracks in: {Path.GetFileName(filePath)}");

            // Try mkvmerge first (better for MKV files)
            var mkvmergeResult = await AnalyzeWithMkvmergeAsync(filePath);
            if (mkvmergeResult.Audio.Count > 0 || mkvmergeResult.Subtitles.Count > 0)
            {
                return mkvmergeResult;
            }

            // Fallback to ffprobe
            return await AnalyzeWithFfprobeAsync(filePath);
        }

        private async Task<(List<AudioTrackInfo> Audio, List<SubtitleTrackInfo> Subtitles)> AnalyzeWithMkvmergeAsync(string filePath)
        {
            var audioTracks = new List<AudioTrackInfo>();
            var subtitleTracks = new List<SubtitleTrackInfo>();

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = _mkvmergePath,
                    Arguments = $"-J \"{filePath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) return (audioTracks, subtitleTracks);

                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0 || string.IsNullOrEmpty(output))
                {
                    Log("⚠️ mkvmerge JSON output failed, trying ffprobe");
                    return (audioTracks, subtitleTracks);
                }

                var json = JObject.Parse(output);
                var tracks = json["tracks"] as JArray;
                
                if (tracks == null) return (audioTracks, subtitleTracks);

                foreach (var track in tracks)
                {
                    var type = track["type"]?.ToString();
                    var id = track["id"]?.Value<int>() ?? 0;
                    var properties = track["properties"];
                    var language = properties?["language"]?.ToString() ?? "und";
                    var trackName = properties?["track_name"]?.ToString() ?? "";
                    var isDefault = properties?["default_track"]?.Value<bool>() ?? false;
                    var codec = track["codec"]?.ToString() ?? "";

                    if (type == "audio")
                    {
                        var channels = properties?["audio_channels"]?.Value<int>() ?? 2;
                        audioTracks.Add(new AudioTrackInfo
                        {
                            Index = id,
                            Language = language,
                            LanguageDisplay = GetLanguageDisplayName(language),
                            Codec = codec,
                            Channels = channels,
                            Title = trackName,
                            IsDefault = isDefault
                        });
                        Log($"  🔊 Audio #{id}: {GetLanguageDisplayName(language)} ({codec}, {channels}ch)");
                    }
                    else if (type == "subtitles")
                    {
                        var isForced = properties?["forced_track"]?.Value<bool>() ?? false;
                        var isSDH = trackName.ToLowerInvariant().Contains("sdh") || 
                                   trackName.ToLowerInvariant().Contains("cc");
                        
                        subtitleTracks.Add(new SubtitleTrackInfo
                        {
                            Index = id,
                            Language = language,
                            LanguageDisplay = GetLanguageDisplayName(language),
                            Codec = codec,
                            Title = trackName,
                            IsDefault = isDefault,
                            IsForced = isForced,
                            IsSDH = isSDH
                        });
                        Log($"  📝 Subtitle #{id}: {GetLanguageDisplayName(language)} - \"{trackName}\"");
                    }
                }

                Log($"✅ Found {audioTracks.Count} audio, {subtitleTracks.Count} subtitle tracks");
            }
            catch (Exception ex)
            {
                Log($"⚠️ mkvmerge analysis error: {ex.Message}");
            }

            return (audioTracks, subtitleTracks);
        }

        private async Task<(List<AudioTrackInfo> Audio, List<SubtitleTrackInfo> Subtitles)> AnalyzeWithFfprobeAsync(string filePath)
        {
            var audioTracks = new List<AudioTrackInfo>();
            var subtitleTracks = new List<SubtitleTrackInfo>();

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = _ffprobePath,
                    Arguments = $"-v quiet -print_format json -show_streams \"{filePath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) return (audioTracks, subtitleTracks);

                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (string.IsNullOrEmpty(output)) return (audioTracks, subtitleTracks);

                var json = JObject.Parse(output);
                var streams = json["streams"] as JArray;
                
                if (streams == null) return (audioTracks, subtitleTracks);

                int audioIndex = 0, subIndex = 0;
                foreach (var stream in streams)
                {
                    var codecType = stream["codec_type"]?.ToString();
                    var tags = stream["tags"];
                    var language = tags?["language"]?.ToString() ?? "und";
                    var title = tags?["title"]?.ToString() ?? "";
                    var codec = stream["codec_name"]?.ToString() ?? "";
                    var disposition = stream["disposition"];
                    var isDefault = disposition?["default"]?.Value<int>() == 1;

                    if (codecType == "audio")
                    {
                        var channels = stream["channels"]?.Value<int>() ?? 2;
                        audioTracks.Add(new AudioTrackInfo
                        {
                            Index = audioIndex++,
                            Language = language,
                            LanguageDisplay = GetLanguageDisplayName(language),
                            Codec = codec,
                            Channels = channels,
                            Title = title,
                            IsDefault = isDefault
                        });
                        Log($"  🔊 Audio #{audioIndex - 1}: {GetLanguageDisplayName(language)} ({codec}, {channels}ch)");
                    }
                    else if (codecType == "subtitle")
                    {
                        var isForced = disposition?["forced"]?.Value<int>() == 1;
                        var isSDH = title.ToLowerInvariant().Contains("sdh") || 
                                   title.ToLowerInvariant().Contains("cc");
                        
                        subtitleTracks.Add(new SubtitleTrackInfo
                        {
                            Index = subIndex++,
                            Language = language,
                            LanguageDisplay = GetLanguageDisplayName(language),
                            Codec = codec,
                            Title = title,
                            IsDefault = isDefault,
                            IsForced = isForced,
                            IsSDH = isSDH
                        });
                        Log($"  📝 Subtitle #{subIndex - 1}: {GetLanguageDisplayName(language)} - \"{title}\"");
                    }
                }

                Log($"✅ Found {audioTracks.Count} audio, {subtitleTracks.Count} subtitle tracks (via ffprobe)");
            }
            catch (Exception ex)
            {
                Log($"⚠️ ffprobe analysis error: {ex.Message}");
            }

            return (audioTracks, subtitleTracks);
        }

        /// <summary>
        /// Remuxes the encoded file with proper track ordering and naming
        /// </summary>
        public async Task<MuxingResult> RemuxWithProperTracksAsync(
            string videoFile,
            string audioSubsFile,
            string outputPath,
            QueueItem item,
            CancellationToken cancellationToken)
        {
            var result = new MuxingResult { OutputPath = outputPath };
            _isCancelled = false;

            try
            {
                Log($"🔧 Starting mux: {Path.GetFileName(outputPath)}");

                // Build mkvmerge command with proper track ordering and naming
                var args = BuildMkvmergeArguments(videoFile, audioSubsFile, outputPath, item);
                
                Log($"📋 mkvmerge arguments: {args}");

                var startInfo = new ProcessStartInfo
                {
                    FileName = _mkvmergePath,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                _currentProcess = Process.Start(startInfo);
                if (_currentProcess == null)
                {
                    result.Success = false;
                    result.ErrorMessage = "Failed to start mkvmerge";
                    return result;
                }

                // Parse progress from mkvmerge output
                _currentProcess.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        var match = Regex.Match(e.Data, @"Progress: (\d+)%");
                        if (match.Success && int.TryParse(match.Groups[1].Value, out int progress))
                        {
                            ProgressChanged?.Invoke(this, progress / 100.0);
                        }
                    }
                };

                _currentProcess.BeginOutputReadLine();
                var stderr = await _currentProcess.StandardError.ReadToEndAsync();

                while (!_currentProcess.HasExited)
                {
                    if (cancellationToken.IsCancellationRequested || _isCancelled)
                    {
                        try { _currentProcess.Kill(); } catch { }
                        result.Success = false;
                        result.ErrorMessage = "Muxing cancelled";
                        return result;
                    }
                    await Task.Delay(100);
                }

                await _currentProcess.WaitForExitAsync();

                if (_currentProcess.ExitCode == 0 && File.Exists(outputPath))
                {
                    result.Success = true;
                    result.FileSize = new FileInfo(outputPath).Length;
                    Log($"✅ Muxing complete: {FileUtilities.FormatFileSize(result.FileSize)}");
                }
                else if (_currentProcess.ExitCode == 1)
                {
                    // Exit code 1 = warnings but success
                    if (File.Exists(outputPath))
                    {
                        result.Success = true;
                        result.FileSize = new FileInfo(outputPath).Length;
                        Log($"⚠️ Muxing complete with warnings: {FileUtilities.FormatFileSize(result.FileSize)}");
                    }
                    else
                    {
                        result.Success = false;
                        result.ErrorMessage = $"Muxing failed: {stderr}";
                    }
                }
                else
                {
                    result.Success = false;
                    result.ErrorMessage = $"mkvmerge exited with code {_currentProcess.ExitCode}: {stderr}";
                    Log($"❌ Muxing failed: {result.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                Log($"❌ Muxing error: {ex.Message}");
            }
            finally
            {
                _currentProcess?.Dispose();
                _currentProcess = null;
            }

            return result;
        }

        private string BuildMkvmergeArguments(string videoFile, string audioSubsFile, string outputPath, QueueItem item)
        {
            var args = new List<string>
            {
                "--output", $"\"{outputPath}\""
            };

            // === VIDEO TRACK (from encoded video file) ===
            // Set video track name and language
            args.Add("--track-name");
            args.Add("\"0:[Judas] x265 10b\"");
            args.Add("--language");
            args.Add("\"0:jpn\"");
            args.Add($"\"{videoFile}\"");

            // === AUDIO TRACKS ===
            // Determine audio encoding info for track names
            var audioCodec = item.IsTestRun ? "Opus" : "Opus";
            var audioBitrate = "112Kbps";
            
            // Order: Japanese first, then English, then others
            var orderedAudio = item.AudioTracks
                .OrderBy(a => a.Language.ToLowerInvariant() switch
                {
                    "jpn" or "ja" or "japanese" => 0,
                    "eng" or "en" or "english" => 1,
                    _ => 2
                })
                .ThenBy(a => a.Index)
                .ToList();

            if (orderedAudio.Count > 0)
            {
                // Build audio track order
                var audioOrder = string.Join(",", orderedAudio.Select((a, i) => $"0:{a.Index}"));
                args.Add("--track-order");
                
                // Set track names for each audio track
                for (int i = 0; i < orderedAudio.Count; i++)
                {
                    var track = orderedAudio[i];
                    var trackName = track.GetJudasTrackName(audioCodec, audioBitrate);
                    args.Add("--track-name");
                    args.Add($"\"{track.Index}:{trackName}\"");
                    args.Add("--language");
                    args.Add($"\"{track.Index}:{track.Language}\"");

                    // Default only when it is the sole audio track; otherwise let the user choose
                    var isDefault = orderedAudio.Count == 1 && i == 0;
                    args.Add("--default-track");
                    args.Add($"\"{track.Index}:{(isDefault ? "yes" : "no")}\"");
                }
            }

            // === SUBTITLE TRACKS ===
            // Order subtitles by priority (English first, then standard order)
            var orderedSubs = item.SubtitleTracks
                .OrderBy(s => s.SortPriority)
                .ThenBy(s => s.Index)
                .ToList();

            if (orderedSubs.Count > 0)
            {
                for (int i = 0; i < orderedSubs.Count; i++)
                {
                    var track = orderedSubs[i];
                    var displayName = track.GetDisplayName();

                    args.Add("--track-name");
                    args.Add($"\"{track.Index}:{displayName}\"");
                    args.Add("--language");
                    args.Add($"\"{track.Index}:{track.NormalizedLanguage}\"");

                    // Default only when it is the sole subtitle track; otherwise let the user choose
                    var isDefault = orderedSubs.Count == 1 && i == 0;
                    args.Add("--default-track");
                    args.Add($"\"{track.Index}:{(isDefault ? "yes" : "no")}\"");
                }
            }

            // Add the audio/subs file
            args.Add($"\"{audioSubsFile}\"");

            return string.Join(" ", args);
        }

        /// <summary>
        /// Remuxes an existing file with proper Judas track naming conventions
        /// Used for full encodes where PowerShell already muxed but without our track names
        /// </summary>
        public async Task<MuxingResult> RemuxWithTrackNamesAsync(
            string inputFile,
            string outputPath,
            QueueItem item,
            CancellationToken cancellationToken)
        {
            var result = new MuxingResult { OutputPath = outputPath };
            _isCancelled = false;

            // Check if input and output are the same file - use temp file if so
            var normalizedInput = Path.GetFullPath(inputFile).ToLowerInvariant();
            var normalizedOutput = Path.GetFullPath(outputPath).ToLowerInvariant();
            var useTempFile = normalizedInput == normalizedOutput;
            var actualOutputPath = useTempFile 
                ? Path.Combine(Path.GetDirectoryName(outputPath)!, Path.GetFileNameWithoutExtension(outputPath) + "_remux_temp.mkv")
                : outputPath;

            if (useTempFile)
            {
                Log($"Input and output are same file, using temp: {Path.GetFileName(actualOutputPath)}");
            }

            try
            {
                Log($"🔧 Remuxing with track names: {Path.GetFileName(outputPath)}");

                // First, get track info directly from mkvmerge to ensure correct track IDs
                var trackInfo = await GetMkvmergeTrackInfoAsync(inputFile);
                Log($"Track info from mkvmerge: Video={trackInfo.VideoTrackId}, Audio=[{string.Join(",", trackInfo.AudioTrackIds)}], Subs=[{string.Join(",", trackInfo.SubtitleTrackIds)}]");

                // Build mkvmerge command for single-file remux with track naming
                var args = BuildSingleFileMkvmergeArgumentsV2(inputFile, actualOutputPath, item, trackInfo);
                
                Log($"📋 mkvmerge arguments: {args}");

                var startInfo = new ProcessStartInfo
                {
                    FileName = _mkvmergePath,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                _currentProcess = Process.Start(startInfo);
                if (_currentProcess == null)
                {
                    result.Success = false;
                    result.ErrorMessage = "Failed to start mkvmerge";
                    return result;
                }

                // Capture all output for debugging
                var outputBuilder = new System.Text.StringBuilder();
                _currentProcess.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        outputBuilder.AppendLine(e.Data);
                        var match = Regex.Match(e.Data, @"Progress: (\d+)%");
                        if (match.Success && int.TryParse(match.Groups[1].Value, out int progress))
                        {
                            ProgressChanged?.Invoke(this, progress / 100.0);
                        }
                    }
                };

                _currentProcess.BeginOutputReadLine();
                var stderr = await _currentProcess.StandardError.ReadToEndAsync();

                while (!_currentProcess.HasExited)
                {
                    if (cancellationToken.IsCancellationRequested || _isCancelled)
                    {
                        try { _currentProcess.Kill(); } catch { }
                        result.Success = false;
                        result.ErrorMessage = "Muxing cancelled";
                        return result;
                    }
                    await Task.Delay(100);
                }

                await _currentProcess.WaitForExitAsync();

                Log($"mkvmerge stdout: {outputBuilder}");
                if (!string.IsNullOrEmpty(stderr))
                    Log($"mkvmerge stderr: {stderr}");

                if (_currentProcess.ExitCode == 0 && File.Exists(actualOutputPath))
                {
                    // If we used a temp file, swap it with the original
                    if (useTempFile)
                    {
                        Log($"Swapping temp file with original...");
                        File.Delete(inputFile);
                        File.Move(actualOutputPath, outputPath);
                    }
                    
                    result.Success = true;
                    result.FileSize = new FileInfo(outputPath).Length;
                    Log($"✅ Remux complete: {FileUtilities.FormatFileSize(result.FileSize)}");
                }
                else if (_currentProcess.ExitCode == 1)
                {
                    // Exit code 1 = warnings but success
                    if (File.Exists(actualOutputPath))
                    {
                        // If we used a temp file, swap it with the original
                        if (useTempFile)
                        {
                            Log($"Swapping temp file with original...");
                            File.Delete(inputFile);
                            File.Move(actualOutputPath, outputPath);
                        }
                        
                        result.Success = true;
                        result.FileSize = new FileInfo(outputPath).Length;
                        Log($"⚠️ Remux complete with warnings: {FileUtilities.FormatFileSize(result.FileSize)}");
                    }
                    else
                    {
                        result.Success = false;
                        result.ErrorMessage = $"Remux failed: {stderr}";
                    }
                }
                else
                {
                    result.Success = false;
                    result.ErrorMessage = $"mkvmerge exited with code {_currentProcess.ExitCode}: {stderr}";
                    Log($"❌ Remux failed: {result.ErrorMessage}");
                    
                    // Clean up temp file if it exists
                    if (useTempFile && File.Exists(actualOutputPath))
                    {
                        try { File.Delete(actualOutputPath); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                Log($"❌ Remux error: {ex.Message}");
            }
            finally
            {
                _currentProcess?.Dispose();
                _currentProcess = null;
            }

            return result;
        }

        private class MkvmergeTrackInfo
        {
            public int VideoTrackId { get; set; } = -1;
            public List<int> AudioTrackIds { get; set; } = new();
            public List<int> SubtitleTrackIds { get; set; } = new();
        }

        private async Task<MkvmergeTrackInfo> GetMkvmergeTrackInfoAsync(string filePath)
        {
            var info = new MkvmergeTrackInfo();
            
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = _mkvmergePath,
                    Arguments = $"--identify \"{filePath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) return info;

                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                // Parse output like: "Track ID 0: video (HEVC/H.265/MPEG-H)"
                // "Track ID 1: audio (Opus)"
                // "Track ID 2: subtitles (SubRip/SRT)"
                foreach (var line in output.Split('\n'))
                {
                    var match = Regex.Match(line, @"Track ID (\d+): (\w+)");
                    if (match.Success)
                    {
                        var trackId = int.Parse(match.Groups[1].Value);
                        var trackType = match.Groups[2].Value.ToLower();
                        
                        if (trackType == "video" && info.VideoTrackId == -1)
                            info.VideoTrackId = trackId;
                        else if (trackType == "audio")
                            info.AudioTrackIds.Add(trackId);
                        else if (trackType == "subtitles")
                            info.SubtitleTrackIds.Add(trackId);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error getting track info: {ex.Message}");
            }

            return info;
        }

        private string BuildSingleFileMkvmergeArgumentsV2(string inputFile, string outputPath, QueueItem item, MkvmergeTrackInfo trackInfo)
        {
            var args = new List<string>
            {
                "--output", $"\"{outputPath}\""
            };

            // === VIDEO TRACK ===
            if (trackInfo.VideoTrackId >= 0)
            {
                args.Add("--track-name");
                args.Add($"\"{trackInfo.VideoTrackId}:[Judas] x265 10b\"");
                args.Add("--language");
                args.Add($"\"{trackInfo.VideoTrackId}:jpn\"");
            }

            // === AUDIO TRACKS ===
            // Just use language name (Japanese, English) for audio track names
            var audioCount = Math.Min(trackInfo.AudioTrackIds.Count, item.AudioTracks.Count);
            for (int i = 0; i < audioCount; i++)
            {
                var mkvTrackId = trackInfo.AudioTrackIds[i];
                var trackData = item.AudioTracks[i];
                var trackName = trackData.LanguageDisplay;

                args.Add("--track-name");
                args.Add($"\"{mkvTrackId}:{trackName}\"");
                args.Add("--language");
                args.Add($"\"{mkvTrackId}:{trackData.Language}\"");

                // Default only when it is the sole audio track; otherwise let the user choose
                var isDefault = audioCount == 1 && i == 0;
                args.Add("--default-track");
                args.Add($"\"{mkvTrackId}:{(isDefault ? "yes" : "no")}\"");
            }

            // === SUBTITLE TRACKS ===
            // Order subtitles by priority (English first, then by priority)
            var orderedSubs = item.SubtitleTracks
                .Select((track, originalIndex) => new { Track = track, OriginalIndex = originalIndex, MkvId = trackInfo.SubtitleTrackIds.ElementAtOrDefault(originalIndex) })
                .Where(x => x.MkvId >= 0)
                .OrderBy(x => x.Track.SortPriority)
                .ThenBy(x => x.OriginalIndex)
                .ToList();

            // Build track order for subtitles
            if (orderedSubs.Count > 0)
            {
                var trackOrderParts = new List<string>();
                
                // Video track first
                if (trackInfo.VideoTrackId >= 0)
                    trackOrderParts.Add($"0:{trackInfo.VideoTrackId}");
                
                // Audio tracks in original order
                foreach (var audioId in trackInfo.AudioTrackIds)
                    trackOrderParts.Add($"0:{audioId}");
                
                // Subtitle tracks in sorted order
                foreach (var sub in orderedSubs)
                    trackOrderParts.Add($"0:{sub.MkvId}");
                
                args.Add("--track-order");
                args.Add(string.Join(",", trackOrderParts));
            }

            // Set subtitle track names and properties
            for (int i = 0; i < orderedSubs.Count; i++)
            {
                var sub = orderedSubs[i];
                var displayName = sub.Track.GetDisplayName();

                args.Add("--track-name");
                args.Add($"\"{sub.MkvId}:{displayName}\"");
                args.Add("--language");
                args.Add($"\"{sub.MkvId}:{sub.Track.NormalizedLanguage}\"");

                // Default only when it is the sole subtitle track; otherwise let the user choose
                var isDefault = orderedSubs.Count == 1 && i == 0;
                args.Add("--default-track");
                args.Add($"\"{sub.MkvId}:{(isDefault ? "yes" : "no")}\"");
            }

            // Add the input file
            args.Add($"\"{inputFile}\"");

            return string.Join(" ", args);
        }

        /// <summary>
        /// Simple mux for test runs - just combines video with audio/subs without reordering
        /// </summary>
        public async Task<MuxingResult> SimpleMuxAsync(
            string videoFile,
            string audioSubsFile,
            string outputPath,
            CancellationToken cancellationToken)
        {
            var result = new MuxingResult { OutputPath = outputPath };
            
            try
            {
                Log($"🔧 Simple mux: {Path.GetFileName(outputPath)}");

                var args = $"--output \"{outputPath}\" " +
                          $"--track-name \"0:[Judas] x265 10b\" --language \"0:jpn\" \"{videoFile}\" " +
                          $"\"{audioSubsFile}\"";

                var startInfo = new ProcessStartInfo
                {
                    FileName = _mkvmergePath,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    result.Success = false;
                    result.ErrorMessage = "Failed to start mkvmerge";
                    return result;
                }

                await process.WaitForExitAsync();

                if ((process.ExitCode == 0 || process.ExitCode == 1) && File.Exists(outputPath))
                {
                    result.Success = true;
                    result.FileSize = new FileInfo(outputPath).Length;
                    Log($"✅ Simple mux complete: {FileUtilities.FormatFileSize(result.FileSize)}");
                }
                else
                {
                    var stderr = await process.StandardError.ReadToEndAsync();
                    result.Success = false;
                    result.ErrorMessage = $"mkvmerge failed: {stderr}";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private static string GetLanguageDisplayName(string langCode)
        {
            return langCode.ToLowerInvariant() switch
            {
                "jpn" or "ja" or "japanese" => "Japanese",
                "eng" or "en" or "english" => "English",
                "fre" or "fr" or "fra" or "french" => "French",
                "ger" or "de" or "deu" or "german" => "German",
                "ita" or "it" or "italian" => "Italian",
                "spa" or "es" or "spanish" => "Spanish",
                "por" or "pt" or "portuguese" => "Portuguese",
                "rus" or "ru" or "russian" => "Russian",
                "ara" or "ar" or "arabic" => "Arabic",
                "chi" or "zh" or "zho" or "chinese" => "Chinese",
                "kor" or "ko" or "korean" => "Korean",
                "pol" or "pl" or "polish" => "Polish",
                "dut" or "nl" or "nld" or "dutch" => "Dutch",
                "tur" or "tr" or "turkish" => "Turkish",
                "vie" or "vi" or "vietnamese" => "Vietnamese",
                "tha" or "th" or "thai" => "Thai",
                "ind" or "id" or "indonesian" => "Indonesian",
                "may" or "ms" or "msa" or "malay" => "Malay",
                "und" or "undetermined" => "Unknown",
                _ => langCode.ToUpperInvariant()
            };
        }

    }

    public class MuxingResult
    {
        public bool Success { get; set; }
        public string OutputPath { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
        public long FileSize { get; set; }
        public string FileSizeFormatted => FileUtilities.FormatFileSize(FileSize);
    }
}
