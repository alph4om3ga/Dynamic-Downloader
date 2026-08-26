using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using JudasEncodingManager.Models;
using JudasEncodingManager.ViewModels;

namespace JudasEncodingManager.Services
{
    public class QueueManager
    {
        private readonly RssService _rssService;
        private readonly QBittorrentService _qbitService;
        private readonly EncodingService _encodingService;
        private readonly MuxingService _muxingService;
        private readonly ScreenshotService _screenshotService;
        private readonly FtpService _ftpService;
        private readonly TorrentService _torrentService;
        private readonly NyaaService _nyaaService;
        private readonly DiscordService _discordService;
        private readonly Func<QueueItem, CancellationToken, Task>? _processBeforeCleanupAsync;
        private readonly Action? _cleanupStarted;

        private CancellationTokenSource? _monitoringCts;
        private CancellationTokenSource? _processingCts;
        private bool _isMonitoring;
        private bool _isPaused;
        private bool _isProcessing;
        private readonly object _processingLock = new object();

        public event EventHandler<ActivityLogEntry>? LogReceived;
        public event EventHandler<QueueItem>? QueueItemUpdated;
        public event EventHandler<bool>? MonitoringStateChanged;
        public event EventHandler<bool>? ProcessingStateChanged;

        public ObservableCollection<QueueItem> Queue { get; } = new();
        public bool IsMonitoring => _isMonitoring;
        public bool IsPaused => _isPaused;
        public bool IsProcessing => _isProcessing;

        // Base path for all operations - C:\JudasEncodingManager
        public string BasePath { get; set; } = @"C:\JudasEncodingManager";
        
        // Folder structure
        public string EncodingFolder => Path.Combine(BasePath, "Encoding");
        public string ScreenshotsFolder => Path.Combine(BasePath, "Screenshots");
        public string SeedingFolder => Path.Combine(BasePath, "Seeding");
        
        // Configuration
        public string WorkerConfigPath { get; set; } = "";
        public string DescriptionTemplatePath { get; set; } = "";
        public bool IsTestMode { get; set; }
        public int RssCheckIntervalMinutes { get; set; } = 5;
        public string SeedboxReleasesPath { get; set; } = "Releases/";

        public QueueManager() : this(null, null)
        {
        }

        internal QueueManager(
            Func<QueueItem, CancellationToken, Task>? processBeforeCleanupAsync,
            Action? cleanupStarted)
        {
            _processBeforeCleanupAsync = processBeforeCleanupAsync;
            _cleanupStarted = cleanupStarted;
            _rssService = new RssService();
            _qbitService = new QBittorrentService();
            _encodingService = new EncodingService();
            _muxingService = new MuxingService();
            _screenshotService = new ScreenshotService();
            _ftpService = new FtpService();
            _torrentService = new TorrentService();
            _nyaaService = new NyaaService();
            _discordService = new DiscordService();

            // Wire up events - use dispatcher for thread-safe UI updates
            _encodingService.OutputReceived += (s, msg) => Log(msg, ActivityLogLevel.Debug);
            _encodingService.ProgressChanged += (s, progress) =>
            {
                Application.Current?.Dispatcher?.BeginInvoke(() =>
                {
                    var current = Queue.FirstOrDefault(q => q.Status == QueueItemStatus.Encoding);
                    if (current != null)
                    {
                        current.EncodeProgress = progress;
                    }
                });
            };

            _muxingService.LogMessage += (s, msg) => Log($"🔧 {msg}", ActivityLogLevel.Info);
            _muxingService.ProgressChanged += (s, progress) =>
            {
                Application.Current?.Dispatcher?.BeginInvoke(() =>
                {
                    var current = Queue.FirstOrDefault(q => q.Status == QueueItemStatus.Muxing);
                    if (current != null)
                    {
                        current.EncodeProgress = progress; // Reuse encode progress for muxing
                    }
                });
            };

            _screenshotService.LogMessage += (s, msg) => Log($"📸 {msg}", ActivityLogLevel.Info);

            _ftpService.StatusChanged += (s, msg) => Log(msg, ActivityLogLevel.Info);
            _ftpService.ProgressChanged += (s, progress) =>
            {
                Application.Current?.Dispatcher?.BeginInvoke(() =>
                {
                    var current = Queue.FirstOrDefault(q =>
                        q.Status == QueueItemStatus.UploadingTorrentFile ||
                        q.Status == QueueItemStatus.UploadingDescription ||
                        q.Status == QueueItemStatus.UploadingEpisode);
                    if (current != null)
                    {
                        current.UploadProgress = progress;
                    }
                });
            };
        }

        public void EnsureFoldersExist()
        {
            try
            {
                Directory.CreateDirectory(BasePath);
                Directory.CreateDirectory(EncodingFolder);
                Directory.CreateDirectory(ScreenshotsFolder);
                Directory.CreateDirectory(SeedingFolder);
                
                Log($"📁 Folders initialized:", ActivityLogLevel.Info);
                Log($"   • Encoding: {EncodingFolder}", ActivityLogLevel.Info);
                Log($"   • Screenshots: {ScreenshotsFolder}", ActivityLogLevel.Info);
                Log($"   • Seeding: {SeedingFolder}", ActivityLogLevel.Info);
            }
            catch (Exception ex)
            {
                Log($"❌ Failed to create folders: {ex.Message}", ActivityLogLevel.Error);
            }
        }

        public void Configure(AppSettings settings)
        {
            _qbitService.Configure(
                settings.QBittorrent.LocalIpPort,
                settings.QBittorrent.SeedboxIpPort,
                settings.QBittorrent.SeedboxUsername,
                settings.QBittorrent.SeedboxPassword);

            _ftpService.Configure(
                settings.Ftp.Host,
                settings.Ftp.Username,
                settings.Ftp.Password,
                settings.Ftp.ReleasesPath,
                settings.Ftp.TorrentsPath);

            _screenshotService.Configure(
                settings.Remuxer.FfmpegPath,
                settings.ImgbbApiKey);

            _muxingService.Configure(
                settings.Remuxer.MkvmergePath,
                settings.Remuxer.FfmpegPath);

            _nyaaService.Configure(
                settings.AutoPosting.NyaaCookieDdlg,
                settings.AutoPosting.NyaaCookieSession,
                settings.Discord.ServerInviteLink);

            _discordService.Configure(
                settings.Discord.WebhookUrl,
                settings.MachineName,
                settings.TestMode);

            IsTestMode = settings.TestMode;
            SeedboxReleasesPath = settings.QBittorrent.SeedboxReleasesPath;
            
            // Set description template path
            DescriptionTemplatePath = Path.Combine(EncodingFolder, "NyaaDescriptionTemplate.txt");
            
            // Ensure folders exist
            EnsureFoldersExist();
        }

        public async Task StartMonitoringAsync(ObservableCollection<ShowViewModel> shows)
        {
            if (_isMonitoring) return;

            _isMonitoring = true;
            _monitoringCts = new CancellationTokenSource();
            MonitoringStateChanged?.Invoke(this, true);
            Log("📡 RSS monitoring started", ActivityLogLevel.Success);

            try
            {
                while (!_monitoringCts.Token.IsCancellationRequested)
                {
                    foreach (var showVm in shows.Where(s => s.IsActive))
                    {
                        if (_monitoringCts.Token.IsCancellationRequested) break;

                        try
                        {
                            var newEpisode = await _rssService.FindNewEpisodeAsync(
                                showVm.Model, 
                                showVm.EpisodesReleased.ToList());

                            if (newEpisode != null)
                            {
                                var episodeNum = _rssService.ExtractEpisodeNumber(
                                    newEpisode.Title, 
                                    showVm.CustomEpisodeRegex);

                                if (episodeNum.HasValue)
                                {
                                    var adjustedEpNum = episodeNum.Value + showVm.NumberOfEpisodesToRemoveFromCount;
                                    var version = _rssService.ExtractVersion(newEpisode.Title);

                                    // Check if already in queue
                                    var existsInQueue = Queue.Any(q => 
                                        q.Show.OutputTorrentTitle == showVm.OutputTorrentTitle &&
                                        q.EpisodeNumber == adjustedEpNum &&
                                        q.Version == version);

                                    if (!existsInQueue)
                                    {
                                        var queueItem = new QueueItem
                                        {
                                            Show = showVm.Model,
                                            EpisodeNumber = adjustedEpNum,
                                            Version = version,
                                            SourceFileName = newEpisode.Title,
                                            TorrentHash = newEpisode.InfoHash,
                                            SourceGroup = showVm.SourceGroup,
                                            Status = QueueItemStatus.Pending
                                        };

                                        // Thread-safe add to queue
                                        Application.Current?.Dispatcher?.BeginInvoke(() => Queue.Add(queueItem));
                                        Log($"📥 New episode found: {showVm.OutputFileTitle} E{adjustedEpNum}", ActivityLogLevel.Success, queueItem);

                                        // Start download with error handling
                                        _ = Task.Run(async () =>
                                        {
                                            try
                                            {
                                                await StartDownloadAsync(queueItem, newEpisode.TorrentUrl);
                                            }
                                            catch (Exception ex)
                                            {
                                                Log($"❌ Download task error: {ex.Message}", ActivityLogLevel.Error, queueItem);
                                            }
                                        });
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"Error checking RSS for {showVm.OutputFileTitle}: {ex.Message}", ActivityLogLevel.Warning);
                        }
                    }

                    // Wait before next check
                    await Task.Delay(TimeSpan.FromMinutes(RssCheckIntervalMinutes), _monitoringCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping
            }
            finally
            {
                _isMonitoring = false;
                MonitoringStateChanged?.Invoke(this, false);
                Log("📡 RSS monitoring stopped", ActivityLogLevel.Info);
            }
        }

        public void StopMonitoring()
        {
            _monitoringCts?.Cancel();
        }

        public void PauseQueue()
        {
            _isPaused = true;
            Log("⏸️ Queue paused", ActivityLogLevel.Warning);
        }

        public void ResumeQueue()
        {
            _isPaused = false;
            Log("▶️ Queue resumed", ActivityLogLevel.Success);

            // Fire-and-forget with error handling
            _ = Task.Run(async () =>
            {
                try
                {
                    await _discordService.SendQueueResumedAsync();
                }
                catch (Exception ex)
                {
                    Log($"⚠️ Failed to send Discord notification: {ex.Message}", ActivityLogLevel.Warning);
                }
            });

            // Continue processing if there are items
            _ = Task.Run(async () =>
            {
                try
                {
                    await ProcessQueueAsync();
                }
                catch (Exception ex)
                {
                    Log($"❌ Queue processing error: {ex.Message}", ActivityLogLevel.Error);
                }
            });
        }

        private async Task StartDownloadAsync(QueueItem item, string torrentUrl)
        {
            try
            {
                item.Status = QueueItemStatus.Downloading;
                item.StatusMessage = "Starting download...";
                item.StartedAt = DateTime.Now;
                Log($"⬇️ Starting download: {item.SourceFileName}", ActivityLogLevel.Info, item);
                await _discordService.SendEpisodeGrabbedAsync(item);

                // Add to local qBittorrent - download to Encoding folder
                Log($"📁 Download destination: {EncodingFolder}", ActivityLogLevel.Debug, item);
                var success = await _qbitService.AddTorrentAsync(torrentUrl, EncodingFolder, isLocal: true);
                
                if (!success)
                {
                    Log($"⚠️ First download attempt failed, retrying in 5s...", ActivityLogLevel.Warning, item);
                    await Task.Delay(5000);
                    success = await _qbitService.AddTorrentAsync(torrentUrl, EncodingFolder, isLocal: true);
                }

                if (!success)
                {
                    item.Status = QueueItemStatus.DownloadFailed;
                    item.StatusMessage = "Failed to add torrent to qBittorrent";
                    item.LastError = "Failed to add torrent to qBittorrent";
                    item.DownloadRetries++;
                    Log($"❌ Failed to add torrent: {item.SourceFileName}", ActivityLogLevel.Error, item);
                    
                    if (item.DownloadRetries < 3)
                    {
                        item.Status = QueueItemStatus.Pending;
                        item.StatusMessage = $"Will retry ({item.DownloadRetries}/3)";
                    }
                    else
                    {
                        await _discordService.SendErrorAsync(item, "Download failed after 3 retries");
                        PauseQueue();
                    }
                    return;
                }

                Log($"✅ Torrent added to qBittorrent", ActivityLogLevel.Success, item);
                item.StatusMessage = "Downloading...";
                
                // Monitor download progress - await this to ensure proper flow
                await MonitorDownloadAsync(item);
            }
            catch (Exception ex)
            {
                item.Status = QueueItemStatus.DownloadFailed;
                item.StatusMessage = $"Error: {ex.Message}";
                item.LastError = ex.Message;
                Log($"❌ Download error: {ex.Message}", ActivityLogLevel.Error, item);
            }
        }

        private async Task MonitorDownloadAsync(QueueItem item)
        {
            try
            {
                int attempts = 0;
                const int maxAttempts = 720; // 2 hours max (10 sec intervals)
                
                Log($"🔍 Monitoring download for: {item.SourceFileName}", ActivityLogLevel.Debug, item);
                
                while (item.Status == QueueItemStatus.Downloading && attempts < maxAttempts)
                {
                    attempts++;
                    
                    // Try to find the torrent hash if we don't have it
                    if (string.IsNullOrEmpty(item.TorrentHash))
                    {
                        var torrents = await _qbitService.GetAllTorrentsAsync(isLocal: true);
                        
                        Log($"🔍 Looking for torrent... ({torrents.Count} torrents in qBittorrent)", ActivityLogLevel.Debug, item);
                        
                        // Try to find matching torrent
                        TorrentInfo? match = null;
                        
                        if (torrents.Count == 1)
                        {
                            match = torrents[0];
                            Log($"📍 Only one torrent found, using it: {match.Name}", ActivityLogLevel.Debug, item);
                        }
                        else if (torrents.Count > 1)
                        {
                            foreach (var t in torrents)
                            {
                                if (item.SourceFileName.Contains(t.Name, StringComparison.OrdinalIgnoreCase) ||
                                    t.Name.Contains(item.SourceFileName, StringComparison.OrdinalIgnoreCase) ||
                                    t.Name.Contains(item.Show.OutputFileTitle, StringComparison.OrdinalIgnoreCase))
                                {
                                    match = t;
                                    Log($"📍 Matched by name: {match.Name}", ActivityLogLevel.Debug, item);
                                    break;
                                }
                            }
                            
                            if (match == null)
                            {
                                var incomplete = torrents.FirstOrDefault(t => t.Progress < 1.0);
                                match = incomplete ?? torrents.Last();
                                Log($"📍 Using fallback torrent: {match.Name}", ActivityLogLevel.Debug, item);
                            }
                        }
                        
                        if (match != null)
                        {
                            item.TorrentHash = match.Hash;
                            Log($"✅ Found torrent: {match.Name} (Progress: {match.Progress:P0}, State: {match.State})", ActivityLogLevel.Info, item);
                        }
                    }

                    if (!string.IsNullOrEmpty(item.TorrentHash))
                    {
                        var info = await _qbitService.GetTorrentInfoAsync(item.TorrentHash, isLocal: true);
                        
                        if (info != null)
                        {
                            item.DownloadProgress = info.Progress;
                            item.StatusMessage = $"Downloading: {info.Progress:P0}";
                            
                            Log($"📊 Progress: {info.Progress:P0}, State: {info.State}", ActivityLogLevel.Debug, item);

                            var isComplete = info.Progress >= 0.999 || 
                                            info.State == "uploading" || 
                                            info.State == "stalledUP" ||
                                            info.State == "pausedUP" ||
                                            info.State == "queuedUP" ||
                                            info.State == "forcedUP" ||
                                            info.State == "seeding" ||
                                            info.State == "completed";
                            
                            if (isComplete)
                            {
                                Log($"✅ Torrent complete! State: {info.State}, Progress: {info.Progress:P0}", ActivityLogLevel.Success, item);
                                
                                // Construct file path
                                var savePath = !string.IsNullOrEmpty(info.SavePath) ? info.SavePath : EncodingFolder;
                                var contentPath = !string.IsNullOrEmpty(info.ContentPath) ? info.ContentPath : Path.Combine(savePath, info.Name);
                                
                                Log($"📁 Content path: {contentPath}", ActivityLogLevel.Debug, item);
                                
                                // If it's a folder, look for mkv/m2ts inside
                                if (Directory.Exists(contentPath))
                                {
                                    var videoFiles = Directory.GetFiles(contentPath, "*.mkv", SearchOption.AllDirectories)
                                        .Concat(Directory.GetFiles(contentPath, "*.m2ts", SearchOption.AllDirectories))
                                        .ToList();
                                    if (videoFiles.Count > 0)
                                    {
                                        item.SourceFilePath = videoFiles.OrderByDescending(f => new FileInfo(f).Length).First();
                                        Log($"📁 Found video in folder: {item.SourceFilePath}", ActivityLogLevel.Debug, item);
                                    }
                                    else
                                    {
                                        item.SourceFilePath = contentPath;
                                    }
                                }
                                else if (File.Exists(contentPath))
                                {
                                    item.SourceFilePath = contentPath;
                                }
                                else
                                {
                                    var searchPath = Path.Combine(savePath, info.Name);
                                    if (File.Exists(searchPath))
                                    {
                                        item.SourceFilePath = searchPath;
                                    }
                                    else
                                    {
                                        var videoFiles = Directory.GetFiles(EncodingFolder, "*.mkv", SearchOption.AllDirectories)
                                            .Concat(Directory.GetFiles(EncodingFolder, "*.m2ts", SearchOption.AllDirectories))
                                            .ToList();
                                        if (videoFiles.Count > 0)
                                        {
                                            item.SourceFilePath = videoFiles.OrderByDescending(f => new FileInfo(f).Length).First();
                                            Log($"📁 Found video by search: {item.SourceFilePath}", ActivityLogLevel.Debug, item);
                                        }
                                    }
                                }
                                
                                // Store source file size for description
                                if (File.Exists(item.SourceFilePath))
                                {
                                    item.SourceFileSizeBytes = new FileInfo(item.SourceFilePath).Length;
                                }
                                
                                Log($"📁 Final source path: {item.SourceFilePath}", ActivityLogLevel.Info, item);
                                
                                // Remove from qBittorrent but keep file
                                await _qbitService.DeleteTorrentAsync(item.TorrentHash, deleteFiles: false, isLocal: true);
                                Log($"🗑️ Removed torrent from qBittorrent (kept files)", ActivityLogLevel.Info, item);

                                // Update status
                                item.Status = QueueItemStatus.DownloadComplete;
                                item.StatusMessage = "Download complete!";
                                item.DownloadProgress = 1.0;
                                
                                Log($"✅ Download complete: {item.SourceFileName}", ActivityLogLevel.Success, item);
                                await _discordService.SendDownloadCompleteAsync(item);

                                // Start processing queue
                                Log($"🚀 Starting queue processing...", ActivityLogLevel.Info, item);
                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        await ProcessQueueAsync();
                                    }
                                    catch (Exception ex)
                                    {
                                        Log($"❌ Queue processing error: {ex.Message}", ActivityLogLevel.Error, item);
                                    }
                                });
                                return;
                            }
                        }
                        else
                        {
                            Log($"⚠️ Cannot get torrent info for hash: {item.TorrentHash}", ActivityLogLevel.Warning, item);
                            item.TorrentHash = "";
                        }
                    }

                    await Task.Delay(10000);
                }
                
                if (attempts >= maxAttempts)
                {
                    Log($"❌ Download timed out after 2 hours", ActivityLogLevel.Error, item);
                    item.Status = QueueItemStatus.DownloadFailed;
                    item.StatusMessage = "Download timed out";
                    item.LastError = "Download timed out";
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Error monitoring download: {ex.Message}", ActivityLogLevel.Error, item);
                item.Status = QueueItemStatus.DownloadFailed;
                item.StatusMessage = $"Monitor error: {ex.Message}";
                item.LastError = ex.Message;
            }
        }

        private async Task ProcessQueueAsync()
        {
            // Use lock to prevent race condition where multiple calls pass the _isProcessing check
            lock (_processingLock)
            {
                if (_isProcessing || _isPaused)
                {
                    Log($"📋 ProcessQueue: Already processing={_isProcessing}, Paused={_isPaused}", ActivityLogLevel.Debug);
                    return;
                }
                _isProcessing = true;
            }

            _processingCts?.Dispose();
            _processingCts = new CancellationTokenSource();
            ProcessingStateChanged?.Invoke(this, true);

            Log($"📋 Starting queue processing...", ActivityLogLevel.Info);

            try
            {
                while (!_processingCts.Token.IsCancellationRequested && !_isPaused)
                {
                    var nextItem = Queue.FirstOrDefault(q => q.Status == QueueItemStatus.DownloadComplete);
                    
                    if (nextItem == null) 
                    {
                        Log($"📋 No items ready for processing (DownloadComplete)", ActivityLogLevel.Debug);
                        break;
                    }

                    Log($"🎬 Processing: {nextItem.OutputFileName}", ActivityLogLevel.Info, nextItem);
                    await ProcessItemAsync(nextItem, _processingCts.Token);
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Queue processing error: {ex.Message}", ActivityLogLevel.Error);
            }
            finally
            {
                _isProcessing = false;
                ProcessingStateChanged?.Invoke(this, false);
                Log($"📋 Queue processing finished", ActivityLogLevel.Debug);
            }
        }

        internal async Task ProcessItemAsync(QueueItem item, CancellationToken cancellationToken)
        {
            try
            {
                // Verify source file exists
                if (!File.Exists(item.SourceFilePath))
                {
                    var possibleFiles = Directory.GetFiles(EncodingFolder, "*.mkv", SearchOption.AllDirectories)
                        .Concat(Directory.GetFiles(EncodingFolder, "*.m2ts", SearchOption.AllDirectories))
                        .ToList();
                    
                    if (possibleFiles.Count > 0)
                    {
                        item.SourceFilePath = possibleFiles.OrderByDescending(f => new FileInfo(f).Length).First();
                        Log($"📁 Found source file: {item.SourceFilePath}", ActivityLogLevel.Info, item);
                    }
                    else
                    {
                        Log($"❌ Source file not found: {item.SourceFilePath}", ActivityLogLevel.Error, item);
                        item.Status = QueueItemStatus.Error;
                        item.StatusMessage = "Source file not found";
                        item.LastError = "Source file not found";
                        PauseQueue();
                        return;
                    }
                }

                if (_processBeforeCleanupAsync != null)
                {
                    await _processBeforeCleanupAsync(item, cancellationToken);
                }
                else
                {
                // 0. Analyze source tracks
                item.Status = QueueItemStatus.AnalyzingTracks;
                item.StatusMessage = "Analyzing tracks...";
                Log($"🔍 Analyzing source tracks...", ActivityLogLevel.Info, item);

                var (audioTracks, subTracks) = await _muxingService.AnalyzeTracksAsync(item.SourceFilePath);
                item.AudioTracks = audioTracks;
                item.SubtitleTracks = subTracks;
                
                // Update source file size if not already set
                if (item.SourceFileSizeBytes == 0 && File.Exists(item.SourceFilePath))
                {
                    item.SourceFileSizeBytes = new FileInfo(item.SourceFilePath).Length;
                }
                
                Log($"📊 Track analysis complete: {audioTracks.Count} audio, {subTracks.Count} subtitle tracks", ActivityLogLevel.Success, item);

                // 1. Encoding
                item.Status = QueueItemStatus.Encoding;
                item.StatusMessage = "Encoding...";
                Log($"🎬 Starting encode: {item.Show.OutputFileTitle} E{item.EpisodeNumber}", ActivityLogLevel.Info, item);
                await _discordService.SendEncodingStartedAsync(item, item.IsTestRun);

                _encodingService.EncodingScriptsPath = EncodingFolder;
                _encodingService.OutputPath = SeedingFolder;

                var encodeResult = await _encodingService.EncodeAsync(item, WorkerConfigPath, cancellationToken);

                if (!encodeResult.Success)
                {
                    item.Status = QueueItemStatus.EncodingFailed;
                    item.StatusMessage = $"Encoding failed: {encodeResult.ErrorMessage}";
                    item.LastError = encodeResult.ErrorMessage;
                    Log($"❌ Encoding failed: {encodeResult.ErrorMessage}", ActivityLogLevel.Error, item);
                    await _discordService.SendErrorAsync(item, encodeResult.ErrorMessage);
                    PauseQueue();
                    return;
                }

                item.EncodedFilePath = encodeResult.OutputFilePath;
                item.Status = QueueItemStatus.EncodingComplete;
                item.StatusMessage = "Encoding complete";
                Log($"✅ Encoding complete: {encodeResult.FileSizeFormatted}", ActivityLogLevel.Success, item);
                await _discordService.SendEncodingCompleteAsync(item, encodeResult.Duration, encodeResult.FileSizeFormatted);

                // 2. Screenshots (save to Screenshots folder)
                item.Status = QueueItemStatus.TakingScreenshots;
                item.StatusMessage = "Taking screenshots...";
                Log("📸 Taking screenshots...", ActivityLogLevel.Info, item);

                _screenshotService.OutputFolder = ScreenshotsFolder;
                item.ScreenshotPaths = await _screenshotService.TakeScreenshotsAsync(item.EncodedFilePath, 3);
                Log($"📸 Saved {item.ScreenshotPaths.Count} screenshots to {ScreenshotsFolder}", ActivityLogLevel.Info, item);

                // 3. Upload screenshots
                item.Status = QueueItemStatus.UploadingScreenshots;
                item.StatusMessage = "Uploading screenshots...";
                Log("📤 Uploading screenshots to ImgBB...", ActivityLogLevel.Info, item);

                item.ScreenshotUrls = await _screenshotService.UploadScreenshotsAsync(item.ScreenshotPaths);
                Log($"✅ Uploaded {item.ScreenshotUrls.Count} screenshots", ActivityLogLevel.Success, item);

                // 4. Generate description (save to Seeding folder)
                item.Status = QueueItemStatus.GeneratingDescription;
                item.StatusMessage = "Generating description...";

                var descriptionTemplate = File.Exists(DescriptionTemplatePath) 
                    ? await File.ReadAllTextAsync(DescriptionTemplatePath) 
                    : GetDefaultDescriptionTemplate();

                // Use item's SourceInfoForDescription which uses actual file size and source group
                var description = _nyaaService.GenerateDescription(item, descriptionTemplate, item.SourceInfoForDescription);

                // Save description to Seeding folder
                item.DescriptionFilePath = Path.Combine(SeedingFolder, $"{item.OutputFileName}_description.txt");
                await File.WriteAllTextAsync(item.DescriptionFilePath, description);
                Log($"📝 Description saved: {item.DescriptionFilePath}", ActivityLogLevel.Info, item);

                // 5. Create torrent (save to Seeding folder)
                item.Status = QueueItemStatus.CreatingTorrent;
                item.StatusMessage = "Creating torrent...";
                Log("📦 Creating torrent file...", ActivityLogLevel.Info, item);

                item.TorrentFilePath = Path.Combine(SeedingFolder, $"{item.OutputFileName}.torrent");
                var torrentResult = await _torrentService.CreateTorrentAsync(item.EncodedFilePath, item.TorrentFilePath);

                if (!torrentResult.Success)
                {
                    Log($"⚠️ Torrent creation failed: {torrentResult.ErrorMessage}", ActivityLogLevel.Warning, item);
                }
                else
                {
                    item.TorrentHash = torrentResult.InfoHash;
                    Log($"✅ Torrent created: {item.TorrentFilePath}", ActivityLogLevel.Success, item);
                }

                // 6. Upload to FTP (in order: torrent, description, episode)
                await _discordService.SendUploadStartedAsync(item);

                // Upload torrent file
                item.Status = QueueItemStatus.UploadingTorrentFile;
                item.StatusMessage = "Uploading torrent file...";
                Log("📤 Uploading torrent file...", ActivityLogLevel.Info, item);

                var torrentUpload = await _ftpService.UploadTorrentFileAsync(
                    item.TorrentFilePath,
                    Path.GetFileName(item.TorrentFilePath),
                    cancellationToken);

                if (!torrentUpload.Success)
                {
                    Log($"⚠️ Torrent upload failed: {torrentUpload.ErrorMessage}", ActivityLogLevel.Warning, item);
                }

                // Upload description
                item.Status = QueueItemStatus.UploadingDescription;
                item.StatusMessage = "Uploading description...";
                Log("📤 Uploading description...", ActivityLogLevel.Info, item);

                var descUpload = await _ftpService.UploadDescriptionFileAsync(
                    item.DescriptionFilePath,
                    Path.GetFileName(item.DescriptionFilePath),
                    cancellationToken);

                if (!descUpload.Success)
                {
                    Log($"⚠️ Description upload failed: {descUpload.ErrorMessage}", ActivityLogLevel.Warning, item);
                }

                // Upload episode (this takes a while)
                item.Status = QueueItemStatus.UploadingEpisode;
                item.StatusMessage = "Uploading episode...";
                Log("📤 Uploading episode...", ActivityLogLevel.Info, item);

                var episodeUpload = await _ftpService.UploadEpisodeAsync(
                    item.EncodedFilePath,
                    Path.GetFileName(item.EncodedFilePath),
                    cancellationToken);

                if (!episodeUpload.Success)
                {
                    item.Status = QueueItemStatus.UploadFailed;
                    item.StatusMessage = $"Upload failed: {episodeUpload.ErrorMessage}";
                    item.LastError = episodeUpload.ErrorMessage;
                    Log($"❌ Upload failed after {episodeUpload.Attempts} attempts", ActivityLogLevel.Error, item);
                    return;
                }

                await _discordService.SendUploadCompleteAsync(item);

                // 7. Add to seedbox qBittorrent
                item.Status = QueueItemStatus.AddingToSeedbox;
                item.StatusMessage = "Adding to seedbox...";
                Log("🌐 Adding to seedbox...", ActivityLogLevel.Info, item);

                var seedboxAdded = await _qbitService.AddTorrentFileAsync(
                    item.TorrentFilePath,
                    SeedboxReleasesPath,
                    isLocal: false);
                
                if (seedboxAdded)
                    Log("✅ Added to seedbox qBittorrent", ActivityLogLevel.Success, item);
                else
                    Log("⚠️ Failed to add to seedbox qBittorrent", ActivityLogLevel.Warning, item);

                // 8. Post to Nyaa
                if (item.Show.AutopostOnTrackers)
                {
                    item.Status = QueueItemStatus.PostingToNyaa;
                    item.StatusMessage = "Posting to Nyaa...";
                    Log("📢 Posting to Nyaa...", ActivityLogLevel.Info, item);

                    var nyaaResult = await _nyaaService.PostToNyaaAsync(item, item.TorrentFilePath, description, IsTestMode);

                    if (nyaaResult.Success)
                    {
                        item.NyaaUrl = nyaaResult.Url;
                        Log($"✅ Posted to Nyaa: {nyaaResult.Url}", ActivityLogLevel.Success, item);
                        await _discordService.SendNyaaPostedAsync(item, nyaaResult.Url, IsTestMode);
                    }
                    else
                    {
                        Log($"⚠️ Nyaa posting failed: {nyaaResult.Message}", ActivityLogLevel.Warning, item);
                    }
                }

                }

                // 9. Cleanup source files
                // File cleanup is synchronous and can touch many files. Keep it off the
                // calling context so queue processing does not block the UI thread.
                await QueueCleanupScheduler.RunAsync(() => CleanupAfterEncoding(item));

                // 10. Complete!
                item.Status = QueueItemStatus.Completed;
                item.StatusMessage = "Completed!";
                item.CompletedAt = DateTime.Now;
                Log($"🎉 Release complete: {item.TorrentDisplayName}", ActivityLogLevel.Success, item);
            }
            catch (Exception ex)
            {
                item.Status = QueueItemStatus.Error;
                item.StatusMessage = $"Error: {ex.Message}";
                item.LastError = ex.Message;
                Log($"❌ Error processing {item.Show.OutputFileTitle}: {ex.Message}", ActivityLogLevel.Error, item);
                await _discordService.SendErrorAsync(item, ex.Message);
                PauseQueue();
            }
        }

        /// <summary>
        /// Cleans up source files, LWI index files, and temp folders after successful encoding
        /// </summary>
        private void CleanupAfterEncoding(QueueItem item)
        {
            try
            {
                Log("🧹 Starting cleanup...", ActivityLogLevel.Info, item);
                _cleanupStarted?.Invoke();

                // 1. Delete source file
                if (File.Exists(item.SourceFilePath))
                {
                    File.Delete(item.SourceFilePath);
                    Log($"🗑️ Deleted source file: {Path.GetFileName(item.SourceFilePath)}", ActivityLogLevel.Info, item);
                }

                // 2. Delete LWI index files (created by lsmas/ffms2)
                var sourceDir = Path.GetDirectoryName(item.SourceFilePath);
                if (!string.IsNullOrEmpty(sourceDir) && Directory.Exists(sourceDir))
                {
                    var lwiFiles = Directory.GetFiles(sourceDir, "*.lwi", SearchOption.TopDirectoryOnly);
                    foreach (var lwiFile in lwiFiles)
                    {
                        try
                        {
                            File.Delete(lwiFile);
                            Log($"🗑️ Deleted LWI file: {Path.GetFileName(lwiFile)}", ActivityLogLevel.Debug, item);
                        }
                        catch (Exception ex)
                        {
                            Log($"⚠️ Failed to delete LWI file {Path.GetFileName(lwiFile)}: {ex.Message}", ActivityLogLevel.Debug, item);
                        }
                    }
                }

                // 3. Delete temp encoding folders (done, data, video, audio-subs)
                var tempFolders = new[] { "done", "data", "video", "audio-subs" };
                foreach (var folder in tempFolders)
                {
                    var folderPath = Path.Combine(EncodingFolder, folder);
                    if (Directory.Exists(folderPath))
                    {
                        try
                        {
                            Directory.Delete(folderPath, true);
                            Log($"🗑️ Deleted temp folder: {folder}", ActivityLogLevel.Debug, item);
                        }
                        catch (Exception ex)
                        {
                            Log($"⚠️ Failed to delete temp folder {folder}: {ex.Message}", ActivityLogLevel.Debug, item);
                        }
                    }
                }

                // 4. Clean up source folder if empty (for torrents that extract to a subfolder)
                if (!string.IsNullOrEmpty(sourceDir) && 
                    sourceDir != EncodingFolder && 
                    Directory.Exists(sourceDir))
                {
                    try
                    {
                        var remainingFiles = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
                        if (remainingFiles.Length == 0)
                        {
                            Directory.Delete(sourceDir, true);
                            Log($"🗑️ Deleted empty source folder", ActivityLogLevel.Debug, item);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"⚠️ Failed to delete source folder: {ex.Message}", ActivityLogLevel.Debug, item);
                    }
                }

                // 5. Clean up screenshots
                _screenshotService.CleanupScreenshots(item.ScreenshotPaths);
                Log($"🗑️ Cleaned up {item.ScreenshotPaths.Count} screenshot files", ActivityLogLevel.Debug, item);

                Log("✅ Cleanup complete", ActivityLogLevel.Success, item);
            }
            catch (Exception ex)
            {
                Log($"⚠️ Cleanup error (non-fatal): {ex.Message}", ActivityLogLevel.Warning, item);
            }
        }

        private void Log(string message, ActivityLogLevel level, QueueItem? item = null)
        {
            LogReceived?.Invoke(this, new ActivityLogEntry
            {
                Message = message,
                Level = level,
                QueueItemId = item?.Id,
                ShowName = item?.Show.OutputFileTitle
            });
        }

        private string GetDefaultDescriptionTemplate()
        {
            return @"**Title**: @@TITLE@@
HEVC 10bit SoftSubbed - 1920 x 1080
Encoded by: Judas Team
**Source**: @@SOURCE@@

**Audio**: @@AUDIO_TRACKS@@
**Subtitles**: @@SUBS_TRACKS@@

[Request an anime or get DDL links @ Discord](@@DISCORD_LINK@@)
[Donations are always welcome](https://www.paypal.com/paypalme/JudasEncoders)

**[If you like this release please seed]**

@@SCREENSHOTS@@";
        }

        public void CancelProcessing()
        {
            Log("⛔ Cancelling all operations...", ActivityLogLevel.Warning);
            _processingCts?.Cancel();
            _encodingService.Cancel();
            _muxingService.Cancel();
            _ftpService.Cancel();
            _screenshotService.Cancel();
            _isPaused = true;
            
            foreach (var item in Queue.Where(q => 
                q.Status != QueueItemStatus.Completed && 
                q.Status != QueueItemStatus.Pending &&
                q.Status != QueueItemStatus.Error))
            {
                item.Status = QueueItemStatus.Error;
                item.StatusMessage = "Cancelled by user";
                item.LastError = "Cancelled by user";
            }
            
            Log("⛔ All operations cancelled", ActivityLogLevel.Warning);
        }

        public void CancelItem(QueueItem item)
        {
            Log($"⛔ Cancelling: {item.OutputFileName}", ActivityLogLevel.Warning, item);
            
            if (item.Status == QueueItemStatus.Downloading ||
                item.Status == QueueItemStatus.AnalyzingTracks ||
                item.Status == QueueItemStatus.Encoding ||
                item.Status == QueueItemStatus.Muxing ||
                item.Status == QueueItemStatus.TakingScreenshots ||
                item.Status == QueueItemStatus.UploadingScreenshots ||
                item.Status == QueueItemStatus.UploadingTorrentFile ||
                item.Status == QueueItemStatus.UploadingDescription ||
                item.Status == QueueItemStatus.UploadingEpisode)
            {
                _processingCts?.Cancel();
                _encodingService.Cancel();
                _muxingService.Cancel();
                _ftpService.Cancel();
                _screenshotService.Cancel();
            }
            
            item.Status = QueueItemStatus.Error;
            item.StatusMessage = "Cancelled by user";
            item.LastError = "Cancelled by user";
        }

        // Test Run Methods
        public async Task<List<RssItem>> GetRssFeedItemsAsync(string feedUrl)
        {
            return await _rssService.FetchFeedAsync(feedUrl);
        }

        public async Task StartTestRunAsync(ShowViewModel showVm, RssItem selectedItem)
        {
            EnsureFoldersExist();
            
            var episodeNum = _rssService.ExtractEpisodeNumber(selectedItem.Title, showVm.CustomEpisodeRegex);
            var adjustedEpNum = (episodeNum ?? 1) + showVm.NumberOfEpisodesToRemoveFromCount;

            var queueItem = new QueueItem
            {
                Show = showVm.Model,
                EpisodeNumber = Math.Max(1, adjustedEpNum),
                Version = 1,
                SourceFileName = selectedItem.Title,
                SourceGroup = showVm.SourceGroup,
                TorrentHash = "",
                Status = QueueItemStatus.Pending,
                IsTestRun = true,
                TestEncodeDurationSeconds = 300 // 5 minutes
            };

            Application.Current?.Dispatcher?.BeginInvoke(() => Queue.Add(queueItem));
            Log($"🧪 [TEST RUN] Starting test: {showVm.OutputFileTitle} E{queueItem.EpisodeNumber}", ActivityLogLevel.Info, queueItem);
            Log($"📁 Folders: Encoding={EncodingFolder}, Screenshots={ScreenshotsFolder}, Seeding={SeedingFolder}", ActivityLogLevel.Info, queueItem);

            await _discordService.SendMessageAsync(
                $"🧪 Starting TEST RUN: **{showVm.OutputFileTitle}** Episode {queueItem.EpisodeNumber}\n" +
                $"Will encode 5 minutes only and post as HIDDEN torrent.",
                "Test Run Started",
                DiscordEmbedColor.Orange);

            _ = Task.Run(async () =>
            {
                try
                {
                    await StartDownloadAsync(queueItem, selectedItem.TorrentUrl);
                }
                catch (Exception ex)
                {
                    Log($"❌ Test run download error: {ex.Message}", ActivityLogLevel.Error, queueItem);
                }
            });
        }
    }
}
