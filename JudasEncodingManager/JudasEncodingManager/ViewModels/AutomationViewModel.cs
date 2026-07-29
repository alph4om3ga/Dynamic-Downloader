using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.Input;
using JudasEncodingManager.Models;
using JudasEncodingManager.Services;
using static JudasEncodingManager.Models.DownloadMethod;

namespace JudasEncodingManager.ViewModels
{
    /// <summary>
    /// Tracks the monitoring state for each show.
    /// CRD shows use the exact schedule: 1min×10min → 10min×1hr → 30min×6hr → stop.
    /// RSS shows use: 5min×1hr → 30min×6hr → 12hr.
    /// </summary>
    public class ShowMonitoringState
    {
        public string ShowId { get; set; } = "";
        public DownloadMethod Method { get; set; } = CRD;
        public DateTime? LastCheckTime { get; set; }
        public DateTime? ReleaseWindowStart { get; set; }
        public DateTime? NextScheduledCheck { get; set; }
        public bool FoundThisWeek { get; set; }
        public int CheckCount { get; set; }
        public TimeSpan CurrentInterval { get; set; } = TimeSpan.FromMinutes(1);

        // ── CRD timing constants ────────────────────────────────────────────
        // Phase 1: every 1 min for the first 10 min
        // Phase 2: every 10 min until 70 min total elapsed (1 hour after phase 1)
        // Phase 3: every 30 min until 430 min total elapsed (6 hours after phase 2)
        // Stop:    > 430 min — wait until next scheduled airing
        private static readonly TimeSpan CrdPhase1Interval  = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan CrdPhase2Interval  = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan CrdPhase3Interval  = TimeSpan.FromMinutes(30);
        private static readonly double   CrdPhase1EndMin    = 10;
        private static readonly double   CrdPhase2EndMin    = 70;   // 10 + 60
        private static readonly double   CrdWindowEndMin    = 430;  // 70 + 360

        // ── RSS timing constants ────────────────────────────────────────────
        private static readonly TimeSpan RssInitialInterval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan RssAfterOneHour    = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan RssAfterSixHours   = TimeSpan.FromHours(12);

        public void ResetForNewWeek()
        {
            FoundThisWeek   = false;
            CheckCount      = 0;
            CurrentInterval = Method == CRD ? CrdPhase1Interval : RssInitialInterval;
            ReleaseWindowStart = null;
            NextScheduledCheck = null;
        }

        /// <summary>
        /// For CRD shows: returns true when the entire monitoring window (430 min) has
        /// elapsed without finding an episode — caller should stop checking until next
        /// scheduled airing.
        /// </summary>
        public bool HasWindowExpired()
        {
            if (Method != CRD || ReleaseWindowStart == null) return false;
            return (DateTime.Now - ReleaseWindowStart.Value).TotalMinutes > CrdWindowEndMin;
        }

        public void UpdateIntervalBasedOnElapsedTime()
        {
            if (ReleaseWindowStart == null) return;

            var elapsed = DateTime.Now - ReleaseWindowStart.Value;

            if (Method == CRD)
            {
                if (elapsed.TotalMinutes < CrdPhase1EndMin)
                    CurrentInterval = CrdPhase1Interval;
                else if (elapsed.TotalMinutes < CrdPhase2EndMin)
                    CurrentInterval = CrdPhase2Interval;
                else
                    CurrentInterval = CrdPhase3Interval;
            }
            else
            {
                if (elapsed.TotalHours >= 6)
                    CurrentInterval = RssAfterSixHours;
                else if (elapsed.TotalHours >= 1)
                    CurrentInterval = RssAfterOneHour;
                else
                    CurrentInterval = RssInitialInterval;
            }
        }
    }

    public class AutomationViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private readonly ObservableCollection<ShowViewModel> _shows;
        private readonly Dictionary<string, ShowMonitoringState> _monitoringStates = new();
        private readonly Func<AppSettings> _getSettings;
        private readonly Action? _onSave;
        
        // Services
        private QBittorrentService? _qbitService;
        private EncodingService? _encodingService;
        private MuxingService? _muxingService;
        private ScreenshotService? _screenshotService;
        private FtpService? _ftpService;
        private TorrentService? _torrentService;
        private NyaaService? _nyaaService;
        private DiscordService? _discordService;
        private CRDService? _crdService;
        
        // State
        private bool _isMonitoring;
        private bool _isPaused;
        private bool _isProcessing;
        private int _rssCheckInterval = 5;
        private string _monitoringStatus = "Stopped";
        private ShowViewModel? _testRunSelectedShow;
        private RssItem? _testRunSelectedEpisode;
        private int _testRunSourceIndex = 0; // 0 = CRD, 1 = RSS
        private string _testRunStatus = "Select a show and load episodes";
        private QueueItem? _selectedQueueItem;
        private CancellationTokenSource? _monitoringCts;
        private CancellationTokenSource? _currentProcessCts;
        private CancellationTokenSource? _testRunCts;
        
        // Logging
        private string _logsFolder = @"C:\JudasEncodingManager\Logs";

        // Test run state
        private bool _isSimulatedTest = true;
        private bool _isQuickEncode = true;  // true = 5-min test, false = full encode
        private bool _isHiddenPost = true;   // true = hidden on Nyaa, false = public
        private bool _isTestRunning;
        private double _testRunProgress;

        public AutomationViewModel(ObservableCollection<ShowViewModel> shows, Func<AppSettings>? getSettings = null, Action? onSave = null)
        {
            _shows = shows;
            _getSettings = getSettings ?? (() => new AppSettings());
            _onSave = onSave;

            Queue = new ObservableCollection<QueueItem>();
            TestRunRssItems = new ObservableCollection<RssItem>();
            ActivityLog = new ObservableCollection<ActivityLogEntry>();

            // Monitoring commands
            StartMonitoringCommand = new RelayCommand(StartMonitoring, () => !IsMonitoring);
            StopMonitoringCommand = new RelayCommand(StopMonitoring, () => IsMonitoring);
            PauseQueueCommand = new RelayCommand(PauseQueue, () => IsMonitoring && !IsPaused);
            ResumeQueueCommand = new RelayCommand(ResumeQueue, () => IsPaused);
            CancelCurrentProcessCommand = new RelayCommand(CancelCurrentProcess, () => IsProcessing);
            StopAllProcessingCommand = new RelayCommand(StopAllProcessing);

            // Queue commands
            RetryFailedItemCommand = new RelayCommand(RetryFailedItem, () => SelectedQueueItem != null && IsFailedStatus(SelectedQueueItem.Status));
            RemoveQueueItemCommand = new RelayCommand(RemoveQueueItem, () => SelectedQueueItem != null);
            ClearCompletedItemsCommand = new RelayCommand(ClearCompletedItems);

            // Test run commands
            LoadTestRunRssItemsCommand = new AsyncRelayCommand(LoadTestRunRssItemsAsync, () => TestRunSelectedShow != null);
            StartTestRunCommand = new AsyncRelayCommand(StartTestRunAsync, () => TestRunSelectedEpisode != null && !IsProcessing && !IsTestRunning);
            CancelTestRunCommand = new RelayCommand(CancelTestRun, () => IsTestRunning);
            QueueManualReleaseCommand = new AsyncRelayCommand(QueueManualReleaseAsync, () => TestRunSelectedEpisode != null);

            // Activity log
            ClearActivityLogCommand = new RelayCommand(ClearActivityLog);

            AddLogEntry("Automation system ready", ActivityLogLevel.Info);
        }
        
        private void InitializeServices()
        {
            var settings = _getSettings();
            
            // QBittorrent
            _qbitService = new QBittorrentService();
            _qbitService.Configure(
                settings.QBittorrent.LocalIpPort,
                settings.QBittorrent.SeedboxIpPort,
                settings.QBittorrent.SeedboxUsername,
                settings.QBittorrent.SeedboxPassword
            );
            
            // Encoding
            _encodingService = new EncodingService
            {
                EncodingScriptsPath = settings.Folders.EncodingFolder,
                OutputPath = settings.Folders.SeedingFolder
            };
            _encodingService.ProgressChanged += (s, p) => 
            {
                Application.Current.Dispatcher.Invoke(() => TestRunProgress = 20 + (p * 40)); // 20-60%
            };
            _encodingService.OutputReceived += (s, msg) => AddLogEntry($"[Encode] {msg}", ActivityLogLevel.Info);
            
            // Muxing
            _muxingService = new MuxingService();
            _muxingService.Configure(settings.Remuxer.MkvmergePath, settings.Remuxer.FfmpegPath);
            _muxingService.LogMessage += (s, msg) => AddLogEntry($"[Mux] {msg}", ActivityLogLevel.Info);
            
            // Screenshots
            _screenshotService = new ScreenshotService
            {
                OutputFolder = settings.Folders.ScreenshotsFolder
            };
            _screenshotService.Configure(settings.Remuxer.FfmpegPath, settings.ImgbbApiKey);
            
            // FTP
            _ftpService = new FtpService();
            _ftpService.Configure(
                settings.Ftp.Host,
                settings.Ftp.Username,
                settings.Ftp.Password,
                settings.Ftp.ReleasesPath,
                settings.Ftp.TorrentsPath
            );
            _ftpService.ProgressChanged += (s, p) =>
            {
                Application.Current.Dispatcher.Invoke(() => TestRunProgress = 92 + (p * 5)); // 92-97%
            };
            
            // Torrent
            _torrentService = new TorrentService();
            
            // Nyaa
            _nyaaService = new NyaaService();
            _nyaaService.Configure(
                settings.AutoPosting.NyaaCookieDdlg,
                settings.AutoPosting.NyaaCookieSession,
                settings.Discord.ServerInviteLink
            );
            
            // Discord
            _discordService = new DiscordService();
            _discordService.Configure(
                settings.Discord.WebhookUrl,
                settings.MachineName,
                settings.TestMode
            );
            
            // CRD
            _crdService = new CRDService();
            _crdService.Configure(settings.CRD.Path);
            _crdService.LogMessage += (_, msg) => AddLogEntry(msg, ActivityLogLevel.Info);

            _logsFolder = settings.Folders.LogsFolder;

            AddLogEntry("Services initialized", ActivityLogLevel.Info);
        }

        // ==================== COLLECTIONS ====================

        public ObservableCollection<QueueItem> Queue { get; }
        public ObservableCollection<RssItem> TestRunRssItems { get; }
        public ObservableCollection<ActivityLogEntry> ActivityLog { get; }

        // ==================== COMMANDS ====================

        public ICommand StartMonitoringCommand { get; }
        public ICommand StopMonitoringCommand { get; }
        public ICommand PauseQueueCommand { get; }
        public ICommand ResumeQueueCommand { get; }
        public ICommand CancelCurrentProcessCommand { get; }
        public ICommand StopAllProcessingCommand { get; }
        public ICommand RetryFailedItemCommand { get; }
        public ICommand RemoveQueueItemCommand { get; }
        public ICommand ClearCompletedItemsCommand { get; }
        public ICommand LoadTestRunRssItemsCommand { get; }
        public ICommand StartTestRunCommand { get; }
        public ICommand CancelTestRunCommand { get; }
        public ICommand QueueManualReleaseCommand { get; }
        public ICommand ClearActivityLogCommand { get; }

        // ==================== TEST MODE PROPERTIES ====================

        public bool IsSimulatedTest
        {
            get => _isSimulatedTest;
            set
            {
                _isSimulatedTest = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsRealTest));
                OnPropertyChanged(nameof(TestModeDescription));
                OnPropertyChanged(nameof(CanSelectEncodeOptions));
            }
        }

        public bool IsRealTest
        {
            get => !_isSimulatedTest;
            set
            {
                _isSimulatedTest = !value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsSimulatedTest));
                OnPropertyChanged(nameof(TestModeDescription));
                OnPropertyChanged(nameof(CanSelectEncodeOptions));
            }
        }

        public bool CanSelectEncodeOptions => !IsSimulatedTest;

        // Encode type: Quick (5-min) vs Full
        public bool IsQuickEncode
        {
            get => _isQuickEncode;
            set
            {
                _isQuickEncode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsFullEncode));
                OnPropertyChanged(nameof(TestModeDescription));
            }
        }

        public bool IsFullEncode
        {
            get => !_isQuickEncode;
            set
            {
                _isQuickEncode = !value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsQuickEncode));
                OnPropertyChanged(nameof(TestModeDescription));
            }
        }

        // Post visibility: Hidden vs Public
        public bool IsHiddenPost
        {
            get => _isHiddenPost;
            set
            {
                _isHiddenPost = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsPublicPost));
                OnPropertyChanged(nameof(TestModeDescription));
            }
        }

        public bool IsPublicPost
        {
            get => !_isHiddenPost;
            set
            {
                _isHiddenPost = !value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsHiddenPost));
                OnPropertyChanged(nameof(TestModeDescription));
            }
        }

        public string TestModeDescription
        {
            get
            {
                if (IsSimulatedTest)
                    return "Simulates the full pipeline with timing delays. No actual files are processed.";
                
                var encodeType = IsQuickEncode ? "5-minute test encode" : "Full episode encode";
                var postType = IsHiddenPost ? "post as HIDDEN" : "post as PUBLIC";
                return $"Real pipeline: {encodeType}, {postType} on Nyaa.";
            }
        }

        public bool IsTestRunning
        {
            get => _isTestRunning;
            set
            {
                _isTestRunning = value;
                OnPropertyChanged();
                NotifyCommandsChanged();
            }
        }

        public double TestRunProgress
        {
            get => _testRunProgress;
            set
            {
                _testRunProgress = value;
                OnPropertyChanged();
            }
        }

        // ==================== MONITORING PROPERTIES ====================

        public bool IsMonitoring
        {
            get => _isMonitoring;
            set
            {
                _isMonitoring = value;
                OnPropertyChanged();
                UpdateMonitoringStatus();
                NotifyCommandsChanged();
            }
        }

        public bool IsPaused
        {
            get => _isPaused;
            set
            {
                _isPaused = value;
                OnPropertyChanged();
                UpdateMonitoringStatus();
                NotifyCommandsChanged();
            }
        }

        public bool IsProcessing
        {
            get => _isProcessing;
            set
            {
                _isProcessing = value;
                OnPropertyChanged();
                UpdateMonitoringStatus();
                NotifyCommandsChanged();
            }
        }

        public int RssCheckInterval
        {
            get => _rssCheckInterval;
            set
            {
                _rssCheckInterval = Math.Max(1, Math.Min(60, value));
                OnPropertyChanged();
            }
        }

        public string MonitoringStatus
        {
            get => _monitoringStatus;
            set { _monitoringStatus = value; OnPropertyChanged(); }
        }

        public string QueueSummary
        {
            get
            {
                var pending = Queue.Count(q => q.Status == QueueItemStatus.Pending);
                var processing = Queue.Count(q => q.Status == QueueItemStatus.Downloading || 
                                                   q.Status == QueueItemStatus.Encoding ||
                                                   q.Status == QueueItemStatus.Muxing ||
                                                   q.Status == QueueItemStatus.UploadingEpisode);
                var completed = Queue.Count(q => q.Status == QueueItemStatus.Completed);
                var failed = Queue.Count(q => IsFailedStatus(q.Status));

                return $"Pending: {pending} | Processing: {processing} | Done: {completed} | Failed: {failed}";
            }
        }

        public ShowViewModel? TestRunSelectedShow
        {
            get => _testRunSelectedShow;
            set
            {
                _testRunSelectedShow = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LoadTestRunButtonLabel));
                TestRunRssItems.Clear();
                TestRunSelectedEpisode = null;
                TestRunStatus = value != null
                    ? $"Selected: {value.DisplayName} — choose a source and click '{LoadTestRunButtonLabel}'"
                    : "Select a show";
                ((AsyncRelayCommand)LoadTestRunRssItemsCommand).NotifyCanExecuteChanged();
            }
        }

        /// <summary>0 = CRD (scan output folder), 1 = RSS (load feed).</summary>
        public int TestRunSourceIndex
        {
            get => _testRunSourceIndex;
            set
            {
                _testRunSourceIndex = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TestRunSourceIsCRD));
                OnPropertyChanged(nameof(TestRunSourceIsRSS));
                OnPropertyChanged(nameof(LoadTestRunButtonLabel));
                // Clear loaded episodes when source changes
                TestRunRssItems.Clear();
                TestRunSelectedEpisode = null;
            }
        }

        public bool TestRunSourceIsCRD
        {
            get => _testRunSourceIndex == 0;
            set { if (value) TestRunSourceIndex = 0; }
        }

        public bool TestRunSourceIsRSS
        {
            get => _testRunSourceIndex == 1;
            set { if (value) TestRunSourceIndex = 1; }
        }

        /// <summary>Label for the "Load Episodes" button — adapts to the selected source.</summary>
        public string LoadTestRunButtonLabel => _testRunSourceIndex == 0 ? "📁 Scan CRD Folder" : "📥 Load RSS Feed";

        public RssItem? TestRunSelectedEpisode
        {
            get => _testRunSelectedEpisode;
            set
            {
                _testRunSelectedEpisode = value;
                OnPropertyChanged();
                if (value != null)
                {
                    TestRunStatus = $"Ready to test: {value.Title}";
                }
                ((AsyncRelayCommand)StartTestRunCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)QueueManualReleaseCommand).NotifyCanExecuteChanged();
            }
        }

        public string TestRunStatus
        {
            get => _testRunStatus;
            set { _testRunStatus = value; OnPropertyChanged(); }
        }

        public QueueItem? SelectedQueueItem
        {
            get => _selectedQueueItem;
            set
            {
                _selectedQueueItem = value;
                OnPropertyChanged();
                ((RelayCommand)RetryFailedItemCommand).NotifyCanExecuteChanged();
                ((RelayCommand)RemoveQueueItemCommand).NotifyCanExecuteChanged();
            }
        }

        // ==================== SMART RSS SCHEDULING ====================

        /// <summary>
        /// Gets the next release time for a show (today or next week)
        /// </summary>
        private DateTime? GetNextReleaseTime(ShowViewModel show)
        {
            if (!show.ReleaseDay.HasValue || string.IsNullOrEmpty(show.ReleaseTime))
                return null;

            if (!TimeSpan.TryParse(show.ReleaseTime, out var releaseTime))
                return null;

            var today = DateTime.Today;
            var daysUntilRelease = ((int)show.ReleaseDay.Value - (int)today.DayOfWeek + 7) % 7;
            
            var nextRelease = today.AddDays(daysUntilRelease).Add(releaseTime);
            
            // If the release time already passed today, check if we should still monitor or wait for next week
            if (nextRelease < DateTime.Now)
            {
                // Check if we're within the monitoring window (release time + 6 hours)
                if (DateTime.Now < nextRelease.AddHours(6))
                {
                    return nextRelease; // Still in active monitoring window
                }
                // Otherwise, next week
                nextRelease = nextRelease.AddDays(7);
            }
            
            return nextRelease;
        }

        /// <summary>
        /// Determines if it's time to check a show based on its download method and smart scheduling.
        /// CRD: checks the output folder. RSS: polls the feed URL.
        /// </summary>
        private bool ShouldCheckShow(ShowViewModel show, out string reason)
        {
            reason = "";

            if (!show.IsActive)
            {
                reason = "Show is inactive";
                return false;
            }

            // Download-method-specific prerequisites
            switch (show.DownloadMethod)
            {
                case DownloadMethod.CRD when string.IsNullOrEmpty(show.CrdOutputPath):
                    reason = "No CRD output folder configured";
                    return false;

                case DownloadMethod.RSS when string.IsNullOrEmpty(show.RssFeed):
                    reason = "No RSS feed configured";
                    return false;

            }

            var showId = show.Model.OutputFileTitle ?? show.OutputTorrentTitle;

            // Get or create monitoring state
            if (!_monitoringStates.TryGetValue(showId, out var state))
            {
                state = new ShowMonitoringState { ShowId = showId, Method = show.DownloadMethod };
                _monitoringStates[showId] = state;
            }
            else
            {
                state.Method = show.DownloadMethod; // keep in sync if user changed method
            }

            var nextRelease = GetNextReleaseTime(show);
            if (!nextRelease.HasValue)
            {
                reason = "No release schedule configured";
                return false;
            }

            var now = DateTime.Now;

            // Reset state when a new weekly window begins (> 7 days since window started)
            if (state.ReleaseWindowStart.HasValue &&
                (now - state.ReleaseWindowStart.Value).TotalDays >= 7)
            {
                state.ResetForNewWeek();
                AddLogEntry($"New release week for {show.DisplayName} — monitoring reset.", ActivityLogLevel.Info);
            }

            if (state.FoundThisWeek)
            {
                reason = "Already found this week's episode";
                return false;
            }

            // Not yet in the release window
            if (now < nextRelease.Value)
            {
                var until = nextRelease.Value - now;
                reason = $"Release in {until.Hours}h {until.Minutes}m";
                return false;
            }

            // Enter the release window for the first time
            if (!state.ReleaseWindowStart.HasValue)
            {
                state.ReleaseWindowStart = nextRelease.Value;
                state.NextScheduledCheck = now; // check immediately
                AddLogEntry($"🔔 Release window opened for {show.DisplayName} ({show.DownloadMethod})", ActivityLogLevel.Info);
            }

            // CRD shows stop checking after the window expires (≈ 7 hours)
            if (state.HasWindowExpired())
            {
                reason = "CRD monitoring window expired — waiting for next airing";
                return false;
            }

            // Update interval based on elapsed time in this window
            state.UpdateIntervalBasedOnElapsedTime();

            if (state.NextScheduledCheck.HasValue && now < state.NextScheduledCheck.Value)
            {
                var until = state.NextScheduledCheck.Value - now;
                reason = $"Next check in {(int)until.TotalMinutes}m {until.Seconds}s (every {state.CurrentInterval.TotalMinutes}m)";
                return false;
            }

            reason = $"Checking now (every {state.CurrentInterval.TotalMinutes}m, elapsed {(now - state.ReleaseWindowStart!.Value).TotalMinutes:F0}m)";
            return true;
        }

        /// <summary>
        /// Updates the monitoring state after checking a show's RSS
        /// </summary>
        private void UpdateShowMonitoringState(ShowViewModel show, bool foundNewEpisode)
        {
            var showId = show.Model.OutputFileTitle ?? show.OutputTorrentTitle;
            
            if (!_monitoringStates.TryGetValue(showId, out var state))
                return;

            state.LastCheckTime = DateTime.Now;
            state.CheckCount++;

            if (foundNewEpisode)
            {
                state.FoundThisWeek = true;
                state.NextScheduledCheck = null;
                AddLogEntry($"Found new episode for {show.DisplayName}! Pausing checks until next week.", ActivityLogLevel.Success);
            }
            else
            {
                state.NextScheduledCheck = DateTime.Now.Add(state.CurrentInterval);
                var elapsed = state.ReleaseWindowStart.HasValue 
                    ? (DateTime.Now - state.ReleaseWindowStart.Value).TotalHours 
                    : 0;
                AddLogEntry($"{show.DisplayName}: No new episode. Next check in {state.CurrentInterval.TotalMinutes}m (waiting {elapsed:F1}h)", ActivityLogLevel.Info);
            }
        }

        // ==================== MONITORING METHODS ====================

        private void StartMonitoring()
        {
            IsMonitoring = true;
            IsPaused = false;

            InitializeServices();

            // Initialize monitoring states for all active shows.
            // On restart, preserve FoundThisWeek (avoid re-processing) but clear
            // NextScheduledCheck so shows are evaluated immediately rather than
            // waiting out the stale interval from the previous session.
            foreach (var show in _shows.Where(s => s.IsActive))
            {
                var showId = show.Model.OutputFileTitle ?? show.OutputTorrentTitle;
                if (!_monitoringStates.ContainsKey(showId))
                {
                    _monitoringStates[showId] = new ShowMonitoringState
                    {
                        ShowId = showId,
                        Method = show.DownloadMethod
                    };
                }
                else
                {
                    // Clear stale timing so the loop checks immediately on restart
                    var existing = _monitoringStates[showId];
                    existing.NextScheduledCheck = null;
                    existing.LastCheckTime = null;
                }
            }

            AddLogEntry("Started monitoring (CRD primary, RSS secondary)", ActivityLogLevel.Success);
            LogNextCheckTimes();

            _monitoringCts?.Dispose();
            _monitoringCts = new CancellationTokenSource();
            _ = MonitorShowsAsync(_monitoringCts.Token);
        }

        private void LogNextCheckTimes()
        {
            foreach (var show in _shows.Where(s => s.IsActive))
            {
                var nextRelease = GetNextReleaseTime(show);
                if (nextRelease.HasValue)
                {
                    var timeUntil = nextRelease.Value - DateTime.Now;
                    if (timeUntil.TotalSeconds > 0)
                    {
                        AddLogEntry($"  {show.DisplayName}: releases in {timeUntil.Hours}h {timeUntil.Minutes}m ({nextRelease.Value:ddd HH:mm})", ActivityLogLevel.Info);
                    }
                    else
                    {
                        AddLogEntry($"  {show.DisplayName}: in release window, checking now", ActivityLogLevel.Info);
                    }
                }
            }
        }

        private void StopMonitoring()
        {
            _monitoringCts?.Cancel();
            IsMonitoring = false;
            IsPaused = false;
            AddLogEntry("Stopped monitoring", ActivityLogLevel.Warning);
        }

        private void PauseQueue()
        {
            IsPaused = true;
            AddLogEntry("Queue processing paused", ActivityLogLevel.Warning);
        }

        private void ResumeQueue()
        {
            IsPaused = false;
            AddLogEntry("Queue processing resumed", ActivityLogLevel.Success);
        }

        private void CancelCurrentProcess()
        {
            _currentProcessCts?.Cancel();
            AddLogEntry("Cancelling current process...", ActivityLogLevel.Warning);
        }

        private void StopAllProcessing()
        {
            StopMonitoring();
            _currentProcessCts?.Cancel();
            
            foreach (var item in Queue.Where(q => q.Status != QueueItemStatus.Completed && !IsFailedStatus(q.Status)))
            {
                item.Status = QueueItemStatus.Error;
                item.StatusMessage = "Cancelled by user";
            }
            AddLogEntry("Stopped all processing", ActivityLogLevel.Error);
            UpdateQueueSummary();
        }

        /// <summary>
        /// Main monitoring loop — checks both CRD output folders and RSS feeds on
        /// their respective smart schedules. Runs every 30 seconds so CRD's 1-minute
        /// phase is honoured accurately.
        /// </summary>
        private async Task MonitorShowsAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && IsMonitoring)
            {
                if (!IsPaused)
                {
                    var activeShows = _shows.Where(s => s.IsActive).ToList();
                    var crdShows  = new List<ShowViewModel>();
                    var rssShows  = new List<ShowViewModel>();

                    foreach (var show in activeShows)
                    {
                        if (!ShouldCheckShow(show, out _)) continue;

                        if (show.DownloadMethod == DownloadMethod.CRD)
                            crdShows.Add(show);
                        else if (show.DownloadMethod == DownloadMethod.RSS)
                            rssShows.Add(show);
                    }

                    // ── CRD: scan output folders ──────────────────────────
                    if (crdShows.Count > 0)
                    {
                        AddLogEntry($"[CRD] Scanning {crdShows.Count} output folder(s)...", ActivityLogLevel.Info);
                        foreach (var show in crdShows)
                        {
                            if (cancellationToken.IsCancellationRequested) break;
                            try
                            {
                                var found = await CheckShowViaCRDAsync(show, cancellationToken);
                                UpdateShowMonitoringState(show, found);
                            }
                            catch (Exception ex)
                            {
                                AddLogEntry($"[CRD] Error checking {show.DisplayName}: {ex.Message}", ActivityLogLevel.Error);
                                UpdateShowMonitoringState(show, false);
                            }
                        }
                    }

                    // ── RSS: poll feeds ────────────────────────────────────
                    if (rssShows.Count > 0)
                    {
                        AddLogEntry($"[RSS] Checking {rssShows.Count} feed(s)...", ActivityLogLevel.Info);
                        foreach (var show in rssShows)
                        {
                            if (cancellationToken.IsCancellationRequested) break;
                            try
                            {
                                var found = await CheckShowRssForNewEpisodeAsync(show, cancellationToken);
                                UpdateShowMonitoringState(show, found);
                            }
                            catch (Exception ex)
                            {
                                AddLogEntry($"[RSS] Error checking {show.DisplayName}: {ex.Message}", ActivityLogLevel.Error);
                                UpdateShowMonitoringState(show, false);
                            }
                            await Task.Delay(1000, cancellationToken);
                        }
                    }
                }

                try
                {
                    // Tick every 30 seconds so we can react to CRD's 1-minute phase promptly
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                }
                catch (TaskCanceledException) { break; }
            }
        }

        /// <summary>
        /// Checks whether CRD has downloaded the next expected episode by scanning
        /// the show's configured output folder.
        /// </summary>
        private Task<bool> CheckShowViaCRDAsync(ShowViewModel show, CancellationToken cancellationToken)
        {
            if (_crdService == null) return Task.FromResult(false);

            var releasedEps = show.EpisodesReleased.Select(e => e.EpisodeNumber).ToHashSet();
            var nextEp      = releasedEps.Count > 0 ? releasedEps.Max() + 1 : 1;

            // Reconfigure in case the path changed since service was initialised
            _crdService.Configure(_getSettings().CRD.Path);

            var filePath = _crdService.FindEpisodeFile(show.CrdOutputPath, show.CrdFilePattern, nextEp);
            if (filePath == null) return Task.FromResult(false);

            AddLogEntry($"[CRD] ✅ Found Ep {nextEp} for {show.DisplayName}: {System.IO.Path.GetFileName(filePath)}", ActivityLogLevel.Success);

            var queueItem = new QueueItem
            {
                Show           = show.Model,
                EpisodeNumber  = nextEp,
                Version        = 1,
                Status         = QueueItemStatus.Pending,
                StatusMessage  = "Queued from CRD output",
                SourceFileName = System.IO.Path.GetFileName(filePath),
                SourceFilePath = filePath,
                DownloadSource = DownloadSource.CRD
            };

            Application.Current.Dispatcher.Invoke(() =>
            {
                Queue.Insert(0, queueItem);
                UpdateQueueSummary();
            });

            if (!IsProcessing)
                _ = ProcessQueueAsync();

            return Task.FromResult(true);
        }

        private async Task<bool> CheckShowRssForNewEpisodeAsync(ShowViewModel show, CancellationToken cancellationToken)
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            
            var response = await client.GetStringAsync(show.RssFeed, cancellationToken);
            var doc = XDocument.Parse(response);
            var nyaaNs = XNamespace.Get("https://nyaa.si/xmlns/nyaa");

            var items = doc.Descendants("item").ToList();
            
            // Get the expected next episode number
            var releasedEpisodes = show.EpisodesReleased.Select(e => e.EpisodeNumber).ToHashSet();
            var nextExpectedEpisode = releasedEpisodes.Count > 0 ? releasedEpisodes.Max() + 1 : 1;

            foreach (var item in items)
            {
                var title = item.Element("title")?.Value ?? "";
                
                // Try to extract episode number and version from title
                var (episodeNumber, version) = ExtractEpisodeNumberAndVersion(title, show.CustomEpisodeRegex);
                
                if (episodeNumber.HasValue && episodeNumber.Value == nextExpectedEpisode)
                {
                    // Check if it matches our source group filter (if configured)
                    if (!string.IsNullOrEmpty(show.SourceGroup) && !title.Contains(show.SourceGroup, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    AddLogEntry($"Found episode {episodeNumber}{(version > 1 ? $"v{version}" : "")} for {show.DisplayName}: {title}", ActivityLogLevel.Success);
                    
                    // Create queue item
                    var link = item.Element("link")?.Value ?? "";
                    var infoHash = item.Element(nyaaNs + "infoHash")?.Value ?? "";
                    
                    var queueItem = new QueueItem
                    {
                        Show           = show.Model,
                        EpisodeNumber  = episodeNumber.Value,
                        Version        = version,
                        Status         = QueueItemStatus.Pending,
                        StatusMessage  = "Queued from RSS",
                        TorrentHash    = infoHash,
                        TorrentLink    = link,
                        SourceFileName = title,
                        DownloadSource = DownloadSource.Rss
                    };

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        Queue.Insert(0, queueItem);
                        UpdateQueueSummary();
                    });

                    // Start processing if not already
                    if (!IsProcessing)
                    {
                        _ = ProcessQueueAsync();
                    }

                    return true;
                }
            }

            return false;
        }

        private (int? Episode, int Version) ExtractEpisodeNumberAndVersion(string title, string? customRegex)
        {
            int? episodeNumber = null;
            int version = 1;

            // Try custom regex first
            if (!string.IsNullOrEmpty(customRegex))
            {
                try
                {
                    var match = Regex.Match(title, customRegex);
                    if (match.Success && match.Groups.Count > 1 && int.TryParse(match.Groups[1].Value, out var ep))
                    {
                        episodeNumber = ep;
                    }
                }
                catch { }
            }

            if (episodeNumber == null)
            {
                // Standard patterns for episode number — most-specific first so a show
                // title containing a number (e.g. "Level 999") can't shadow the real episode.
                var patterns = new[]
                {
                    @"S\d+E(\d+)",                               // S01E06  ← unambiguous, try first
                    @"E(\d{2,3})(?:v\d)?(?:[- _\.]|$|\[)",       // E06, E06v2
                    @"Episode\s*(\d+)",                          // Episode 6
                    @"Ep\.?\s*(\d+)",                            // Ep 6, Ep. 6
                    @"#(\d+)",                                   // #06
                    @"[- _](\d{2,3})(?:v\d)?(?:[- _\.]|$|\[)",   // - 06, _06, etc. (last resort)
                };

                foreach (var pattern in patterns)
                {
                    var match = Regex.Match(title, pattern, RegexOptions.IgnoreCase);
                    if (match.Success && int.TryParse(match.Groups[1].Value, out var ep))
                    {
                        episodeNumber = ep;
                        break;
                    }
                }
            }

            // Extract version (v2, v3, etc.) - look for vN pattern near the episode number
            var versionMatch = Regex.Match(title, @"[- _](\d{2,3})v(\d)", RegexOptions.IgnoreCase);
            if (versionMatch.Success && int.TryParse(versionMatch.Groups[2].Value, out var ver))
            {
                version = ver;
                // Also use the episode number from this match if we haven't found one yet
                if (episodeNumber == null && int.TryParse(versionMatch.Groups[1].Value, out var ep))
                {
                    episodeNumber = ep;
                }
            }

            return (episodeNumber, version);
        }

        // Keep backward compatible method
        private int? ExtractEpisodeNumber(string title, string? customRegex)
        {
            return ExtractEpisodeNumberAndVersion(title, customRegex).Episode;
        }

        private void UpdateMonitoringStatus()
        {
            if (!IsMonitoring)
                MonitoringStatus = "Stopped";
            else if (IsPaused)
                MonitoringStatus = "Paused";
            else if (IsProcessing)
                MonitoringStatus = "Processing";
            else
                MonitoringStatus = "Monitoring";
        }

        // ==================== QUEUE PROCESSING ====================

        private async Task ProcessQueueAsync()
        {
            if (IsProcessing) return;
            
            IsProcessing = true;
            _currentProcessCts?.Dispose();
            _currentProcessCts = new CancellationTokenSource();

            try
            {
                while (Queue.Any(q => q.Status == QueueItemStatus.Pending) && !_currentProcessCts.Token.IsCancellationRequested)
                {
                    if (IsPaused)
                    {
                        await Task.Delay(1000);
                        continue;
                    }

                    var nextItem = Queue.FirstOrDefault(q => q.Status == QueueItemStatus.Pending);
                    if (nextItem == null) break;

                    await ProcessQueueItemAsync(nextItem, _currentProcessCts.Token);
                }
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private async Task ProcessQueueItemAsync(QueueItem item, CancellationToken cancellationToken)
        {
            if (_qbitService == null)
                InitializeServices();

            var settings = _getSettings();
            string description = "";

            try
            {
                item.StartedAt = DateTime.Now;

                // ==================== STAGE 1: Acquire Source File ====================
                if (item.DownloadSource == DownloadSource.CRD)
                {
                    // File already on disk — move it to the encoding folder
                    if (string.IsNullOrEmpty(item.SourceFilePath) || !File.Exists(item.SourceFilePath))
                        throw new Exception($"CRD source file not found: '{item.SourceFilePath}'");

                    item.Status        = QueueItemStatus.Downloading;
                    item.StatusMessage = "Moving CRD file to encoding folder...";
                    AddLogEntry($"[CRD] Source: {Path.GetFileName(item.SourceFilePath)}", ActivityLogLevel.Info);

                    var encodingFolder = settings.Folders.EncodingFolder;
                    var destPath       = Path.Combine(encodingFolder, Path.GetFileName(item.SourceFilePath));

                    Directory.CreateDirectory(Path.Combine(encodingFolder, "video"));
                    Directory.CreateDirectory(Path.Combine(encodingFolder, "audio-subs"));
                    Directory.CreateDirectory(Path.Combine(encodingFolder, "data"));
                    Directory.CreateDirectory(Path.Combine(encodingFolder, "done"));

                    if (!string.Equals(Path.GetFullPath(item.SourceFilePath), Path.GetFullPath(destPath), StringComparison.OrdinalIgnoreCase))
                    {
                        if (File.Exists(destPath)) File.Delete(destPath);
                        AddLogEntry($"Moving to encoding folder: {encodingFolder}", ActivityLogLevel.Info);
                        File.Move(item.SourceFilePath, destPath);
                        item.SourceFilePath = destPath;
                        AddLogEntry($"✅ Moved to encoding folder", ActivityLogLevel.Success);
                    }

                    item.SourceFileSizeBytes = new FileInfo(item.SourceFilePath).Length;
                    item.Status        = QueueItemStatus.DownloadComplete;
                    item.StatusMessage = "CRD file ready";
                }
                else
                {
                    // RSS — download via qBittorrent
                    item.Status        = QueueItemStatus.Downloading;
                    item.StatusMessage = "Adding torrent to qBittorrent...";
                    AddLogEntry($"📥 Adding torrent: {item.SourceFileName}", ActivityLogLevel.Info);

                    if (string.IsNullOrEmpty(item.TorrentLink))
                        throw new Exception("No torrent link stored for this RSS queue item — cannot download.");

                    await _discordService?.SendEpisodeGrabbedAsync(item)!;

                    var downloadPath = Path.Combine(settings.Folders.TempFolder, "Downloads");
                    Directory.CreateDirectory(downloadPath);

                    var torrentAdded = await _qbitService!.AddTorrentAsync(item.TorrentLink, downloadPath, isLocal: true);
                    if (!torrentAdded)
                        throw new Exception("Failed to add torrent to qBittorrent");

                    await Task.Delay(2000, cancellationToken);

                    AddLogEntry("Waiting for download to complete...", ActivityLogLevel.Info);
                    var downloadComplete = false;
                    var downloadTimeout  = DateTime.Now.AddMinutes(30);
                    TorrentInfo? torrentInfo = null;

                    while (!downloadComplete && DateTime.Now < downloadTimeout)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        torrentInfo = await _qbitService.GetTorrentInfoAsync(item.TorrentHash, isLocal: true);
                        if (torrentInfo == null)
                        {
                            await Task.Delay(5000, cancellationToken);
                            continue;
                        }
                        item.DownloadProgress = torrentInfo.Progress;
                        item.StatusMessage    = $"Downloading... {torrentInfo.Progress:P0}";
                        if (torrentInfo.Progress >= 1.0)
                            downloadComplete = true;
                        else
                            await Task.Delay(5000, cancellationToken);
                    }

                    if (!downloadComplete || torrentInfo == null)
                        throw new Exception("Download timed out after 30 minutes");

                    await Task.Delay(2000, cancellationToken);

                    // Locate video file
                    string? videoFilePath = null;
                    var contentPath = torrentInfo.ContentPath;
                    if (File.Exists(contentPath))
                    {
                        var ext = Path.GetExtension(contentPath).ToLowerInvariant();
                        if (ext is ".mkv" or ".mp4") videoFilePath = contentPath;
                    }
                    else if (Directory.Exists(contentPath))
                    {
                        videoFilePath = Directory.GetFiles(contentPath, "*.mkv", SearchOption.AllDirectories)
                            .Concat(Directory.GetFiles(contentPath, "*.mp4", SearchOption.AllDirectories))
                            .OrderByDescending(f => new FileInfo(f).Length)
                            .FirstOrDefault();
                    }
                    if (string.IsNullOrEmpty(videoFilePath))
                    {
                        videoFilePath = Directory.GetFiles(downloadPath, "*.mkv", SearchOption.AllDirectories)
                            .Concat(Directory.GetFiles(downloadPath, "*.mp4", SearchOption.AllDirectories))
                            .OrderByDescending(f => new FileInfo(f).Length)
                            .FirstOrDefault();
                    }
                    if (string.IsNullOrEmpty(videoFilePath) || !File.Exists(videoFilePath))
                        throw new Exception($"No video file found after download. Content path: {contentPath}");

                    item.SourceFilePath      = videoFilePath;
                    item.SourceFileSizeBytes = new FileInfo(videoFilePath).Length;
                    item.SourceFileName      = Path.GetFileName(videoFilePath);

                    var groupMatch = Regex.Match(item.SourceFileName, @"\[([^\]]+)\]");
                    if (groupMatch.Success) item.SourceGroup = groupMatch.Groups[1].Value;

                    AddLogEntry($"✅ Downloaded: {item.SourceFileName} ({item.SourceFileSizeFormatted})", ActivityLogLevel.Success);
                    await _discordService?.SendDownloadCompleteAsync(item)!;
                    await _qbitService.DeleteTorrentAsync(item.TorrentHash, deleteFiles: false, isLocal: true);

                    // Move to encoding folder
                    var encodingFolder    = settings.Folders.EncodingFolder;
                    var encodingDestPath  = Path.Combine(encodingFolder, item.SourceFileName);
                    Directory.CreateDirectory(Path.Combine(encodingFolder, "video"));
                    Directory.CreateDirectory(Path.Combine(encodingFolder, "audio-subs"));
                    Directory.CreateDirectory(Path.Combine(encodingFolder, "data"));
                    Directory.CreateDirectory(Path.Combine(encodingFolder, "done"));
                    if (!string.Equals(Path.GetFullPath(item.SourceFilePath), Path.GetFullPath(encodingDestPath), StringComparison.OrdinalIgnoreCase))
                    {
                        if (File.Exists(encodingDestPath)) File.Delete(encodingDestPath);
                        AddLogEntry($"Moving source to encoding folder: {encodingFolder}", ActivityLogLevel.Info);
                        File.Move(item.SourceFilePath, encodingDestPath);
                        item.SourceFilePath = encodingDestPath;
                    }

                    item.Status        = QueueItemStatus.DownloadComplete;
                    item.StatusMessage = "Download complete";
                }

                // ==================== STAGE 2: Analyze Tracks ====================
                item.Status        = QueueItemStatus.AnalyzingTracks;
                item.StatusMessage = "Analyzing audio/subtitle tracks...";
                AddLogEntry($"🔍 Analyzing tracks: {Path.GetFileName(item.SourceFilePath)}", ActivityLogLevel.Info);

                var (audioTracks, subtitleTracks) = await _muxingService!.AnalyzeTracksAsync(item.SourceFilePath);
                item.AudioTracks    = audioTracks;
                item.SubtitleTracks = subtitleTracks;
                AddLogEntry($"Found {audioTracks.Count} audio track(s), {subtitleTracks.Count} subtitle track(s)", ActivityLogLevel.Info);

                // ==================== STAGE 3: Encode ====================
                item.Status        = QueueItemStatus.Encoding;
                item.StatusMessage = item.IsTestRun ? "Encoding (5 minute test)..." : "Encoding full episode...";
                AddLogEntry($"🎬 Starting {(item.IsTestRun ? "5-minute test" : "full episode")} encode...", ActivityLogLevel.Info);

                await _discordService?.SendEncodingStartedAsync(item, item.IsTestRun)!;

                var workerConfigPath = Path.Combine(settings.Folders.EncodingFolder, "WorkerConfig.ini");
                var encodeResult     = await _encodingService!.EncodeAsync(item, workerConfigPath, cancellationToken);
                if (!encodeResult.Success)
                    throw new Exception($"Encoding failed: {encodeResult.ErrorMessage}");

                item.EncodedFilePath = encodeResult.OutputFilePath;
                item.Status          = QueueItemStatus.EncodingComplete;
                item.StatusMessage   = "Encoding complete";
                AddLogEntry($"✅ Encoding complete: {Path.GetFileName(encodeResult.OutputFilePath)} ({encodeResult.FileSizeFormatted})", ActivityLogLevel.Success);
                await _discordService?.SendEncodingCompleteAsync(item, encodeResult.Duration, encodeResult.FileSizeFormatted)!;

                // Cleanup source + LWI files
                AddLogEntry("🧹 Cleaning up source files...", ActivityLogLevel.Info);
                try
                {
                    if (File.Exists(item.SourceFilePath))
                    {
                        File.Delete(item.SourceFilePath);
                        AddLogEntry($"Deleted source: {Path.GetFileName(item.SourceFilePath)}", ActivityLogLevel.Info);
                    }
                    foreach (var lwi in Directory.GetFiles(settings.Folders.EncodingFolder, "*.lwi", SearchOption.TopDirectoryOnly))
                    {
                        File.Delete(lwi);
                        AddLogEntry($"Deleted LWI: {Path.GetFileName(lwi)}", ActivityLogLevel.Info);
                    }
                }
                catch (Exception ex) { AddLogEntry($"⚠️ Cleanup warning: {ex.Message}", ActivityLogLevel.Warning); }

                // ==================== STAGE 4: Mux ====================
                item.Status        = QueueItemStatus.Muxing;
                item.StatusMessage = "Muxing final file...";
                AddLogEntry("🔧 Muxing final file...", ActivityLogLevel.Info);

                var muxOutputPath = Path.Combine(settings.Folders.SeedingFolder, $"{item.OutputFileName}.mkv");
                Directory.CreateDirectory(settings.Folders.SeedingFolder);

                MuxingResult muxResult;
                if (item.IsTestRun)
                {
                    // Quick test: ffmpeg already produced a complete file
                    try
                    {
                        var normEnc = Path.GetFullPath(item.EncodedFilePath).ToLowerInvariant();
                        var normMux = Path.GetFullPath(muxOutputPath).ToLowerInvariant();
                        if (normEnc == normMux)
                        {
                            muxResult = new MuxingResult { Success = true, OutputPath = muxOutputPath, FileSize = new FileInfo(muxOutputPath).Length };
                        }
                        else
                        {
                            if (File.Exists(muxOutputPath)) File.Delete(muxOutputPath);
                            if (Path.GetPathRoot(item.EncodedFilePath) == Path.GetPathRoot(muxOutputPath))
                                File.Move(item.EncodedFilePath, muxOutputPath);
                            else
                                File.Copy(item.EncodedFilePath, muxOutputPath);
                            muxResult = new MuxingResult { Success = true, OutputPath = muxOutputPath, FileSize = new FileInfo(muxOutputPath).Length };
                        }
                        AddLogEntry($"✅ Quick test mux complete: {muxResult.FileSizeFormatted}", ActivityLogLevel.Success);
                    }
                    catch (Exception ex)
                    {
                        muxResult = new MuxingResult { Success = false, ErrorMessage = ex.Message };
                    }
                }
                else
                {
                    // Full encode: remux with proper Judas track naming
                    AddLogEntry("Remuxing with proper track names...", ActivityLogLevel.Info);
                    var (encAudio, encSubs) = await _muxingService!.AnalyzeTracksAsync(item.EncodedFilePath);
                    item.AudioTracks    = encAudio;
                    item.SubtitleTracks = encSubs;
                    muxResult = await _muxingService.RemuxWithTrackNamesAsync(item.EncodedFilePath, muxOutputPath, item, cancellationToken);
                    if (muxResult.Success && item.EncodedFilePath != muxOutputPath)
                    {
                        try { if (File.Exists(item.EncodedFilePath)) File.Delete(item.EncodedFilePath); } catch { }
                    }
                    if (muxResult.Success)
                        AddLogEntry($"✅ Mux complete: {muxResult.FileSizeFormatted}", ActivityLogLevel.Success);
                }

                if (!muxResult.Success)
                    throw new Exception($"Muxing failed: {muxResult.ErrorMessage}");

                item.MuxedFilePath = muxResult.OutputPath;

                // ==================== STAGE 5: Screenshots ====================
                item.Status        = QueueItemStatus.TakingScreenshots;
                item.StatusMessage = "Taking screenshots...";
                AddLogEntry("📸 Taking screenshots...", ActivityLogLevel.Info);

                var screenshotPaths = await _screenshotService!.TakeScreenshotsAsync(item.MuxedFilePath, 3);
                item.ScreenshotPaths = screenshotPaths;
                AddLogEntry($"✅ Captured {screenshotPaths.Count} screenshots", ActivityLogLevel.Success);

                // ==================== STAGE 6: Upload Screenshots ====================
                item.Status        = QueueItemStatus.UploadingScreenshots;
                item.StatusMessage = "Uploading screenshots to ImgBB...";
                AddLogEntry("☁️ Uploading screenshots...", ActivityLogLevel.Info);

                var screenshotUrls = await _screenshotService.UploadScreenshotsAsync(screenshotPaths);
                item.ScreenshotUrls = screenshotUrls;
                AddLogEntry($"✅ Uploaded {screenshotUrls.Count} screenshots", ActivityLogLevel.Success);

                // ==================== STAGE 7: Generate Description ====================
                item.Status        = QueueItemStatus.GeneratingDescription;
                item.StatusMessage = "Generating Nyaa description...";
                AddLogEntry("📝 Generating description...", ActivityLogLevel.Info);

                var templatePath    = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NyaaDescriptionTemplate.txt");
                var altTemplatePath = Path.Combine(settings.Folders.EncodingFolder, "NyaaDescriptionTemplate.txt");
                string template;
                if (File.Exists(templatePath))
                    template = await File.ReadAllTextAsync(templatePath, cancellationToken);
                else if (File.Exists(altTemplatePath))
                    template = await File.ReadAllTextAsync(altTemplatePath, cancellationToken);
                else
                {
                    AddLogEntry("⚠️ Template not found, using default", ActivityLogLevel.Warning);
                    template = "**Title**: @@TITLE@@\r\nHEVC 10bit SoftSubbed - 1920 x 1080\r\nEncoded by: Judas Team\r\n**Source**: @@SOURCE@@\r\n\r\n**Audio**: @@AUDIO_TRACKS@@\r\n**Subtitles**: @@SUBS_TRACKS@@\r\n\r\n[Request an anime or get DDL links @ Discord](@@DISCORD_LINK@@)\r\n\r\n**[If you like this release please seed]**\r\n\r\n@@SCREENSHOTS@@";
                }

                var sourceGroup = string.IsNullOrEmpty(item.SourceGroup)
                    ? (Regex.Match(item.SourceFileName, @"\[([^\]]+)\]") is { Success: true } m ? m.Groups[1].Value : "Unknown")
                    : item.SourceGroup;
                var sourceInfo = $"{sourceGroup} ({item.SourceFileSizeFormatted})";

                description = _nyaaService!.GenerateDescription(item, template, sourceInfo);
                var descPath = Path.Combine(settings.Folders.TempFolder, $"{item.OutputFileName}_description.txt");
                await File.WriteAllTextAsync(descPath, description, cancellationToken);
                item.DescriptionFilePath = descPath;
                AddLogEntry($"✅ Description generated ({description.Length} chars)", ActivityLogLevel.Success);

                // ==================== STAGE 8: Create Torrent ====================
                item.Status        = QueueItemStatus.CreatingTorrent;
                item.StatusMessage = "Creating torrent file...";
                AddLogEntry("🧲 Creating torrent...", ActivityLogLevel.Info);

                var torrentPath   = Path.Combine(settings.Folders.TempFolder, $"{item.OutputFileName}.torrent");
                var torrentResult = await _torrentService!.CreateTorrentAsync(item.MuxedFilePath, torrentPath, $"Encoded by Judas - {settings.Discord.ServerInviteLink}");
                if (!torrentResult.Success)
                    throw new Exception($"Torrent creation failed: {torrentResult.ErrorMessage}");

                item.TorrentFilePath = torrentPath;
                item.TorrentHash     = torrentResult.InfoHash;
                AddLogEntry($"✅ Torrent created: {torrentResult.InfoHash}", ActivityLogLevel.Success);

                // ==================== STAGE 9: Upload to Server ====================
                item.Status        = QueueItemStatus.UploadingEpisode;
                item.StatusMessage = "Uploading to seedbox...";
                AddLogEntry("📤 Uploading to seedbox...", ActivityLogLevel.Info);

                var uploadResult = await _ftpService!.UploadEpisodeAsync(item.MuxedFilePath, Path.GetFileName(item.MuxedFilePath), cancellationToken);
                if (!uploadResult.Success)
                    throw new Exception($"Failed to upload file to seedbox: {uploadResult.ErrorMessage}");

                var torrentUploadResult = await _ftpService.UploadTorrentFileAsync(item.TorrentFilePath, Path.GetFileName(item.TorrentFilePath), cancellationToken);
                if (!torrentUploadResult.Success)
                    AddLogEntry($"⚠️ Torrent file upload failed: {torrentUploadResult.ErrorMessage}", ActivityLogLevel.Warning);

                AddLogEntry("✅ Uploaded to seedbox", ActivityLogLevel.Success);

                // Add torrent to seedbox qBittorrent
                if (File.Exists(item.TorrentFilePath))
                {
                    AddLogEntry("Adding torrent to seedbox qBittorrent...", ActivityLogLevel.Info);
                    var seedboxAdded = await _qbitService!.AddTorrentFileAsync(
                        item.TorrentFilePath,
                        settings.QBittorrent.SeedboxReleasesPath,
                        isLocal: false);
                    AddLogEntry(seedboxAdded
                        ? "✅ Torrent added to seedbox qBittorrent"
                        : "⚠️ Failed to add torrent to seedbox qBittorrent — add it manually",
                        seedboxAdded ? ActivityLogLevel.Success : ActivityLogLevel.Warning);
                }

                // ==================== STAGE 10: Post to Nyaa ====================
                item.Status = QueueItemStatus.PostingToNyaa;
                var isHidden   = item.IsTestRun;   // automation = public; items marked as test = hidden
                var visibility = isHidden ? "HIDDEN" : "public";
                item.StatusMessage = $"Posting to Nyaa ({visibility})...";
                AddLogEntry($"📢 Posting to Nyaa ({visibility})...", ActivityLogLevel.Info);

                var nyaaResult = await _nyaaService.PostToNyaaAsync(item, item.TorrentFilePath, description, isHidden: isHidden);
                if (!nyaaResult.Success)
                    AddLogEntry($"⚠️ Nyaa posting failed: {nyaaResult.Message} (continuing)", ActivityLogLevel.Warning);
                else
                {
                    item.NyaaUrl = nyaaResult.Url;
                    AddLogEntry($"✅ Posted to Nyaa ({visibility}): {nyaaResult.Url}", ActivityLogLevel.Success);
                    await _discordService?.SendNyaaPostedAsync(item, nyaaResult.Url, isHidden)!;
                }

                // ==================== COMPLETE ====================
                item.Status        = QueueItemStatus.Completed;
                item.StatusMessage = "Released!";
                item.CompletedAt   = DateTime.Now;

                // Record the episode in the show's history and persist to disk
                var showId = item.Show.OutputFileTitle ?? item.Show.OutputTorrentTitle;
                var showVm = _shows.FirstOrDefault(s =>
                    (s.Model.OutputFileTitle ?? s.OutputTorrentTitle) == showId);
                if (showVm != null)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                        showVm.AddEpisode(
                            item.EpisodeNumber,
                            item.Version,
                            Path.GetFileName(item.SourceFilePath ?? ""),
                            Path.GetFileName(item.MuxedFilePath ?? item.OutputFileName)));
                    _onSave?.Invoke();
                }

                var duration = item.CompletedAt.Value - item.StartedAt!.Value;
                AddLogEntry($"✅ COMPLETED: {item.OutputFileName} in {duration.TotalMinutes:F1} min", ActivityLogLevel.Success);
                UpdateQueueSummary();
            }
            catch (OperationCanceledException)
            {
                item.Status        = QueueItemStatus.Error;
                item.StatusMessage = "Cancelled";
                item.LastError     = "Processing was cancelled";
                AddLogEntry($"⚠️ Cancelled: {item.OutputFileName}", ActivityLogLevel.Warning);
                UpdateQueueSummary();
            }
            catch (Exception ex)
            {
                item.Status        = QueueItemStatus.Error;
                item.StatusMessage = ex.Message;
                item.LastError     = ex.ToString();
                AddLogEntry($"❌ Failed: {item.OutputFileName} — {ex.Message}", ActivityLogLevel.Error);
                UpdateQueueSummary();
                throw;
            }
        }

        private async Task SimulateStageAsync(QueueItem item, double startProgress, double endProgress, int durationMs, CancellationToken cancellationToken)
        {
            var steps = 20;
            var stepDelay = durationMs / steps;
            var progressStep = (endProgress - startProgress) / steps;

            for (int i = 0; i <= steps; i++)
            {
                if (cancellationToken.IsCancellationRequested) return;
                
                item.EncodeProgress = startProgress + (progressStep * i);
                await Task.Delay(stepDelay, cancellationToken);
            }
        }

        // ==================== QUEUE MANAGEMENT ====================

        private void RetryFailedItem()
        {
            if (SelectedQueueItem == null || !IsFailedStatus(SelectedQueueItem.Status)) return;

            SelectedQueueItem.Status = QueueItemStatus.Pending;
            SelectedQueueItem.StatusMessage = "Queued for retry";
            SelectedQueueItem.LastError = "";
            AddLogEntry($"Retrying: {SelectedQueueItem.OutputFileName}", ActivityLogLevel.Info);
            UpdateQueueSummary();

            if (!IsProcessing)
            {
                _ = ProcessQueueAsync();
            }
        }

        private void RemoveQueueItem()
        {
            if (SelectedQueueItem == null) return;

            var name = SelectedQueueItem.OutputFileName;
            Queue.Remove(SelectedQueueItem);
            SelectedQueueItem = null;
            AddLogEntry($"Removed from queue: {name}", ActivityLogLevel.Info);
            UpdateQueueSummary();
        }

        private void ClearCompletedItems()
        {
            var completed = Queue.Where(q => q.Status == QueueItemStatus.Completed).ToList();
            foreach (var item in completed)
            {
                Queue.Remove(item);
            }
            AddLogEntry($"Cleared {completed.Count} completed items", ActivityLogLevel.Info);
            UpdateQueueSummary();
        }

        private void UpdateQueueSummary()
        {
            OnPropertyChanged(nameof(QueueSummary));
        }

        private static bool IsFailedStatus(QueueItemStatus status)
        {
            return status == QueueItemStatus.Error ||
                   status == QueueItemStatus.DownloadFailed ||
                   status == QueueItemStatus.EncodingFailed ||
                   status == QueueItemStatus.MuxingFailed ||
                   status == QueueItemStatus.UploadFailed;
        }

        // ==================== TEST RUN ====================

        private async Task LoadTestRunRssItemsAsync()
        {
            if (TestRunSelectedShow == null) return;

            TestRunRssItems.Clear();

            // ── CRD: scan output folder for video files ──────────────────────────────
            if (_testRunSourceIndex == 0)
            {
                var folder = TestRunSelectedShow.CrdOutputPath;
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                {
                    TestRunStatus = "CRD output folder is not configured or does not exist.";
                    return;
                }

                TestRunStatus = "Scanning CRD output folder...";

                var videoFiles = Directory.GetFiles(folder, "*.mkv", SearchOption.TopDirectoryOnly)
                    .Concat(Directory.GetFiles(folder, "*.mp4", SearchOption.TopDirectoryOnly))
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .Take(30)
                    .ToList();

                foreach (var file in videoFiles)
                {
                    var info = new FileInfo(file);
                    TestRunRssItems.Add(new RssItem
                    {
                        Title       = info.Name,
                        LocalFilePath = file,
                        PublishDate = info.LastWriteTime,
                        Size        = info.Length
                    });
                }

                TestRunStatus = videoFiles.Count > 0
                    ? $"Found {videoFiles.Count} file(s) in CRD output folder."
                    : "No .mkv / .mp4 files found in the CRD output folder.";

                AddLogEntry($"[CRD] Scanned {folder} — {videoFiles.Count} file(s) for {TestRunSelectedShow.DisplayName}",
                    ActivityLogLevel.Info);
                return;
            }

            // ── RSS: load from the show's RSS feed ───────────────────────────────────
            if (string.IsNullOrEmpty(TestRunSelectedShow.RssFeed))
            {
                TestRunStatus = "No RSS feed configured for this show.";
                return;
            }

            TestRunStatus = "Loading RSS feed...";

            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(15);
                var response = await client.GetStringAsync(TestRunSelectedShow.RssFeed);

                var doc    = XDocument.Parse(response);
                var nyaaNs = XNamespace.Get("https://nyaa.si/xmlns/nyaa");

                foreach (var item in doc.Descendants("item").Take(20))
                {
                    var rssItem = new RssItem
                    {
                        Title      = item.Element("title")?.Value ?? "Unknown",
                        Link       = item.Element("link")?.Value ?? "",
                        TorrentUrl = item.Element("link")?.Value ?? "",
                        InfoHash   = item.Element(nyaaNs + "infoHash")?.Value ?? ""
                    };

                    if (DateTime.TryParse(item.Element("pubDate")?.Value, out var pub))
                        rssItem.PublishDate = pub;

                    if (long.TryParse(item.Element(nyaaNs + "size")?.Value, out var sz))
                        rssItem.Size = sz;

                    TestRunRssItems.Add(rssItem);
                }

                TestRunStatus = $"Loaded {TestRunRssItems.Count} episode(s) from RSS.";
                AddLogEntry($"Loaded {TestRunRssItems.Count} items from {TestRunSelectedShow.DisplayName} RSS",
                    ActivityLogLevel.Info);
            }
            catch (Exception ex)
            {
                TestRunStatus = $"Error loading RSS: {ex.Message}";
                AddLogEntry($"Failed to load RSS: {ex.Message}", ActivityLogLevel.Error);
            }
        }

        private async Task StartTestRunAsync()
        {
            if (TestRunSelectedShow == null || TestRunSelectedEpisode == null) return;

            var show = TestRunSelectedShow;
            var episode = TestRunSelectedEpisode;

            IsTestRunning = true;
            TestRunProgress = 0;
            _testRunCts?.Dispose();
            _testRunCts = new CancellationTokenSource();

            try
            {
                var modeText = IsSimulatedTest ? "SIMULATED" : "REAL";
                TestRunStatus = $"Starting {modeText} test run...";
                AddLogEntry($"🧪 Starting {modeText} TEST RUN: {episode.Title}", ActivityLogLevel.Success);
                
                if (IsSimulatedTest)
                {
                    AddLogEntry("Simulating: Download → Encode 5min → Screenshots → Upload → Post as HIDDEN", ActivityLogLevel.Info);
                    await RunSimulatedTestAsync(show, episode, _testRunCts.Token);
                }
                else
                {
                    AddLogEntry("Real pipeline: Download → Encode 5min → Mux → Screenshots → Upload → Post as HIDDEN", ActivityLogLevel.Info);
                    await RunRealTestAsync(show, episode, _testRunCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                TestRunStatus = "❌ Test run cancelled";
                AddLogEntry("Test run cancelled by user", ActivityLogLevel.Warning);
            }
            catch (Exception ex)
            {
                TestRunStatus = $"❌ Test run failed: {ex.Message}";
                AddLogEntry($"Test run error: {ex.Message}", ActivityLogLevel.Error);
            }
            finally
            {
                IsTestRunning = false;
                _testRunCts?.Dispose();
                _testRunCts = null;
            }
        }

        private void CancelTestRun()
        {
            _testRunCts?.Cancel();
            AddLogEntry("Cancelling test run...", ActivityLogLevel.Warning);
        }

        /// <summary>
        /// Adds the selected episode directly to the main Queue as a full (non-test) release.
        /// Multiple episodes can be queued this way and processed sequentially — the main use
        /// case is catching up on missed episodes without going through the interactive test flow.
        /// </summary>
        private Task QueueManualReleaseAsync()
        {
            if (TestRunSelectedShow == null || TestRunSelectedEpisode == null)
                return Task.CompletedTask;

            var show    = TestRunSelectedShow;
            var episode = TestRunSelectedEpisode;
            var (episodeNumber, version) = ExtractEpisodeNumberAndVersion(episode.Title, show.CustomEpisodeRegex);

            var queueItem = new QueueItem
            {
                Show           = show.Model,
                EpisodeNumber  = episodeNumber ?? 1,
                Version        = version,
                Status         = QueueItemStatus.Pending,
                StatusMessage  = "Manual release — queued",
                IsTestRun      = false,              // full encode + public post
                TorrentHash    = episode.InfoHash,
                TorrentLink    = episode.Link,
                SourceFileName = Path.GetFileName(episode.LocalFilePath.Length > 0
                                     ? episode.LocalFilePath
                                     : episode.Title),
                SourceFilePath = episode.LocalFilePath ?? "",
                DownloadSource = _testRunSourceIndex == 0 ? DownloadSource.CRD : DownloadSource.Rss
            };

            Queue.Add(queueItem);   // append — respects existing queue order
            UpdateQueueSummary();

            TestRunStatus = $"✅ Ep {episodeNumber} queued for release — check the Queue panel.";
            AddLogEntry($"📋 Manual release queued: {queueItem.OutputFileName}", ActivityLogLevel.Success);

            if (!IsProcessing)
                _ = ProcessQueueAsync();

            return Task.CompletedTask;
        }

        private async Task RunSimulatedTestAsync(ShowViewModel show, RssItem episode, CancellationToken ct)
        {
            var (episodeNumber, version) = ExtractEpisodeNumberAndVersion(episode.Title, show.CustomEpisodeRegex);

            var queueItem = new QueueItem
            {
                Show = show.Model,
                EpisodeNumber = episodeNumber ?? 1,
                Version = version,
                Status = QueueItemStatus.Pending,
                StatusMessage = "Queued for simulated test",
                IsTestRun = true,
                TestEncodeDurationSeconds = 300,
                TorrentHash = episode.InfoHash,
                SourceFileName = episode.Title
            };

            Queue.Insert(0, queueItem);
            UpdateQueueSummary();

            // Simulate each stage
            var stages = new[]
            {
                (QueueItemStatus.Downloading, "Downloading torrent...", 0.0, 15.0),
                (QueueItemStatus.DownloadComplete, "Download complete", 15.0, 15.0),
                (QueueItemStatus.AnalyzingTracks, "Analyzing audio/subtitle tracks...", 15.0, 20.0),
                (QueueItemStatus.Encoding, "Encoding (5 min test)...", 20.0, 60.0),
                (QueueItemStatus.EncodingComplete, "Encoding complete", 60.0, 60.0),
                (QueueItemStatus.Muxing, "Muxing final file...", 60.0, 70.0),
                (QueueItemStatus.TakingScreenshots, "Taking screenshots...", 70.0, 78.0),
                (QueueItemStatus.UploadingScreenshots, "Uploading screenshots to ImgBB...", 78.0, 85.0),
                (QueueItemStatus.GeneratingDescription, "Generating Nyaa description...", 85.0, 88.0),
                (QueueItemStatus.CreatingTorrent, "Creating torrent file...", 88.0, 92.0),
                (QueueItemStatus.UploadingEpisode, "Uploading to seedbox...", 92.0, 97.0),
                (QueueItemStatus.PostingToNyaa, "Posting to Nyaa (HIDDEN)...", 97.0, 100.0),
            };

            queueItem.StartedAt = DateTime.Now;

            foreach (var (status, message, startProg, endProg) in stages)
            {
                ct.ThrowIfCancellationRequested();
                
                queueItem.Status = status;
                queueItem.StatusMessage = message;
                TestRunStatus = message;
                AddLogEntry($"[SIM] {message}", ActivityLogLevel.Info);

                // Animate progress
                var progRange = endProg - startProg;
                for (int i = 0; i <= 10; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    TestRunProgress = startProg + (progRange * i / 10);
                    queueItem.EncodeProgress = TestRunProgress / 100;
                    await Task.Delay(100, ct);
                }
            }

            queueItem.Status = QueueItemStatus.Completed;
            queueItem.StatusMessage = "Simulated test completed!";
            queueItem.CompletedAt = DateTime.Now;
            TestRunProgress = 100;
            
            var duration = queueItem.CompletedAt.Value - queueItem.StartedAt!.Value;
            TestRunStatus = $"✅ Simulated test completed in {duration.TotalSeconds:F0}s";
            AddLogEntry($"✅ Simulated test completed in {duration.TotalSeconds:F0}s", ActivityLogLevel.Success);
            UpdateQueueSummary();
        }

        private async Task RunRealTestAsync(ShowViewModel show, RssItem episode, CancellationToken ct)
        {
            // Initialize services if not already done
            if (_qbitService == null)
            {
                InitializeServices();
            }

            var settings = _getSettings();
            var (episodeNumber, version) = ExtractEpisodeNumberAndVersion(episode.Title, show.CustomEpisodeRegex);
            
            AddLogEntry($"Extracted from '{episode.Title}': Episode {episodeNumber ?? 0}, Version {version}", ActivityLogLevel.Info);

            // IsTestRun controls whether EncodingService uses 5-min ffmpeg (true) or full PowerShell (false)
            var queueItem = new QueueItem
            {
                Show = show.Model,
                EpisodeNumber = episodeNumber ?? 1,
                Version = version,
                Status = QueueItemStatus.Pending,
                StatusMessage = IsQuickEncode ? "Queued for quick test" : "Queued for full encode",
                IsTestRun = IsQuickEncode,  // Quick = 5-min ffmpeg, Full = PowerShell script
                TestEncodeDurationSeconds = 300,
                TorrentHash = episode.InfoHash,
                SourceFileName = episode.Title
            };

            Queue.Insert(0, queueItem);
            UpdateQueueSummary();
            queueItem.StartedAt = DateTime.Now;

            // Log the run configuration
            var encodeType = IsQuickEncode ? "Quick (5-min)" : "Full episode";
            var postVisibility = IsHiddenPost ? "HIDDEN" : "PUBLIC";
            AddLogEntry($"🚀 Starting {encodeType} encode, will post as {postVisibility}", ActivityLogLevel.Info);

            try
            {
                // ==================== STAGE 1: Acquire Source File ====================
                // For CRD shows the file is already on disk — skip the torrent download.
                if (episode.IsLocalFile)
                {
                    AddLogEntry($"[CRD] Using local file: {Path.GetFileName(episode.LocalFilePath)}", ActivityLogLevel.Success);
                    queueItem.SourceFilePath    = episode.LocalFilePath;
                    queueItem.SourceFileName    = Path.GetFileName(episode.LocalFilePath);
                    queueItem.SourceFileSizeBytes = new FileInfo(episode.LocalFilePath).Length;
                    queueItem.Status            = QueueItemStatus.DownloadComplete;
                    queueItem.StatusMessage     = "CRD file ready";
                    TestRunStatus               = "[CRD] Source file ready — skipping download.";
                    TestRunProgress             = 15;

                    // Move to encoding folder so the encode scripts can find it.
                    var settings2         = _getSettings();
                    var encodingFolder2   = settings2.Folders.EncodingFolder;
                    var srcFileName2      = Path.GetFileName(queueItem.SourceFilePath);
                    var destPath2         = Path.Combine(encodingFolder2, srcFileName2);
                    Directory.CreateDirectory(Path.Combine(encodingFolder2, "video"));
                    Directory.CreateDirectory(Path.Combine(encodingFolder2, "audio-subs"));
                    Directory.CreateDirectory(Path.Combine(encodingFolder2, "data"));
                    Directory.CreateDirectory(Path.Combine(encodingFolder2, "done"));
                    if (queueItem.SourceFilePath != destPath2)
                    {
                        if (File.Exists(destPath2)) File.Delete(destPath2);
                        File.Move(queueItem.SourceFilePath, destPath2);
                        queueItem.SourceFilePath = destPath2;
                        AddLogEntry($"Moved to encoding folder: {encodingFolder2}", ActivityLogLevel.Info);
                    }
                }
                else
                {
                // ── RSS: download via qBittorrent ────────────────────────────────────
                queueItem.Status = QueueItemStatus.Downloading;
                queueItem.StatusMessage = "Adding torrent to qBittorrent...";
                TestRunStatus = "Downloading torrent...";
                TestRunProgress = 0;
                AddLogEntry($"📥 Adding torrent: {episode.Title}", ActivityLogLevel.Info);

                // Send Discord notification - Episode grabbed
                await _discordService?.SendEpisodeGrabbedAsync(queueItem)!;

                var downloadPath = Path.Combine(settings.Folders.TempFolder, "Downloads");
                Directory.CreateDirectory(downloadPath);

                // Add torrent to local qBittorrent
                var torrentAdded = await _qbitService!.AddTorrentAsync(episode.Link, downloadPath, isLocal: true);
                if (!torrentAdded)
                {
                    throw new Exception("Failed to add torrent to qBittorrent");
                }

                // Wait a moment for qBittorrent to process the torrent
                await Task.Delay(2000, ct);

                // Wait for download to complete (poll every 5 seconds)
                AddLogEntry("Waiting for download to complete...", ActivityLogLevel.Info);
                var downloadComplete = false;
                var downloadTimeout = DateTime.Now.AddMinutes(30);
                TorrentInfo? torrentInfo = null;
                
                while (!downloadComplete && DateTime.Now < downloadTimeout)
                {
                    ct.ThrowIfCancellationRequested();
                    
                    torrentInfo = await _qbitService.GetTorrentInfoAsync(episode.InfoHash, isLocal: true);
                    if (torrentInfo == null)
                    {
                        AddLogEntry($"⚠️ Torrent not found by hash, waiting...", ActivityLogLevel.Warning);
                        await Task.Delay(5000, ct);
                        continue;
                    }
                    
                    var progress = torrentInfo.Progress;
                    TestRunProgress = progress * 15; // 0-15%
                    queueItem.DownloadProgress = progress;
                    queueItem.StatusMessage = $"Downloading... {progress:P0}";
                    
                    if (progress >= 1.0)
                    {
                        downloadComplete = true;
                        AddLogEntry($"Download complete. Content path: {torrentInfo.ContentPath}", ActivityLogLevel.Info);
                    }
                    else
                    {
                        await Task.Delay(5000, ct);
                    }
                }

                if (!downloadComplete || torrentInfo == null)
                {
                    throw new Exception("Download timed out after 30 minutes");
                }

                // Wait a moment for files to be fully written to disk
                await Task.Delay(2000, ct);

                // Use the content path from qBittorrent
                string? videoFilePath = null;
                var contentPath = torrentInfo.ContentPath;
                
                if (File.Exists(contentPath))
                {
                    // Content path is a single file
                    var ext = Path.GetExtension(contentPath).ToLowerInvariant();
                    if (ext == ".mkv" || ext == ".mp4")
                    {
                        videoFilePath = contentPath;
                    }
                }
                else if (Directory.Exists(contentPath))
                {
                    // Content path is a folder - search for video files
                    var videoFiles = Directory.GetFiles(contentPath, "*.mkv", SearchOption.AllDirectories)
                        .Concat(Directory.GetFiles(contentPath, "*.mp4", SearchOption.AllDirectories))
                        .OrderByDescending(f => new FileInfo(f).Length)
                        .ToList();
                    
                    if (videoFiles.Count > 0)
                    {
                        videoFilePath = videoFiles.First();
                    }
                }

                // Fallback: search in download path
                if (string.IsNullOrEmpty(videoFilePath))
                {
                    AddLogEntry($"Content path didn't contain video, searching in: {downloadPath}", ActivityLogLevel.Warning);
                    var downloadedFiles = Directory.GetFiles(downloadPath, "*.mkv", SearchOption.AllDirectories)
                        .Concat(Directory.GetFiles(downloadPath, "*.mp4", SearchOption.AllDirectories))
                        .OrderByDescending(f => new FileInfo(f).Length)
                        .ToList();
                    
                    if (downloadedFiles.Count > 0)
                    {
                        videoFilePath = downloadedFiles.First();
                    }
                }

                if (string.IsNullOrEmpty(videoFilePath) || !File.Exists(videoFilePath))
                {
                    throw new Exception($"No video file found after download. Content path: {contentPath}, Download path: {downloadPath}");
                }

                queueItem.SourceFilePath = videoFilePath;
                queueItem.SourceFileSizeBytes = new FileInfo(queueItem.SourceFilePath).Length;
                
                // Extract source group from filename (e.g., [SubsPlease] -> SubsPlease)
                var sourceFileName = Path.GetFileName(queueItem.SourceFilePath);
                var groupMatch = System.Text.RegularExpressions.Regex.Match(sourceFileName, @"\[([^\]]+)\]");
                if (groupMatch.Success)
                {
                    queueItem.SourceGroup = groupMatch.Groups[1].Value;
                    AddLogEntry($"Source group: {queueItem.SourceGroup}", ActivityLogLevel.Info);
                }
                
                AddLogEntry($"✅ Downloaded: {sourceFileName} ({queueItem.SourceFileSizeFormatted})", ActivityLogLevel.Success);
                
                // Send Discord notification - Download complete
                await _discordService?.SendDownloadCompleteAsync(queueItem)!;
                
                // Remove torrent from qBittorrent (keep files)
                AddLogEntry("Removing torrent from qBittorrent...", ActivityLogLevel.Info);
                await _qbitService.DeleteTorrentAsync(episode.InfoHash, deleteFiles: false, isLocal: true);
                
                // Move source file to encoding folder for the PowerShell script
                var encodingFolder = settings.Folders.EncodingFolder;
                var encodingSourcePath = Path.Combine(encodingFolder, sourceFileName);
                
                // Create encoding subdirectories if they don't exist
                Directory.CreateDirectory(Path.Combine(encodingFolder, "video"));
                Directory.CreateDirectory(Path.Combine(encodingFolder, "audio-subs"));
                Directory.CreateDirectory(Path.Combine(encodingFolder, "data"));
                Directory.CreateDirectory(Path.Combine(encodingFolder, "done"));
                
                if (queueItem.SourceFilePath != encodingSourcePath)
                {
                    AddLogEntry($"Moving source to encoding folder: {encodingFolder}", ActivityLogLevel.Info);
                    
                    // Delete existing file if present
                    if (File.Exists(encodingSourcePath))
                    {
                        File.Delete(encodingSourcePath);
                    }
                    
                    // Move the file
                    File.Move(queueItem.SourceFilePath, encodingSourcePath);
                    queueItem.SourceFilePath = encodingSourcePath;
                    AddLogEntry($"Source file moved to: {encodingSourcePath}", ActivityLogLevel.Info);
                }
                
                queueItem.Status = QueueItemStatus.DownloadComplete;
                TestRunProgress = 15;
                } // end else (torrent download path)

                // ==================== STAGE 2: Analyze Tracks ====================
                queueItem.Status = QueueItemStatus.AnalyzingTracks;
                queueItem.StatusMessage = "Analyzing audio/subtitle tracks...";
                TestRunStatus = "Analyzing tracks...";
                TestRunProgress = 16;
                AddLogEntry($"🔍 Analyzing tracks in: {Path.GetFileName(queueItem.SourceFilePath)}", ActivityLogLevel.Info);

                var (audioTracks, subtitleTracks) = await _muxingService!.AnalyzeTracksAsync(queueItem.SourceFilePath);
                queueItem.AudioTracks = audioTracks;
                queueItem.SubtitleTracks = subtitleTracks;
                
                AddLogEntry($"Found {audioTracks.Count} audio track(s), {subtitleTracks.Count} subtitle track(s)", ActivityLogLevel.Info);
                TestRunProgress = 20;

                // ==================== STAGE 3: Encode ====================
                queueItem.Status = QueueItemStatus.Encoding;
                if (IsQuickEncode)
                {
                    queueItem.StatusMessage = "Encoding (5 minute test)...";
                    TestRunStatus = "Encoding (5 min)...";
                    AddLogEntry($"🎬 Starting 5-minute test encode...", ActivityLogLevel.Info);
                }
                else
                {
                    queueItem.StatusMessage = "Encoding full episode...";
                    TestRunStatus = "Encoding (full)...";
                    AddLogEntry($"🎬 Starting full episode encode (this may take a while)...", ActivityLogLevel.Info);
                }

                // Send Discord notification - Encode started
                await _discordService?.SendEncodingStartedAsync(queueItem, IsQuickEncode)!;

                var workerConfigPath = Path.Combine(settings.Folders.EncodingFolder, "WorkerConfig.ini");
                var encodeResult = await _encodingService!.EncodeAsync(queueItem, workerConfigPath, ct);

                if (!encodeResult.Success)
                {
                    throw new Exception($"Encoding failed: {encodeResult.ErrorMessage}");
                }

                queueItem.EncodedFilePath = encodeResult.OutputFilePath;
                AddLogEntry($"✅ Encoding complete: {Path.GetFileName(encodeResult.OutputFilePath)} ({encodeResult.FileSizeFormatted})", ActivityLogLevel.Success);
                queueItem.Status = QueueItemStatus.EncodingComplete;
                TestRunProgress = 60;

                // Send Discord notification - Encode complete
                var encodeDuration = encodeResult.Duration;
                await _discordService?.SendEncodingCompleteAsync(queueItem, encodeDuration, encodeResult.FileSizeFormatted)!;

                // ==================== CLEANUP: Remove source file and LWI files ====================
                AddLogEntry("🧹 Cleaning up source files...", ActivityLogLevel.Info);
                try
                {
                    // Delete source file from encoding folder
                    if (File.Exists(queueItem.SourceFilePath))
                    {
                        File.Delete(queueItem.SourceFilePath);
                        AddLogEntry($"Deleted source: {Path.GetFileName(queueItem.SourceFilePath)}", ActivityLogLevel.Info);
                    }
                    
                    // Delete any LWI files in encoding folder
                    var lwiFiles = Directory.GetFiles(settings.Folders.EncodingFolder, "*.lwi", SearchOption.TopDirectoryOnly);
                    foreach (var lwi in lwiFiles)
                    {
                        File.Delete(lwi);
                        AddLogEntry($"Deleted LWI: {Path.GetFileName(lwi)}", ActivityLogLevel.Info);
                    }
                }
                catch (Exception ex)
                {
                    AddLogEntry($"⚠️ Cleanup warning: {ex.Message}", ActivityLogLevel.Warning);
                }

                // ==================== STAGE 4: Mux ====================
                queueItem.Status = QueueItemStatus.Muxing;
                queueItem.StatusMessage = "Muxing final file...";
                TestRunStatus = "Muxing...";
                AddLogEntry($"🔧 Muxing final file...", ActivityLogLevel.Info);

                var muxOutputPath = Path.Combine(settings.Folders.SeedingFolder, $"{queueItem.OutputFileName}.mkv");
                
                // Ensure output directory exists
                Directory.CreateDirectory(settings.Folders.SeedingFolder);
                
                MuxingResult muxResult;
                
                if (queueItem.IsTestRun)
                {
                    // Quick test (5-min ffmpeg): output file already contains video + audio + subs
                    // Just need to ensure it's in the right place
                    AddLogEntry("Quick test: File already muxed by ffmpeg", ActivityLogLevel.Info);
                    
                    try
                    {
                        // Check if encoded file is already at the target path
                        var encodedPath = queueItem.EncodedFilePath;
                        var normalizedEncodedPath = Path.GetFullPath(encodedPath).ToLowerInvariant();
                        var normalizedMuxPath = Path.GetFullPath(muxOutputPath).ToLowerInvariant();
                        
                        if (normalizedEncodedPath == normalizedMuxPath)
                        {
                            // File is already in the right place
                            AddLogEntry($"Encoded file already at output path", ActivityLogLevel.Info);
                            muxResult = new MuxingResult
                            {
                                Success = true,
                                OutputPath = muxOutputPath,
                                FileSize = new FileInfo(muxOutputPath).Length
                            };
                        }
                        else
                        {
                            // Need to move/copy the file
                            if (File.Exists(muxOutputPath))
                            {
                                File.Delete(muxOutputPath);
                            }
                            
                            // Use Move if on same drive, otherwise Copy
                            if (Path.GetPathRoot(encodedPath) == Path.GetPathRoot(muxOutputPath))
                            {
                                File.Move(encodedPath, muxOutputPath);
                            }
                            else
                            {
                                File.Copy(encodedPath, muxOutputPath);
                            }
                            
                            muxResult = new MuxingResult
                            {
                                Success = true,
                                OutputPath = muxOutputPath,
                                FileSize = new FileInfo(muxOutputPath).Length
                            };
                        }
                        AddLogEntry($"✅ Quick test mux complete: {muxResult.FileSizeFormatted}", ActivityLogLevel.Success);
                    }
                    catch (Exception ex)
                    {
                        muxResult = new MuxingResult
                        {
                            Success = false,
                            ErrorMessage = ex.Message
                        };
                    }
                }
                else
                {
                    // Full encode: PowerShell script muxed but without proper track names
                    // We need to remux to apply Judas track naming conventions
                    AddLogEntry("Full encode: Remuxing with proper track names...", ActivityLogLevel.Info);
                    
                    // Analyze the encoded file to get track info
                    var (encodedAudioTracks, encodedSubtitleTracks) = await _muxingService!.AnalyzeTracksAsync(queueItem.EncodedFilePath);
                    queueItem.AudioTracks = encodedAudioTracks;
                    queueItem.SubtitleTracks = encodedSubtitleTracks;
                    AddLogEntry($"Found {encodedAudioTracks.Count} audio, {encodedSubtitleTracks.Count} subtitle tracks for naming", ActivityLogLevel.Info);
                    
                    // Do a proper remux with track naming
                    // For full encode, the video/audio/subs are all in the same file from PowerShell
                    muxResult = await _muxingService.RemuxWithTrackNamesAsync(
                        queueItem.EncodedFilePath,
                        muxOutputPath,
                        queueItem,
                        ct);
                    
                    // If remux succeeded and output is different from input, clean up the PS output
                    if (muxResult.Success && queueItem.EncodedFilePath != muxOutputPath)
                    {
                        try
                        {
                            if (File.Exists(queueItem.EncodedFilePath))
                            {
                                File.Delete(queueItem.EncodedFilePath);
                                AddLogEntry($"Cleaned up intermediate file", ActivityLogLevel.Info);
                            }
                        }
                        catch { }
                    }
                    
                    if (muxResult.Success)
                    {
                        AddLogEntry($"✅ Full encode mux complete: {muxResult.FileSizeFormatted}", ActivityLogLevel.Success);
                    }
                }
                
                if (!muxResult.Success)
                {
                    throw new Exception($"Muxing failed: {muxResult.ErrorMessage}");
                }

                queueItem.MuxedFilePath = muxResult.OutputPath;
                AddLogEntry($"✅ Muxed: {Path.GetFileName(muxResult.OutputPath)}", ActivityLogLevel.Success);
                TestRunProgress = 70;

                // ==================== STAGE 5: Screenshots ====================
                queueItem.Status = QueueItemStatus.TakingScreenshots;
                queueItem.StatusMessage = "Taking screenshots...";
                TestRunStatus = "Taking screenshots...";
                AddLogEntry($"📸 Taking screenshots...", ActivityLogLevel.Info);

                var screenshotPaths = await _screenshotService!.TakeScreenshotsAsync(queueItem.MuxedFilePath, 3);
                queueItem.ScreenshotPaths = screenshotPaths;
                AddLogEntry($"✅ Captured {screenshotPaths.Count} screenshots", ActivityLogLevel.Success);
                TestRunProgress = 78;

                // ==================== STAGE 6: Upload Screenshots ====================
                queueItem.Status = QueueItemStatus.UploadingScreenshots;
                queueItem.StatusMessage = "Uploading screenshots...";
                TestRunStatus = "Uploading screenshots...";
                AddLogEntry($"☁️ Uploading screenshots to ImgBB...", ActivityLogLevel.Info);

                var screenshotUrls = await _screenshotService.UploadScreenshotsAsync(screenshotPaths);
                queueItem.ScreenshotUrls = screenshotUrls;
                AddLogEntry($"✅ Uploaded {screenshotUrls.Count} screenshots", ActivityLogLevel.Success);
                TestRunProgress = 85;

                // ==================== STAGE 7: Generate Description ====================
                queueItem.Status = QueueItemStatus.GeneratingDescription;
                queueItem.StatusMessage = "Generating description...";
                TestRunStatus = "Generating description...";
                AddLogEntry($"📝 Generating Nyaa description...", ActivityLogLevel.Info);

                // Load description template - try multiple locations
                var templatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NyaaDescriptionTemplate.txt");
                var altTemplatePath = Path.Combine(settings.Folders.EncodingFolder, "NyaaDescriptionTemplate.txt");
                
                string template = "";
                if (File.Exists(templatePath))
                {
                    template = await File.ReadAllTextAsync(templatePath, ct);
                    AddLogEntry($"Loaded template from: {templatePath}", ActivityLogLevel.Info);
                }
                else if (File.Exists(altTemplatePath))
                {
                    template = await File.ReadAllTextAsync(altTemplatePath, ct);
                    AddLogEntry($"Loaded template from: {altTemplatePath}", ActivityLogLevel.Info);
                }
                else
                {
                    // Use default template
                    AddLogEntry($"⚠️ Template not found, using default", ActivityLogLevel.Warning);
                    template = @"**Title**: @@TITLE@@
HEVC 10bit SoftSubbed - 1920 x 1080
Encoded by: Judas Team
**Source**: @@SOURCE@@

**Audio**: @@AUDIO_TRACKS@@
**Subtitles**: @@SUBS_TRACKS@@

[Request an anime or get DDL links @ Discord](@@DISCORD_LINK@@)

**[If you like this release please seed]**

@@SCREENSHOTS@@";
                }
                
                // Build source info - try to extract from source filename if SourceGroup is empty
                var sourceGroup = queueItem.SourceGroup;
                if (string.IsNullOrEmpty(sourceGroup))
                {
                    // Try to extract group from source filename [Group] format
                    var sourceGroupMatch = System.Text.RegularExpressions.Regex.Match(queueItem.SourceFileName, @"\[([^\]]+)\]");
                    if (sourceGroupMatch.Success)
                    {
                        sourceGroup = sourceGroupMatch.Groups[1].Value;
                    }
                    else
                    {
                        sourceGroup = "Unknown";
                    }
                }
                var sourceInfo = $"{sourceGroup} ({queueItem.SourceFileSizeFormatted})";
                AddLogEntry($"Source info: {sourceInfo}", ActivityLogLevel.Info);
                
                var description = _nyaaService!.GenerateDescription(queueItem, template, sourceInfo);
                
                if (string.IsNullOrWhiteSpace(description))
                {
                    AddLogEntry($"⚠️ Generated description is empty!", ActivityLogLevel.Warning);
                }
                else
                {
                    AddLogEntry($"Description length: {description.Length} chars", ActivityLogLevel.Info);
                }
                
                var descPath = Path.Combine(settings.Folders.TempFolder, $"{queueItem.OutputFileName}_description.txt");
                await File.WriteAllTextAsync(descPath, description, ct);
                queueItem.DescriptionFilePath = descPath;
                AddLogEntry($"✅ Description generated and saved", ActivityLogLevel.Success);
                TestRunProgress = 88;

                // ==================== STAGE 8: Create Torrent ====================
                queueItem.Status = QueueItemStatus.CreatingTorrent;
                queueItem.StatusMessage = "Creating torrent file...";
                TestRunStatus = "Creating torrent...";
                AddLogEntry($"🧲 Creating torrent file...", ActivityLogLevel.Info);

                var torrentPath = Path.Combine(settings.Folders.TempFolder, $"{queueItem.OutputFileName}.torrent");
                var torrentResult = await _torrentService!.CreateTorrentAsync(queueItem.MuxedFilePath, torrentPath, $"Encoded by Judas - {settings.Discord.ServerInviteLink}");
                
                if (!torrentResult.Success)
                {
                    throw new Exception($"Torrent creation failed: {torrentResult.ErrorMessage}");
                }

                queueItem.TorrentFilePath = torrentPath;
                queueItem.TorrentHash = torrentResult.InfoHash;
                AddLogEntry($"✅ Torrent created: {torrentResult.InfoHash}", ActivityLogLevel.Success);
                TestRunProgress = 92;

                // ==================== STAGE 9: Upload to Server ====================
                queueItem.Status = QueueItemStatus.UploadingEpisode;
                queueItem.StatusMessage = "Uploading to seedbox...";
                TestRunStatus = "Uploading to seedbox...";
                AddLogEntry($"📤 Uploading to seedbox...", ActivityLogLevel.Info);

                var uploadResult = await _ftpService!.UploadEpisodeAsync(queueItem.MuxedFilePath, Path.GetFileName(queueItem.MuxedFilePath), ct);
                if (!uploadResult.Success)
                {
                    throw new Exception($"Failed to upload file to seedbox: {uploadResult.ErrorMessage}");
                }

                // Upload torrent file
                var torrentUploadResult = await _ftpService.UploadTorrentFileAsync(queueItem.TorrentFilePath, Path.GetFileName(queueItem.TorrentFilePath), ct);
                if (!torrentUploadResult.Success)
                {
                    AddLogEntry($"⚠️ Failed to upload torrent file: {torrentUploadResult.ErrorMessage}", ActivityLogLevel.Warning);
                }
                AddLogEntry($"✅ Uploaded to seedbox", ActivityLogLevel.Success);

                // Add torrent to seedbox qBittorrent
                if (File.Exists(queueItem.TorrentFilePath))
                {
                    AddLogEntry("Adding torrent to seedbox qBittorrent...", ActivityLogLevel.Info);
                    var settings2 = _getSettings();
                    var seedboxAdded = await _qbitService!.AddTorrentFileAsync(
                        queueItem.TorrentFilePath,
                        settings2.QBittorrent.SeedboxReleasesPath,
                        isLocal: false);
                    AddLogEntry(seedboxAdded
                        ? "✅ Torrent added to seedbox qBittorrent"
                        : "⚠️ Failed to add torrent to seedbox qBittorrent — add it manually",
                        seedboxAdded ? ActivityLogLevel.Success : ActivityLogLevel.Warning);
                }
                TestRunProgress = 97;

                // ==================== STAGE 10: Post to Nyaa ====================
                queueItem.Status = QueueItemStatus.PostingToNyaa;
                var visibilityText = IsHiddenPost ? "HIDDEN" : "PUBLIC";
                queueItem.StatusMessage = $"Posting to Nyaa ({visibilityText})...";
                TestRunStatus = $"Posting to Nyaa ({visibilityText})...";
                AddLogEntry($"📢 Posting to Nyaa as {visibilityText}...", ActivityLogLevel.Info);

                var nyaaResult = await _nyaaService.PostToNyaaAsync(queueItem, queueItem.TorrentFilePath, description, isHidden: IsHiddenPost);
                
                if (!nyaaResult.Success)
                {
                    AddLogEntry($"⚠️ Nyaa posting failed: {nyaaResult.Message} (continuing anyway)", ActivityLogLevel.Warning);
                }
                else
                {
                    queueItem.NyaaUrl = nyaaResult.Url;
                    AddLogEntry($"✅ Posted to Nyaa ({visibilityText}): {nyaaResult.Url}", ActivityLogLevel.Success);
                    
                    // Send Discord notification - Torrent posted
                    await _discordService?.SendNyaaPostedAsync(queueItem, nyaaResult.Url, IsHiddenPost)!;
                }
                TestRunProgress = 100;

                // ==================== COMPLETE ====================
                queueItem.Status = QueueItemStatus.Completed;
                var encodeTypeDesc = IsQuickEncode ? "Quick test" : "Full encode";
                queueItem.StatusMessage = $"{encodeTypeDesc} completed! ({visibilityText})";
                queueItem.CompletedAt = DateTime.Now;

                var totalDuration = queueItem.CompletedAt.Value - queueItem.StartedAt!.Value;
                TestRunStatus = $"✅ {encodeTypeDesc} completed in {totalDuration.TotalMinutes:F1} minutes";
                AddLogEntry($"✅ {encodeTypeDesc.ToUpper()} COMPLETED in {totalDuration.TotalMinutes:F1} minutes ({visibilityText})", ActivityLogLevel.Success);
            }
            catch (Exception ex)
            {
                queueItem.Status = QueueItemStatus.Error;
                queueItem.StatusMessage = ex.Message;
                queueItem.LastError = ex.ToString();
                throw;
            }
            finally
            {
                UpdateQueueSummary();
            }
        }

        // ==================== ACTIVITY LOG ====================

        private void AddLogEntry(string message, ActivityLogLevel level)
        {
            var timestamp = DateTime.Now;

            Application.Current.Dispatcher.Invoke(() =>
            {
                ActivityLog.Insert(0, new ActivityLogEntry
                {
                    Timestamp = timestamp,
                    Message = message,
                    Level = level
                });

                // Keep only last 200 entries
                while (ActivityLog.Count > 200)
                {
                    ActivityLog.RemoveAt(ActivityLog.Count - 1);
                }
            });

            // Append to daily log file — fire-and-forget, never crashes the app
            _ = Task.Run(() =>
            {
                try
                {
                    Directory.CreateDirectory(_logsFolder);
                    var logFile = Path.Combine(_logsFolder, $"jem-{timestamp:yyyy-MM-dd}.log");
                    var line = $"[{timestamp:HH:mm:ss}] [{level,-7}] {message}{Environment.NewLine}";
                    File.AppendAllText(logFile, line);
                }
                catch { /* never let file I/O break the app */ }
            });
        }

        private void ClearActivityLog()
        {
            ActivityLog.Clear();
            AddLogEntry("Activity log cleared", ActivityLogLevel.Info);
        }

        // ==================== COMMAND NOTIFICATION ====================

        private void NotifyCommandsChanged()
        {
            ((RelayCommand)StartMonitoringCommand).NotifyCanExecuteChanged();
            ((RelayCommand)StopMonitoringCommand).NotifyCanExecuteChanged();
            ((RelayCommand)PauseQueueCommand).NotifyCanExecuteChanged();
            ((RelayCommand)ResumeQueueCommand).NotifyCanExecuteChanged();
            ((RelayCommand)CancelCurrentProcessCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)StartTestRunCommand).NotifyCanExecuteChanged();
            ((RelayCommand)CancelTestRunCommand).NotifyCanExecuteChanged();
        }
    }
}
