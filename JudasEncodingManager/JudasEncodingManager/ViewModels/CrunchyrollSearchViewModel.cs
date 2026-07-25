using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using JudasEncodingManager.Models;
using JudasEncodingManager.Services;

namespace JudasEncodingManager.ViewModels
{
    /// <summary>
    /// View-model for the Crunchyroll series search dialog.
    /// The user types a show name, clicks Search, and picks from the results.
    /// The selected <see cref="SelectedSeries"/> is read back by the caller.
    /// </summary>
    public class CrunchyrollSearchViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private readonly CrunchyrollApiService _api;
        private CancellationTokenSource? _searchCts;

        private string            _query          = "";
        private string            _status         = "Enter a show name and click Search.";
        private bool              _isSearching;
        private CrunchyrollSeries? _selectedSeries;

        public CrunchyrollSearchViewModel(CrunchyrollApiService api)
        {
            _api = api;

            SearchCommand = new AsyncRelayCommand(SearchAsync,
                () => !string.IsNullOrWhiteSpace(Query) && !IsSearching);

            SelectCommand = new RelayCommand(
                () => DialogResult = true,
                () => SelectedSeries != null);
        }

        // ==================== COMMANDS ====================

        public ICommand SearchCommand { get; }
        public ICommand SelectCommand { get; }

        // ==================== PROPERTIES ====================

        public ObservableCollection<CrunchyrollSeries> Results { get; } = new();

        public string Query
        {
            get => _query;
            set
            {
                _query = value;
                OnPropertyChanged();
                ((AsyncRelayCommand)SearchCommand).NotifyCanExecuteChanged();
            }
        }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public bool IsSearching
        {
            get => _isSearching;
            set
            {
                _isSearching = value;
                OnPropertyChanged();
                ((AsyncRelayCommand)SearchCommand).NotifyCanExecuteChanged();
            }
        }

        public CrunchyrollSeries? SelectedSeries
        {
            get => _selectedSeries;
            set
            {
                _selectedSeries = value;
                OnPropertyChanged();
                ((RelayCommand)SelectCommand).NotifyCanExecuteChanged();
            }
        }

        /// <summary>Set to true when the user clicks "Select" — read by the code-behind.</summary>
        public bool DialogResult { get; private set; }

        // ==================== SEARCH ====================

        private async Task SearchAsync()
        {
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var ct = _searchCts.Token;

            IsSearching = true;
            Status = $"Searching for \"{Query}\"…";
            Results.Clear();
            SelectedSeries = null;

            try
            {
                var (results, error) = await _api.SearchSeriesAsync(Query).ConfigureAwait(false);

                if (ct.IsCancellationRequested) return;

                if (!string.IsNullOrEmpty(error))
                {
                    Status = $"❌ {error}";
                    return;
                }

                foreach (var s in results)
                    Results.Add(s);

                Status = results.Count > 0
                    ? $"Found {results.Count} series — double-click or select and click Apply."
                    : "No results found. Try a different search term.";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Status = $"❌ {ex.Message}";
            }
            finally
            {
                if (!ct.IsCancellationRequested)
                    IsSearching = false;
            }
        }
    }
}
