using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using JudasEncodingManager.Services;

namespace JudasEncodingManager.ViewModels
{
    public class AniDLSearchViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private readonly AniDLService _service;

        // ===== Services dropdown =====
        // Keys are the friendly display names shown in the UI.
        // Values are the CLI service IDs expected by multi-downloader-nx.
        private static readonly Dictionary<string, string> ServiceIds = new()
        {
            { "Crunchyroll",              "cr"     },
            { "HIDIVE",                   "hidive" },
            { "Animation Digital Network","adn"    },
            { "Funimation",               "funi"   },
        };

        private string _selectedService = "Crunchyroll";
        public ObservableCollection<string> Services { get; } = new(ServiceIds.Keys);

        public string SelectedService
        {
            get => _selectedService;
            set { _selectedService = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Returns the CLI service ID for the currently selected display name.
        /// Falls back to the lowercase display name if not found in the map.
        /// </summary>
        private string GetServiceId() =>
            ServiceIds.TryGetValue(_selectedService, out var id) ? id : _selectedService.ToLowerInvariant();

        // ===== Search =====
        private string _searchQuery = "";
        public string SearchQuery
        {
            get => _searchQuery;
            set { _searchQuery = value; OnPropertyChanged(); }
        }

        private bool _isSearching;
        public bool IsSearching
        {
            get => _isSearching;
            set { _isSearching = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotBusy)); }
        }

        public ObservableCollection<AniDLSearchResult> SearchResults { get; } = new();

        private AniDLSearchResult? _selectedResult;
        public AniDLSearchResult? SelectedResult
        {
            get => _selectedResult;
            set { _selectedResult = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSelectedResult)); }
        }

        public bool HasSelectedResult => _selectedResult != null;

        // ===== Download =====
        private string _seasonId = "";
        public string SeasonId
        {
            get => _seasonId;
            set { _seasonId = value; OnPropertyChanged(); }
        }

        private string _episodeSelection = "";
        public string EpisodeSelection
        {
            get => _episodeSelection;
            set { _episodeSelection = value; OnPropertyChanged(); }
        }

        private bool _isDownloading;
        public bool IsDownloading
        {
            get => _isDownloading;
            set { _isDownloading = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotBusy)); }
        }

        public bool IsNotBusy => !_isSearching && !_isDownloading;

        // ===== Output =====
        private string _outputText = "Enter a search term above to find a show, or type a Season ID directly below.\n\nEpisode selection examples:\n  • 1        → episode 1 only\n  • 1-12     → episodes 1 through 12\n  • 1,3,5    → episodes 1, 3 and 5\n  • all      → every available episode";
        public string OutputText
        {
            get => _outputText;
            set { _outputText = value; OnPropertyChanged(); }
        }

        private string _statusText = "Ready";
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        private bool _downloadSucceeded;
        public bool DownloadSucceeded
        {
            get => _downloadSucceeded;
            set { _downloadSucceeded = value; OnPropertyChanged(); }
        }

        // ===== Commands =====
        public ICommand SearchCommand { get; }
        public ICommand SelectResultCommand { get; }
        public ICommand DownloadCommand { get; }
        public ICommand AuthCommand { get; }
        public ICommand ClearOutputCommand { get; }
        public ICommand CancelCommand { get; }

        private CancellationTokenSource? _cts;

        public event EventHandler? CloseRequested;

        public AniDLSearchViewModel(AniDLService service)
        {
            _service = service;

            SearchCommand = new AsyncRelayCommand(SearchAsync);
            SelectResultCommand = new RelayCommand<AniDLSearchResult>(SelectResult);
            DownloadCommand = new AsyncRelayCommand(DownloadAsync);
            AuthCommand = new RelayCommand(LaunchAuth);
            ClearOutputCommand = new RelayCommand(() => OutputText = "");
            CancelCommand = new RelayCommand(Cancel);
        }

        private async Task SearchAsync()
        {
            if (string.IsNullOrWhiteSpace(SearchQuery)) return;

            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            IsSearching = true;
            StatusText = $"Searching {SelectedService} for \"{SearchQuery}\"...";
            SearchResults.Clear();
            AppendOutput($"\n▶ Searching {SelectedService} for \"{SearchQuery}\"...\n");

            try
            {
                var (results, raw) = await _service.SearchAsync(
                    GetServiceId(), SearchQuery,
                    line => Application.Current?.Dispatcher?.BeginInvoke(() => AppendOutput(line)),
                    _cts.Token);

                if (results.Count > 0)
                {
                    foreach (var r in results)
                        SearchResults.Add(r);
                    StatusText = $"Found {results.Count} result(s). Select one below or enter a Season ID manually.";
                }
                else
                {
                    StatusText = "No parsed results — check output above for IDs, then enter one manually.";
                }
            }
            catch (OperationCanceledException)
            {
                StatusText = "Search cancelled.";
            }
            catch (Exception ex)
            {
                AppendOutput($"[ERROR] {ex.Message}");
                StatusText = $"Error: {ex.Message}";
            }
            finally
            {
                IsSearching = false;
            }
        }

        private void SelectResult(AniDLSearchResult? result)
        {
            if (result == null) return;
            SelectedResult = result;
            SeasonId = result.Id;
            StatusText = $"Selected: [{result.Id}] {result.Title}";
        }

        private async Task DownloadAsync()
        {
            if (string.IsNullOrWhiteSpace(SeasonId))
            {
                MessageBox.Show("Please enter or select a Season ID.", "Season ID Required",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            IsDownloading = true;
            DownloadSucceeded = false;
            StatusText = $"Downloading from {SelectedService} — Season {SeasonId}...";
            AppendOutput($"\n▶ Downloading {SelectedService} | Season: {SeasonId} | Episodes: {(string.IsNullOrWhiteSpace(EpisodeSelection) ? "all" : EpisodeSelection)}\n");

            try
            {
                var ok = await _service.DownloadAsync(
                    GetServiceId(),
                    SeasonId,
                    EpisodeSelection,
                    line => Application.Current?.Dispatcher?.BeginInvoke(() => AppendOutput(line)),
                    _cts.Token);

                DownloadSucceeded = ok;
                StatusText = ok
                    ? "✅ Download completed successfully."
                    : "⚠️ Download may have encountered errors — check output above.";
            }
            catch (OperationCanceledException)
            {
                StatusText = "Download cancelled.";
            }
            catch (Exception ex)
            {
                AppendOutput($"[ERROR] {ex.Message}");
                StatusText = $"Error: {ex.Message}";
            }
            finally
            {
                IsDownloading = false;
            }
        }

        private void LaunchAuth()
        {
            var id = GetServiceId();
            AppendOutput($"\n▶ Launching authentication for {SelectedService} (service id: {id})...\n" +
                         "(A cmd window will open — complete the login there. The window stays open when done.)\n");
            _service.LaunchAuth(id);
        }

        private void Cancel()
        {
            _cts?.Cancel();
            StatusText = "Cancelling...";
        }

        private void AppendOutput(string line)
        {
            OutputText += line + "\n";
        }
    }
}
