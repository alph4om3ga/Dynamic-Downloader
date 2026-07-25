using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using JudasEncodingManager.Models;
using JudasEncodingManager.Services;

namespace JudasEncodingManager.ViewModels
{
    /// <summary>
    /// View-model for the dedicated CRD tab.
    /// Manages the CRD.exe process, update, and status polling.
    /// </summary>
    public class CRDViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private readonly CRDService _service = new();
        private readonly Func<AppSettings> _getSettings;

        private string _status = "Not configured";
        private string _version = "";
        private bool _isInstalled;
        private bool _isRunning;
        private string _activityLog = "";
        private CancellationTokenSource? _pollCts;

        public CRDViewModel(Func<AppSettings> getSettings)
        {
            _getSettings = getSettings;

            LaunchCRDCommand    = new RelayCommand(LaunchCRD,    () => IsInstalled && !IsRunning);
            RunUpdaterCommand   = new RelayCommand(RunUpdater,   () => IsInstalled);
            RefreshStatusCommand = new RelayCommand(RefreshStatus);

            _service.LogMessage += (_, msg) => AppendLog(msg);

            RefreshStatus();
            StartPolling();
        }

        // ==================== COMMANDS ====================

        public ICommand LaunchCRDCommand    { get; }
        public ICommand RunUpdaterCommand   { get; }
        public ICommand RefreshStatusCommand { get; }

        // ==================== PROPERTIES ====================

        public string CrdPath
        {
            get => _getSettings().CRD.Path;
            set
            {
                _getSettings().CRD.Path = value;
                OnPropertyChanged();
                RefreshStatus();
            }
        }

        public bool AutoUpdate
        {
            get => _getSettings().CRD.AutoUpdate;
            set
            {
                _getSettings().CRD.AutoUpdate = value;
                OnPropertyChanged();
            }
        }

        public string Status
        {
            get => _status;
            private set { _status = value; OnPropertyChanged(); }
        }

        public string InstalledVersion
        {
            get => _version;
            private set { _version = value; OnPropertyChanged(); }
        }

        public bool IsInstalled
        {
            get => _isInstalled;
            private set
            {
                _isInstalled = value;
                OnPropertyChanged();
                NotifyCommandsChanged();
            }
        }

        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                _isRunning = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RunningStatusText));
                NotifyCommandsChanged();
            }
        }

        public string RunningStatusText => IsRunning ? "CRD is running" : "CRD is not running";

        public string ActivityLog
        {
            get => _activityLog;
            private set { _activityLog = value; OnPropertyChanged(); }
        }

        // ==================== METHODS ====================

        private void LaunchCRD()
        {
            _service.Configure(CrdPath);
            if (!_service.Launch())
                AppendLog("[CRD] Launch failed — check the path in General settings.");
        }

        private void RunUpdater()
        {
            _service.Configure(CrdPath);
            _service.RunUpdater();
        }

        public void RefreshStatus()
        {
            _service.Configure(CrdPath);

            IsInstalled = _service.IsInstalled();
            IsRunning   = _service.IsRunning();

            if (!IsInstalled)
            {
                InstalledVersion = "";
                Status = $"Not found at: {CrdPath}";
            }
            else
            {
                InstalledVersion = _service.GetInstalledVersion();
                Status = IsRunning
                    ? $"{InstalledVersion} — Running"
                    : $"{InstalledVersion} — Idle";
            }
        }

        private void StartPolling()
        {
            _pollCts?.Cancel();
            _pollCts = new CancellationTokenSource();
            var token = _pollCts.Token;

            _ = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
                    Application.Current?.Dispatcher.BeginInvoke(RefreshStatus);
                }
            }, token);
        }

        public void StopPolling()
        {
            _pollCts?.Cancel();
        }

        private void AppendLog(string message)
        {
            var ts = DateTime.Now.ToString("HH:mm:ss");
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                ActivityLog = $"[{ts}] {message}\n" + ActivityLog;
                // Keep log from growing unbounded
                if (ActivityLog.Length > 8000)
                    ActivityLog = ActivityLog[..6000];
            });
        }

        private void NotifyCommandsChanged()
        {
            ((RelayCommand)LaunchCRDCommand).NotifyCanExecuteChanged();
            ((RelayCommand)RunUpdaterCommand).NotifyCanExecuteChanged();
        }
    }
}
