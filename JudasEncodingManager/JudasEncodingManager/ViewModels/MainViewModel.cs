using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.Input;
using JudasEncodingManager.Models;
using JudasEncodingManager.Services;
using Newtonsoft.Json;

namespace JudasEncodingManager.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler? QBitRefreshRequested;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private AppSettings _settings = new();
        private string _currentFilePath = "";
        private bool _hasUnsavedChanges;
        private string _statusMessage = "Ready";
        private bool _isTestMode;
        private int _selectedTabIndex;
        private ShowViewModel? _selectedShow;
        private string _manualEpisodeNumber = "";
        private string _rssTestResult = "";
        private bool _isTestingRss;

        // qBittorrent
        private bool _isLocalQBitSelected = true;
        private bool _isSeedboxQBitSelected;
        private string _qBitConnectionStatus = "Not Connected";
        private bool _isQBitLoading;

        // Color Scheme
        private ColorSchemeDefinition? _selectedColorScheme;

        // Trackers
        private TrackerViewModel? _selectedTracker;
        private TrackerViewModel? _editingTracker;
        private bool _isEditingTracker;
        private string _editingTrackerCredentials = "";
        private bool _isAnidexEnabled = true;

        // Folders
        private string _tempFolderSize = "";

        // Store command references for CanExecute updates
        private RelayCommand? _saveFileCommand;
        private RelayCommand? _removeShowCommand;
        private RelayCommand? _duplicateShowCommand;
        private RelayCommand? _addManualEpisodeCommand;
        private RelayCommand? _removeLastEpisodeCommand;
        private RelayCommand? _clearEpisodeHistoryCommand;
        private AsyncRelayCommand? _testRssFeedCommand;
        private RelayCommand? _removeTrackerCommand;
        private RelayCommand? _duplicateTrackerCommand;

        // AniDL
        private readonly AniDLService _aniDLService = new();
        private readonly AniDLUpdateService _aniDLUpdateService = new();
        private AsyncRelayCommand? _checkAniDLUpdateCommand;
        private AsyncRelayCommand? _applyAniDLUpdateCommand;
        private string _aniDLUpdateStatus = "";
        private bool _hasAniDLUpdate;
        private double _aniDLUpdateProgress;
        private AniDLReleaseInfo? _aniDLLatestRelease;

        // CRD
        private CRDViewModel? _crdViewModel;

        public MainViewModel()
        {
            // Initialize collections
            Shows = new ObservableCollection<ShowViewModel>();
            SortedShows = new ObservableCollection<ShowViewModel>();
            Trackers = new ObservableCollection<TrackerViewModel>();
            ColorSchemes = new ObservableCollection<ColorSchemeDefinition>(ColorSchemeDefinition.GetPresetSchemes());

            // Initialize automation (must be after Shows is created)
            Automation = new AutomationViewModel(Shows, () => _settings);

            // File commands
            OpenFileCommand = new RelayCommand(OpenFile);
            _saveFileCommand = new RelayCommand(SaveFile, CanSaveFile);
            SaveFileAsCommand = new RelayCommand(SaveFileAs);
            NewFileCommand = new RelayCommand(NewFile);

            // Show commands
            AddShowCommand = new RelayCommand(AddShow);
            _removeShowCommand = new RelayCommand(RemoveShow, () => SelectedShow != null);
            _duplicateShowCommand = new RelayCommand(DuplicateShow, () => SelectedShow != null);
            RefreshSortCommand = new RelayCommand(RefreshSort);

            // Episode commands
            _addManualEpisodeCommand = new RelayCommand(AddManualEpisode, () => SelectedShow != null);
            _removeLastEpisodeCommand = new RelayCommand(RemoveLastEpisode, () => SelectedShow != null && SelectedShow.EpisodesReleased.Any());
            _clearEpisodeHistoryCommand = new RelayCommand(ClearEpisodeHistory, () => SelectedShow != null);

            // RSS Test
            _testRssFeedCommand = new AsyncRelayCommand(TestRssFeedAsync, () => SelectedShow != null && !string.IsNullOrEmpty(SelectedShow.RssFeed));

            // qBittorrent
            RefreshQBitCommand = new RelayCommand(RefreshQBit);
            OpenQBitSettingsCommand = new RelayCommand(() => SelectedTabIndex = 2); // Switch to Connections tab

            // Browse commands for tools
            BrowseMkvmergeCommand = new RelayCommand(() => BrowseFile("mkvmerge|mkvmerge.exe|All|*.*", s => MkvmergePath = s));
            BrowseFfmpegCommand = new RelayCommand(() => BrowseFile("ffmpeg|ffmpeg.exe|All|*.*", s => FfmpegPath = s));

            // Color Scheme
            ApplyColorSchemeCommand = new RelayCommand(ApplyColorScheme);

            // Trackers
            AddTrackerCommand = new RelayCommand(AddTracker);
            EditTrackerCommand = new RelayCommand<TrackerViewModel>(EditTracker);
            _removeTrackerCommand = new RelayCommand(RemoveTracker, () => SelectedTracker != null);
            _duplicateTrackerCommand = new RelayCommand(DuplicateTracker, () => SelectedTracker != null);
            SaveTrackerCommand = new RelayCommand(SaveTracker);
            CancelEditTrackerCommand = new RelayCommand(CancelEditTracker);

            // Folder browse commands
            BrowseBasePathCommand = new RelayCommand(() => BrowseFolder(s => BasePath = s, BasePath));
            BrowseTempFolderCommand = new RelayCommand(() => BrowseFolder(s => TempFolder = s, TempFolder));
            BrowseLogsFolderCommand = new RelayCommand(() => BrowseFolder(s => LogsFolder = s, LogsFolder));
            BrowseEncodingFolderCommand = new RelayCommand(() => BrowseFolder(s => EncodingFolder = s, EncodingFolder));
            BrowseScreenshotsFolderCommand = new RelayCommand(() => BrowseFolder(s => ScreenshotsFolder = s, ScreenshotsFolder));
            BrowseSeedingFolderCommand = new RelayCommand(() => BrowseFolder(s => SeedingFolder = s, SeedingFolder));
            OpenTempFolderCommand = new RelayCommand(() => OpenFolder(TempFolder));
            OpenLogsFolderCommand = new RelayCommand(() => OpenFolder(LogsFolder));
            ClearTempFolderCommand = new RelayCommand(ClearTempFolder);
            CleanOldLogsCommand = new RelayCommand(CleanOldLogs);

            // CRD commands + ViewModel
            BrowseCRDPathCommand = new RelayCommand(() => BrowseFolder(s => CRDPath = s, CRDPath));
            _crdViewModel = new CRDViewModel(() => _settings);

            // AniDL commands
            BrowseAniDLPathCommand = new RelayCommand(() => BrowseFolder(s => AniDLPath = s, AniDLPath));
            _checkAniDLUpdateCommand = new AsyncRelayCommand(CheckAniDLUpdateAsync);
            _applyAniDLUpdateCommand = new AsyncRelayCommand(ApplyAniDLUpdateAsync, () => _hasAniDLUpdate && _aniDLLatestRelease?.DownloadUrl != null);
            OpenAniDLSearchCommand = new RelayCommand(RaiseOpenAniDLSearch);

            _aniDLUpdateService.StatusChanged += (_, msg) =>
                Application.Current?.Dispatcher?.BeginInvoke(() => AniDLUpdateStatus = msg);

            // Set default color scheme
            _selectedColorScheme = ColorSchemes.FirstOrDefault(c => c.Name == "Dark Blue") ?? ColorSchemes.First();

            // Try to auto-load default settings file
            var defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "JudasEncodingManager", "appSettings.json");
            if (File.Exists(defaultPath))
            {
                LoadSettingsFromFile(defaultPath);
            }
            else
            {
                // Initialize with defaults
                _settings = new AppSettings();
                UpdateTempFolderSize();
            }
        }

        private bool CanSaveFile()
        {
            // Enable Save when: we have a file to save to, OR we have unsaved changes (which will trigger SaveAs)
            return !string.IsNullOrEmpty(_currentFilePath) || _hasUnsavedChanges;
        }

        private void NotifyCommandsCanExecuteChanged()
        {
            _saveFileCommand?.NotifyCanExecuteChanged();
            _removeShowCommand?.NotifyCanExecuteChanged();
            _duplicateShowCommand?.NotifyCanExecuteChanged();
            _addManualEpisodeCommand?.NotifyCanExecuteChanged();
            _removeLastEpisodeCommand?.NotifyCanExecuteChanged();
            _clearEpisodeHistoryCommand?.NotifyCanExecuteChanged();
            _testRssFeedCommand?.NotifyCanExecuteChanged();
            _removeTrackerCommand?.NotifyCanExecuteChanged();
            _duplicateTrackerCommand?.NotifyCanExecuteChanged();
        }

        // ==================== COLLECTIONS ====================

        public ObservableCollection<ShowViewModel> Shows { get; }
        public ObservableCollection<ShowViewModel> SortedShows { get; }
        public ObservableCollection<TrackerViewModel> Trackers { get; }
        public ObservableCollection<ColorSchemeDefinition> ColorSchemes { get; }

        // ==================== AUTOMATION ====================

        public AutomationViewModel Automation { get; }

        // ==================== FILE COMMANDS ====================

        public ICommand OpenFileCommand { get; }
        public ICommand SaveFileCommand => _saveFileCommand!;
        public ICommand SaveFileAsCommand { get; }
        public ICommand NewFileCommand { get; }

        // ==================== SHOW COMMANDS ====================

        public ICommand AddShowCommand { get; }
        public ICommand RemoveShowCommand => _removeShowCommand!;
        public ICommand DuplicateShowCommand => _duplicateShowCommand!;
        public ICommand RefreshSortCommand { get; }
        public ICommand AddManualEpisodeCommand => _addManualEpisodeCommand!;
        public ICommand RemoveLastEpisodeCommand => _removeLastEpisodeCommand!;
        public ICommand ClearEpisodeHistoryCommand => _clearEpisodeHistoryCommand!;
        public ICommand TestRssFeedCommand => _testRssFeedCommand!;

        // ==================== QBITTORRENT COMMANDS ====================

        public ICommand RefreshQBitCommand { get; }
        public ICommand OpenQBitSettingsCommand { get; }

        // ==================== BROWSE COMMANDS ====================

        public ICommand BrowseMkvmergeCommand { get; }
        public ICommand BrowseFfmpegCommand { get; }
        public ICommand BrowseBasePathCommand { get; }
        public ICommand BrowseTempFolderCommand { get; }
        public ICommand BrowseLogsFolderCommand { get; }
        public ICommand BrowseEncodingFolderCommand { get; }
        public ICommand BrowseScreenshotsFolderCommand { get; }
        public ICommand BrowseSeedingFolderCommand { get; }
        public ICommand OpenTempFolderCommand { get; }
        public ICommand OpenLogsFolderCommand { get; }
        public ICommand ClearTempFolderCommand { get; }
        public ICommand CleanOldLogsCommand { get; }

        // ==================== COLOR SCHEME COMMANDS ====================

        public ICommand ApplyColorSchemeCommand { get; }

        // ==================== TRACKER COMMANDS ====================

        public ICommand AddTrackerCommand { get; }
        public ICommand EditTrackerCommand { get; }
        public ICommand RemoveTrackerCommand => _removeTrackerCommand!;
        public ICommand DuplicateTrackerCommand => _duplicateTrackerCommand!;
        public ICommand SaveTrackerCommand { get; }
        public ICommand CancelEditTrackerCommand { get; }

        // ==================== CRD COMMANDS / VIEWMODEL ====================

        public ICommand BrowseCRDPathCommand { get; }

        /// <summary>Exposes the CRD management view-model for the CRD tab.</summary>
        public CRDViewModel CRDViewModel => _crdViewModel!;

        // ==================== ANIDL COMMANDS ====================

        public ICommand BrowseAniDLPathCommand { get; }
        public ICommand CheckAniDLUpdateCommand => _checkAniDLUpdateCommand!;
        public ICommand ApplyAniDLUpdateCommand => _applyAniDLUpdateCommand!;
        public ICommand OpenAniDLSearchCommand { get; }

        // ==================== BASIC PROPERTIES ====================

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            set 
            { 
                _hasUnsavedChanges = value; 
                OnPropertyChanged();
                _saveFileCommand?.NotifyCanExecuteChanged();
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public bool IsTestMode
        {
            get => _isTestMode;
            set
            {
                _isTestMode = value;
                if (_settings != null) _settings.TestMode = value;
                OnPropertyChanged();
                HasUnsavedChanges = true;
            }
        }

        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set { _selectedTabIndex = value; OnPropertyChanged(); }
        }

        public ShowViewModel? SelectedShow
        {
            get => _selectedShow;
            set
            {
                _selectedShow = value;
                OnPropertyChanged();
                RssTestResult = "";
                NotifyCommandsCanExecuteChanged();
            }
        }

        public string ManualEpisodeNumber
        {
            get => _manualEpisodeNumber;
            set { _manualEpisodeNumber = value; OnPropertyChanged(); }
        }

        public string RssTestResult
        {
            get => _rssTestResult;
            set { _rssTestResult = value; OnPropertyChanged(); }
        }

        public bool IsTestingRss
        {
            get => _isTestingRss;
            set { _isTestingRss = value; OnPropertyChanged(); }
        }

        // ==================== GENERAL SETTINGS ====================

        public string MachineName
        {
            get => _settings.MachineName;
            set { _settings.MachineName = value; OnPropertyChanged(); HasUnsavedChanges = true; }
        }

        public string ImgbbApiKey
        {
            get => _settings.ImgbbApiKey;
            set { _settings.ImgbbApiKey = value; OnPropertyChanged(); HasUnsavedChanges = true; }
        }

        public string FreeimageApiKey
        {
            get => _settings.FreeimageApiKey;
            set { _settings.FreeimageApiKey = value; OnPropertyChanged(); HasUnsavedChanges = true; }
        }

        public string MkvmergePath
        {
            get => _settings.Remuxer.MkvmergePath;
            set { _settings.Remuxer.MkvmergePath = value; OnPropertyChanged(); HasUnsavedChanges = true; }
        }

        public string FfmpegPath
        {
            get => _settings.Remuxer.FfmpegPath;
            set { _settings.Remuxer.FfmpegPath = value; OnPropertyChanged(); HasUnsavedChanges = true; }
        }

        // ==================== QBITTORRENT SETTINGS ====================

        public string QBitLocalIpPort
        {
            get => _settings.QBittorrent.LocalIpPort;
            set { _settings.QBittorrent.LocalIpPort = value; OnPropertyChanged(); OnPropertyChanged(nameof(LocalQBitUrl)); HasUnsavedChanges = true; }
        }

        public string QBitSeedboxIpPort
        {
            get => _settings.QBittorrent.SeedboxIpPort;
            set { _settings.QBittorrent.SeedboxIpPort = value; OnPropertyChanged(); OnPropertyChanged(nameof(SeedboxQBitUrl)); HasUnsavedChanges = true; }
        }

        public string QBitSeedboxUsername
        {
            get => _settings.QBittorrent.SeedboxUsername;
            set { _settings.QBittorrent.SeedboxUsername = value; OnPropertyChanged(); HasUnsavedChanges = true; }
        }

        public string QBitSeedboxPassword
        {
            get => _settings.QBittorrent.SeedboxPassword;
            set { _settings.QBittorrent.SeedboxPassword = value; OnPropertyChanged(); HasUnsavedChanges = true; }
        }

        public string QBitSeedboxReleasesPath
        {
            get => _settings.QBittorrent.SeedboxReleasesPath;
            set { _settings.QBittorrent.SeedboxReleasesPath = value; OnPropertyChanged(); HasUnsavedChanges = true; }
        }

        public bool IsLocalQBitSelected
        {
            get => _isLocalQBitSelected;
            set
            {
                _isLocalQBitSelected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentQBitUrl));
                OnPropertyChanged(nameof(IsQBitUrlEmpty));
            }
        }

        public bool IsSeedboxQBitSelected
        {
            get => _isSeedboxQBitSelected;
            set
            {
                _isSeedboxQBitSelected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentQBitUrl));
                OnPropertyChanged(nameof(IsQBitUrlEmpty));
            }
        }

        public string LocalQBitUrl => QBitLocalIpPort;
        public string SeedboxQBitUrl => QBitSeedboxIpPort;
        public string CurrentQBitUrl => IsLocalQBitSelected ? LocalQBitUrl : SeedboxQBitUrl;
        public bool IsQBitUrlEmpty => string.IsNullOrWhiteSpace(CurrentQBitUrl);

        public string QBitConnectionStatus
        {
            get => _qBitConnectionStatus;
            set { _qBitConnectionStatus = value; OnPropertyChanged(); }
        }

        public bool IsQBitLoading
        {
            get => _isQBitLoading;
            set { _isQBitLoading = value; OnPropertyChanged(); }
        }

        public void SetQBitConnectionStatus(string status)
        {
            QBitConnectionStatus = status;
            IsQBitLoading = status == "Connecting";
        }

        public void UpdateQBitConnectionStatus()
        {
            if (IsQBitUrlEmpty)
                SetQBitConnectionStatus("Not Configured");
        }

        // ==================== FTP SETTINGS ====================

        public string FtpHost
        {
            get => _settings.Ftp.Host;
            set { _settings.Ftp.Host = value; OnPropertyChanged(); HasUnsavedChanges = true; }
        }

        public string FtpUsername
        {
            get => _settings.Ftp.Username;
            set { _settings.Ftp.Username = value; OnPropertyChanged(); HasUnsavedChanges = true; }
        }

        public string FtpPassword
        {
            get => _settings.Ftp.Password;
            set { _settings.Ftp.Password = value; OnPropertyChanged(); HasUnsavedChanges = true; }
        }

        public string FtpReleasesPath
        {
            get => _settings.Ftp.ReleasesPath;
            set { _settings.Ftp.ReleasesPath = value; OnPropertyChanged(); HasUnsavedChanges = true; }
        }

        public string FtpTorrentsPath
        {
            get => _settings.Ftp.TorrentsPath;
            set { _settings.Ftp.TorrentsPath = value; OnPropertyChanged(); HasUnsavedChanges = true; }
        }

        // ==================== DISCORD SETTINGS ====================

        public string DiscordWebhookUrl
        {
            get => _settings.Discord.WebhookUrl;
            set { _settings.Discord.WebhookUrl = value; OnPropertyChanged(); HasUnsavedChanges = true; }
        }

        public string DiscordServerInviteLink
        {
            get => _settings.Discord.ServerInviteLink;
            set { _settings.Discord.ServerInviteLink = value; OnPropertyChanged(); HasUnsavedChanges = true; }
        }

        public string DiscordBotToken
        {
            get => _settings.Discord.BotToken;
            set { _settings.Discord.BotToken = value; OnPropertyChanged(); HasUnsavedChanges = true; }
        }

        // ==================== TELEGRAM SETTINGS ====================

        public string TelegramBotToken
        {
            get => _settings.Telegram.BotToken;
            set { _settings.Telegram.BotToken = value; OnPropertyChanged(); HasUnsavedChanges = true; }
        }

        public string TelegramChatId
        {
            get => _settings.Telegram.ChatId;
            set { _settings.Telegram.ChatId = value; OnPropertyChanged(); HasUnsavedChanges = true; }
        }

        // ==================== AUTO POSTING SETTINGS ====================

        public string NyaaCookieDdlg
        {
            get => _settings.AutoPosting.NyaaCookieDdlg;
            set { _settings.AutoPosting.NyaaCookieDdlg = value; OnPropertyChanged(); HasUnsavedChanges = true; }
        }

        public string NyaaCookieSession
        {
            get => _settings.AutoPosting.NyaaCookieSession;
            set { _settings.AutoPosting.NyaaCookieSession = value; OnPropertyChanged(); HasUnsavedChanges = true; }
        }

        public string AnidexApi
        {
            get => _settings.AutoPosting.AnidexApi;
            set { _settings.AutoPosting.AnidexApi = value; OnPropertyChanged(); HasUnsavedChanges = true; }
        }

        public bool IsAnidexEnabled
        {
            get => _isAnidexEnabled;
            set { _isAnidexEnabled = value; OnPropertyChanged(); }
        }

        // ==================== COLOR SCHEME ====================

        public ColorSchemeDefinition? SelectedColorScheme
        {
            get => _selectedColorScheme;
            set
            {
                _selectedColorScheme = value;
                OnPropertyChanged();
                UpdateColorPreview();
            }
        }

        public SolidColorBrush PreviewPrimaryBrush => CreateBrush(_selectedColorScheme?.PrimaryColor);
        public SolidColorBrush PreviewAccentBrush => CreateBrush(_selectedColorScheme?.AccentColor);
        public SolidColorBrush PreviewBackgroundBrush => CreateBrush(_selectedColorScheme?.BackgroundColor);
        public SolidColorBrush PreviewCardBrush => CreateBrush(_selectedColorScheme?.CardColor);
        public SolidColorBrush PreviewSuccessBrush => CreateBrush(_selectedColorScheme?.SuccessColor ?? "#4caf50");
        public SolidColorBrush PreviewWarningBrush => CreateBrush(_selectedColorScheme?.WarningColor ?? "#ff9800");
        public SolidColorBrush PreviewErrorBrush => CreateBrush(_selectedColorScheme?.ErrorColor ?? "#f44336");

        private SolidColorBrush CreateBrush(string? hex)
        {
            if (string.IsNullOrEmpty(hex)) return new SolidColorBrush(Colors.Gray);
            try
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            }
            catch
            {
                return new SolidColorBrush(Colors.Gray);
            }
        }

        private void UpdateColorPreview()
        {
            OnPropertyChanged(nameof(PreviewPrimaryBrush));
            OnPropertyChanged(nameof(PreviewAccentBrush));
            OnPropertyChanged(nameof(PreviewBackgroundBrush));
            OnPropertyChanged(nameof(PreviewCardBrush));
            OnPropertyChanged(nameof(PreviewSuccessBrush));
            OnPropertyChanged(nameof(PreviewWarningBrush));
            OnPropertyChanged(nameof(PreviewErrorBrush));
        }

        // ==================== TRACKER PROPERTIES ====================

        public TrackerViewModel? SelectedTracker
        {
            get => _selectedTracker;
            set 
            { 
                _selectedTracker = value; 
                OnPropertyChanged();
                _removeTrackerCommand?.NotifyCanExecuteChanged();
                _duplicateTrackerCommand?.NotifyCanExecuteChanged();
            }
        }

        public TrackerViewModel? EditingTracker
        {
            get => _editingTracker;
            set { _editingTracker = value; OnPropertyChanged(); }
        }

        public bool IsEditingTracker
        {
            get => _isEditingTracker;
            set { _isEditingTracker = value; OnPropertyChanged(); }
        }

        public string EditingTrackerCredentials
        {
            get => _editingTrackerCredentials;
            set { _editingTrackerCredentials = value; OnPropertyChanged(); }
        }

        public Array TrackerTypes => Enum.GetValues(typeof(TrackerType));

        // ==================== FOLDER PROPERTIES ====================

        public string BasePath
        {
            get => _settings.Folders.BasePath;
            set { _settings.Folders.BasePath = value; OnPropertyChanged(); HasUnsavedChanges = true; }
        }

        public string TempFolder
        {
            get => _settings.Folders.TempFolder;
            set { _settings.Folders.TempFolder = value; OnPropertyChanged(); HasUnsavedChanges = true; UpdateTempFolderSize(); }
        }

        public string LogsFolder
        {
            get => _settings.Folders.LogsFolder;
            set { _settings.Folders.LogsFolder = value; OnPropertyChanged(); HasUnsavedChanges = true; }
        }

        public string EncodingFolder
        {
            get => _settings.Folders.EncodingFolder;
            set { _settings.Folders.EncodingFolder = value; OnPropertyChanged(); HasUnsavedChanges = true; }
        }

        public string ScreenshotsFolder
        {
            get => _settings.Folders.ScreenshotsFolder;
            set { _settings.Folders.ScreenshotsFolder = value; OnPropertyChanged(); HasUnsavedChanges = true; }
        }

        public string SeedingFolder
        {
            get => _settings.Folders.SeedingFolder;
            set { _settings.Folders.SeedingFolder = value; OnPropertyChanged(); HasUnsavedChanges = true; }
        }

        public bool AutoClearTempOnSuccess
        {
            get => _settings.Folders.AutoClearTempOnSuccess;
            set { _settings.Folders.AutoClearTempOnSuccess = value; OnPropertyChanged(); HasUnsavedChanges = true; }
        }

        public int KeepLogsDays
        {
            get => _settings.Folders.KeepLogsDays;
            set { _settings.Folders.KeepLogsDays = value; OnPropertyChanged(); HasUnsavedChanges = true; }
        }

        public string TempFolderSize
        {
            get => _tempFolderSize;
            set { _tempFolderSize = value; OnPropertyChanged(); }
        }

        // ==================== CRD PROPERTIES ====================

        public string CRDPath
        {
            get => _settings.CRD.Path;
            set
            {
                _settings.CRD.Path = value;
                OnPropertyChanged();
                _crdViewModel?.RefreshStatus();
                HasUnsavedChanges = true;
            }
        }

        public bool CRDAutoUpdate
        {
            get => _settings.CRD.AutoUpdate;
            set { _settings.CRD.AutoUpdate = value; OnPropertyChanged(); HasUnsavedChanges = true; }
        }

        // ==================== ANIDL PROPERTIES ====================

        public string AniDLPath
        {
            get => _settings.AniDL.Path;
            set
            {
                _settings.AniDL.Path = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AniDLInstalledVersion));
                OnPropertyChanged(nameof(AniDLIsInstalled));
                HasUnsavedChanges = true;
            }
        }

        public bool AniDLCheckUpdatesOnStartup
        {
            get => _settings.AniDL.CheckUpdatesOnStartup;
            set { _settings.AniDL.CheckUpdatesOnStartup = value; OnPropertyChanged(); HasUnsavedChanges = true; }
        }

        public string AniDLInstalledVersion =>
            _aniDLService.GetInstalledVersion(AniDLPath);

        public bool AniDLIsInstalled =>
            _aniDLService.IsInstalled(AniDLPath);

        public string AniDLUpdateStatus
        {
            get => _aniDLUpdateStatus;
            set { _aniDLUpdateStatus = value; OnPropertyChanged(); }
        }

        public bool HasAniDLUpdate
        {
            get => _hasAniDLUpdate;
            private set
            {
                _hasAniDLUpdate = value;
                OnPropertyChanged();
                _applyAniDLUpdateCommand?.NotifyCanExecuteChanged();
            }
        }

        public double AniDLUpdateProgress
        {
            get => _aniDLUpdateProgress;
            set { _aniDLUpdateProgress = value; OnPropertyChanged(); }
        }

        public string AniDLLatestVersion => _aniDLLatestRelease?.TagName ?? "";

        /// <summary>Raised by <see cref="RaiseOpenAniDLSearch"/>; handled by MainWindow to open the dialog.</summary>
        public event EventHandler? OpenAniDLSearchRequested;

        // ==================== FILE METHODS ====================

        private void OpenFile()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                Title = "Open Settings File"
            };

            if (dialog.ShowDialog() == true)
            {
                LoadSettingsFromFile(dialog.FileName);
            }
        }

        private void LoadSettingsFromFile(string path)
        {
            try
            {
                var json = File.ReadAllText(path);
                _settings = JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
                _currentFilePath = path;

                // Load shows
                Shows.Clear();
                SortedShows.Clear();
                foreach (var show in _settings.WeeklyShows)
                {
                    var vm = new ShowViewModel(show);
                    Shows.Add(vm);
                    SortedShows.Add(vm);
                }
                RefreshSort();

                // Load trackers
                LoadTrackers();

                // Load color scheme
                var schemeName = _settings.ColorScheme ?? "Dark Blue";
                SelectedColorScheme = ColorSchemes.FirstOrDefault(c => c.Name == schemeName) ?? ColorSchemes.First();

                // Update all properties
                IsTestMode = _settings.TestMode;
                NotifyAllPropertiesChanged();
                UpdateTempFolderSize();

                HasUnsavedChanges = false;
                _saveFileCommand?.NotifyCanExecuteChanged();
                StatusMessage = $"Loaded: {Path.GetFileName(path)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveFile()
        {
            if (string.IsNullOrEmpty(_currentFilePath))
            {
                SaveFileAs();
                return;
            }

            SaveSettingsToFile(_currentFilePath);
        }

        private void SaveFileAs()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                Title = "Save Settings File",
                FileName = "appSettings.json"
            };

            if (dialog.ShowDialog() == true)
            {
                SaveSettingsToFile(dialog.FileName);
            }
        }

        private void SaveSettingsToFile(string path)
        {
            try
            {
                // Update settings from shows
                _settings.WeeklyShows = Shows.Select(s => s.Model).ToList();

                // Update trackers
                _settings.AutoPosting.Trackers = Trackers.Select(t => t.Model).ToList();

                // Update color scheme
                _settings.ColorScheme = SelectedColorScheme?.Name ?? "Dark Blue";

                var json = JsonConvert.SerializeObject(_settings, Formatting.Indented);
                File.WriteAllText(path, json);

                _currentFilePath = path;
                HasUnsavedChanges = false;
                _saveFileCommand?.NotifyCanExecuteChanged();
                StatusMessage = $"Saved: {Path.GetFileName(path)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void NewFile()
        {
            if (HasUnsavedChanges)
            {
                var result = MessageBox.Show("You have unsaved changes. Save before creating new file?", "Unsaved Changes",
                    MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                    SaveFile();
                else if (result == MessageBoxResult.Cancel)
                    return;
            }

            _settings = new AppSettings();
            _currentFilePath = "";
            Shows.Clear();
            SortedShows.Clear();
            Trackers.Clear();
            SelectedColorScheme = ColorSchemes.First();
            NotifyAllPropertiesChanged();
            HasUnsavedChanges = false;
            _saveFileCommand?.NotifyCanExecuteChanged();
            StatusMessage = "New file created";
        }

        private void NotifyAllPropertiesChanged()
        {
            OnPropertyChanged(nameof(MachineName));
            OnPropertyChanged(nameof(ImgbbApiKey));
            OnPropertyChanged(nameof(FreeimageApiKey));
            OnPropertyChanged(nameof(MkvmergePath));
            OnPropertyChanged(nameof(FfmpegPath));
            OnPropertyChanged(nameof(QBitLocalIpPort));
            OnPropertyChanged(nameof(QBitSeedboxIpPort));
            OnPropertyChanged(nameof(QBitSeedboxUsername));
            OnPropertyChanged(nameof(QBitSeedboxPassword));
            OnPropertyChanged(nameof(QBitSeedboxReleasesPath));
            OnPropertyChanged(nameof(FtpHost));
            OnPropertyChanged(nameof(FtpUsername));
            OnPropertyChanged(nameof(FtpPassword));
            OnPropertyChanged(nameof(FtpReleasesPath));
            OnPropertyChanged(nameof(FtpTorrentsPath));
            OnPropertyChanged(nameof(DiscordWebhookUrl));
            OnPropertyChanged(nameof(DiscordServerInviteLink));
            OnPropertyChanged(nameof(DiscordBotToken));
            OnPropertyChanged(nameof(TelegramBotToken));
            OnPropertyChanged(nameof(TelegramChatId));
            OnPropertyChanged(nameof(NyaaCookieDdlg));
            OnPropertyChanged(nameof(NyaaCookieSession));
            OnPropertyChanged(nameof(AnidexApi));
            OnPropertyChanged(nameof(BasePath));
            OnPropertyChanged(nameof(TempFolder));
            OnPropertyChanged(nameof(LogsFolder));
            OnPropertyChanged(nameof(EncodingFolder));
            OnPropertyChanged(nameof(ScreenshotsFolder));
            OnPropertyChanged(nameof(SeedingFolder));
            OnPropertyChanged(nameof(AutoClearTempOnSuccess));
            OnPropertyChanged(nameof(KeepLogsDays));
            OnPropertyChanged(nameof(LocalQBitUrl));
            OnPropertyChanged(nameof(SeedboxQBitUrl));
            OnPropertyChanged(nameof(CurrentQBitUrl));
            OnPropertyChanged(nameof(IsQBitUrlEmpty));
            OnPropertyChanged(nameof(AniDLPath));
            OnPropertyChanged(nameof(AniDLCheckUpdatesOnStartup));
            OnPropertyChanged(nameof(AniDLInstalledVersion));
            OnPropertyChanged(nameof(AniDLIsInstalled));
            OnPropertyChanged(nameof(CRDPath));
            OnPropertyChanged(nameof(CRDAutoUpdate));
        }

        // ==================== SHOW METHODS ====================

        private void AddShow()
        {
            var newShow = new WeeklyShow
            {
                OutputTorrentTitle = "New Show",
                OutputFileTitle = "NewShow",
                SeasonNumber = 1,
                IsActive = true
            };

            var vm = new ShowViewModel(newShow);
            Shows.Add(vm);
            SortedShows.Add(vm);
            SelectedShow = vm;
            HasUnsavedChanges = true;
        }

        private void RemoveShow()
        {
            if (SelectedShow == null) return;

            var result = MessageBox.Show($"Remove '{SelectedShow.OutputTorrentTitle}'?", "Confirm Remove",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                Shows.Remove(SelectedShow);
                SortedShows.Remove(SelectedShow);
                SelectedShow = null;
                HasUnsavedChanges = true;
            }
        }

        private void DuplicateShow()
        {
            if (SelectedShow == null) return;

            var copy = SelectedShow.Clone();
            Shows.Add(copy);
            SortedShows.Add(copy);
            SelectedShow = copy;
            HasUnsavedChanges = true;
        }

        private void RefreshSort()
        {
            var sorted = Shows.OrderBy(s => s.SortKey.DayIndex).ThenBy(s => s.SortKey.Time).ToList();
            SortedShows.Clear();
            foreach (var show in sorted)
            {
                SortedShows.Add(show);
            }
        }

        // ==================== EPISODE METHODS ====================

        private void AddManualEpisode()
        {
            if (SelectedShow == null || !int.TryParse(ManualEpisodeNumber, out int epNum)) return;

            var version = SelectedShow.GetLatestVersion(epNum) + 1;
            var release = new EpisodeRelease
            {
                EpisodeNumber = epNum,
                Version = version,
                ReleaseDate = DateTime.Now
            };

            SelectedShow.Model.EpisodesReleased.Add(release);
            SelectedShow.RefreshEpisodes();
            ManualEpisodeNumber = "";
            HasUnsavedChanges = true;
        }

        private void RemoveLastEpisode()
        {
            if (SelectedShow == null || !SelectedShow.EpisodesReleased.Any()) return;

            SelectedShow.Model.EpisodesReleased.RemoveAt(SelectedShow.Model.EpisodesReleased.Count - 1);
            SelectedShow.RefreshEpisodes();
            HasUnsavedChanges = true;
        }

        private void ClearEpisodeHistory()
        {
            if (SelectedShow == null) return;

            var result = MessageBox.Show("Clear all episode history?", "Confirm Clear",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                SelectedShow.Model.EpisodesReleased.Clear();
                SelectedShow.RefreshEpisodes();
                HasUnsavedChanges = true;
            }
        }

        // ==================== RSS TEST ====================

        private async Task TestRssFeedAsync()
        {
            if (SelectedShow == null || string.IsNullOrEmpty(SelectedShow.RssFeed)) return;

            IsTestingRss = true;
            RssTestResult = "Testing RSS feed...";

            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                var response = await client.GetStringAsync(SelectedShow.RssFeed);

                var doc = XDocument.Parse(response);
                var items = doc.Descendants("item").Take(5).ToList();

                if (items.Any())
                {
                    var result = $"✅ Found {doc.Descendants("item").Count()} items. Latest 5:\n\n";
                    foreach (var item in items)
                    {
                        var title = item.Element("title")?.Value ?? "Unknown";
                        result += $"• {title}\n";
                    }
                    RssTestResult = result;
                }
                else
                {
                    RssTestResult = "⚠️ RSS feed is valid but contains no items.";
                }
            }
            catch (Exception ex)
            {
                RssTestResult = $"❌ Error: {ex.Message}";
            }
            finally
            {
                IsTestingRss = false;
            }
        }

        // ==================== QBITTORRENT ====================

        private void RefreshQBit()
        {
            QBitRefreshRequested?.Invoke(this, EventArgs.Empty);
        }

        // ==================== BROWSE METHODS ====================

        private void BrowseFile(string filter, Action<string> setter)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = filter
            };

            if (dialog.ShowDialog() == true)
            {
                setter(dialog.FileName);
            }
        }

        private void BrowseFolder(Action<string> setter, string currentPath)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select Folder",
                SelectedPath = currentPath,
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                setter(dialog.SelectedPath);
            }
        }

        private void OpenFolder(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Process.Start("explorer.exe", path);
                }
                else
                {
                    var result = MessageBox.Show($"Folder does not exist:\n{path}\n\nCreate it now?",
                        "Folder Not Found", MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        Directory.CreateDirectory(path);
                        Process.Start("explorer.exe", path);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==================== COLOR SCHEME METHODS ====================

        private void ApplyColorScheme()
        {
            if (SelectedColorScheme == null) return;

            try
            {
                var app = Application.Current;
                var resources = app.Resources;

                UpdateColorResource(resources, "PrimaryColor", SelectedColorScheme.PrimaryColor);
                UpdateColorResource(resources, "PrimaryDarkColor", SelectedColorScheme.PrimaryDarkColor);
                UpdateColorResource(resources, "AccentColor", SelectedColorScheme.AccentColor);
                UpdateColorResource(resources, "BackgroundColor", SelectedColorScheme.BackgroundColor);
                UpdateColorResource(resources, "SurfaceColor", SelectedColorScheme.SurfaceColor);
                UpdateColorResource(resources, "CardColor", SelectedColorScheme.CardColor);

                UpdateBrushResource(resources, "PrimaryBrush", SelectedColorScheme.PrimaryColor);
                UpdateBrushResource(resources, "PrimaryDarkBrush", SelectedColorScheme.PrimaryDarkColor);
                UpdateBrushResource(resources, "AccentBrush", SelectedColorScheme.AccentColor);
                UpdateBrushResource(resources, "BackgroundBrush", SelectedColorScheme.BackgroundColor);
                UpdateBrushResource(resources, "SurfaceBrush", SelectedColorScheme.SurfaceColor);
                UpdateBrushResource(resources, "CardBrush", SelectedColorScheme.CardColor);

                _settings.ColorScheme = SelectedColorScheme.Name;
                HasUnsavedChanges = true;
                StatusMessage = $"Applied color scheme: {SelectedColorScheme.Name}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to apply color scheme: {ex.Message}";
            }
        }

        private void UpdateColorResource(ResourceDictionary resources, string key, string hexColor)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hexColor);
                if (resources.Contains(key))
                    resources[key] = color;
            }
            catch { }
        }

        private void UpdateBrushResource(ResourceDictionary resources, string key, string hexColor)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hexColor);
                if (resources.Contains(key))
                    resources[key] = new SolidColorBrush(color);
            }
            catch { }
        }

        // ==================== TRACKER METHODS ====================

        private void LoadTrackers()
        {
            Trackers.Clear();
            if (_settings.AutoPosting.Trackers != null)
            {
                foreach (var tracker in _settings.AutoPosting.Trackers)
                {
                    Trackers.Add(new TrackerViewModel(tracker));
                }
            }
        }

        private void AddTracker()
        {
            var newTracker = new TrackerViewModel(new TrackerConfig
            {
                Name = "New Tracker",
                Type = TrackerType.Custom,
                Enabled = false,
                EndpointUrl = "https://",
                Category = "",
                Notes = ""
            });

            Trackers.Add(newTracker);
            SelectedTracker = newTracker;
            EditTracker(newTracker);
            HasUnsavedChanges = true;
        }

        private void EditTracker(TrackerViewModel? tracker)
        {
            if (tracker == null) return;
            EditingTracker = tracker;
            EditingTrackerCredentials = tracker.CredentialsText;
            IsEditingTracker = true;
        }

        private void RemoveTracker()
        {
            if (SelectedTracker == null) return;

            var result = MessageBox.Show($"Remove tracker '{SelectedTracker.Name}'?", "Confirm Remove",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                Trackers.Remove(SelectedTracker);
                SelectedTracker = null;
                IsEditingTracker = false;
                HasUnsavedChanges = true;
            }
        }

        private void DuplicateTracker()
        {
            if (SelectedTracker == null) return;
            var copy = SelectedTracker.Clone();
            Trackers.Add(copy);
            SelectedTracker = copy;
            HasUnsavedChanges = true;
        }

        private void SaveTracker()
        {
            if (EditingTracker == null) return;
            EditingTracker.CredentialsText = EditingTrackerCredentials;
            IsEditingTracker = false;
            EditingTracker = null;
            HasUnsavedChanges = true;
        }

        private void CancelEditTracker()
        {
            IsEditingTracker = false;
            EditingTracker = null;
        }

        // ==================== FOLDER METHODS ====================

        private void UpdateTempFolderSize()
        {
            try
            {
                if (Directory.Exists(TempFolder))
                {
                    var size = GetDirectorySize(new DirectoryInfo(TempFolder));
                    TempFolderSize = $"Current size: {FormatBytes(size)}";
                }
                else
                {
                    TempFolderSize = "Folder does not exist";
                }
            }
            catch
            {
                TempFolderSize = "Unable to calculate size";
            }
        }

        private long GetDirectorySize(DirectoryInfo dir)
        {
            try
            {
                return dir.GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            }
            catch
            {
                return 0;
            }
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }

        private void ClearTempFolder()
        {
            var result = MessageBox.Show(
                $"Delete all files in:\n{TempFolder}\n\nIncluding video/, audio-subs/, data/, done/ subfolders?",
                "Clear Temp Folder", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                int filesDeleted = 0;
                long bytesFreed = 0;

                if (Directory.Exists(TempFolder))
                {
                    foreach (var file in Directory.GetFiles(TempFolder, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            bytesFreed += new FileInfo(file).Length;
                            File.Delete(file);
                            filesDeleted++;
                        }
                        catch { }
                    }

                    foreach (var subdir in new[] { "video", "audio-subs", "data", "done" })
                    {
                        var path = Path.Combine(TempFolder, subdir);
                        if (Directory.Exists(path))
                        {
                            try { Directory.Delete(path, true); } catch { }
                        }
                    }
                }

                UpdateTempFolderSize();
                StatusMessage = $"Cleared temp: {filesDeleted} files, {FormatBytes(bytesFreed)} freed";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to clear temp folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CleanOldLogs()
        {
            try
            {
                if (!Directory.Exists(LogsFolder))
                {
                    StatusMessage = "Logs folder does not exist";
                    return;
                }

                int filesDeleted = 0;
                long bytesFreed = 0;
                var cutoffDate = DateTime.Now.AddDays(-KeepLogsDays);

                foreach (var file in Directory.GetFiles(LogsFolder, "*.log", SearchOption.AllDirectories))
                {
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.LastWriteTime < cutoffDate)
                        {
                            bytesFreed += info.Length;
                            File.Delete(file);
                            filesDeleted++;
                        }
                    }
                    catch { }
                }

                StatusMessage = $"Cleaned: {filesDeleted} logs older than {KeepLogsDays} days, {FormatBytes(bytesFreed)} freed";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to clean logs: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==================== ANIDL METHODS ====================

        private void RaiseOpenAniDLSearch()
        {
            OpenAniDLSearchRequested?.Invoke(this, EventArgs.Empty);
        }

        private async Task CheckAniDLUpdateAsync()
        {
            try
            {
                AniDLUpdateStatus = "Checking for updates…";
                var release = await _aniDLUpdateService.GetLatestReleaseAsync();
                if (release == null)
                {
                    AniDLUpdateStatus = "⚠️ Could not reach GitHub.";
                    return;
                }

                _aniDLLatestRelease = release;
                OnPropertyChanged(nameof(AniDLLatestVersion));

                var installed = AniDLInstalledVersion;
                if (string.IsNullOrEmpty(installed))
                {
                    AniDLUpdateStatus = $"aniDL not found at configured path. Latest: {release.TagName}";
                    HasAniDLUpdate = true;
                }
                else if (installed.TrimStart('v') != release.TagName.TrimStart('v'))
                {
                    AniDLUpdateStatus = $"Update available: {installed} → {release.TagName}";
                    HasAniDLUpdate = true;
                }
                else
                {
                    AniDLUpdateStatus = $"✅ Up to date ({installed})";
                    HasAniDLUpdate = false;
                }
            }
            catch (Exception ex)
            {
                AniDLUpdateStatus = $"❌ Error: {ex.Message}";
            }
        }

        private async Task ApplyAniDLUpdateAsync()
        {
            if (_aniDLLatestRelease == null) return;

            try
            {
                AniDLUpdateStatus = "Downloading update…";
                var progress = new Progress<double>(p =>
                {
                    AniDLUpdateProgress = p;
                    AniDLUpdateStatus = $"Downloading… {p:P0}";
                });

                await _aniDLUpdateService.DownloadAndInstallAsync(_aniDLLatestRelease, AniDLPath, progress);

                AniDLUpdateProgress = 0;
                HasAniDLUpdate = false;
                OnPropertyChanged(nameof(AniDLInstalledVersion));
                OnPropertyChanged(nameof(AniDLIsInstalled));
                AniDLUpdateStatus = $"✅ Updated to {_aniDLLatestRelease.TagName}";
            }
            catch (Exception ex)
            {
                AniDLUpdateProgress = 0;
                AniDLUpdateStatus = $"❌ Update failed: {ex.Message}";
            }
        }

        /// <summary>
        /// Called by MainWindow on startup when <see cref="AniDLCheckUpdatesOnStartup"/> is true.
        /// Runs the update check silently in the background.
        /// </summary>
        public async Task CheckAniDLUpdateOnStartupAsync()
        {
            if (!AniDLCheckUpdatesOnStartup) return;
            await CheckAniDLUpdateAsync();
        }
    }
}
