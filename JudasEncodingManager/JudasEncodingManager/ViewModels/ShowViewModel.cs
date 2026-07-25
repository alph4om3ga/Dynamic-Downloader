using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using JudasEncodingManager.Models;

namespace JudasEncodingManager.ViewModels
{
    public class ShowViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private readonly WeeklyShow _model;
        public WeeklyShow Model => _model;

        public ShowViewModel() : this(new WeeklyShow()) { }

        public ShowViewModel(WeeklyShow model)
        {
            _model = model;
            EpisodesReleased = new ObservableCollection<EpisodeRelease>(_model.EpisodesReleased);
        }

        // Display properties
        public string DisplayName => !string.IsNullOrEmpty(OutputFileTitle) ? OutputFileTitle : OutputTorrentTitle;
        
        public string ShortReleaseDay => ReleaseDay?.ToString().Substring(0, 3) ?? "---";
        
        public string ProgressText => ExpectedEpisodes > 0 
            ? $"{EpisodesReleased.Count}/{ExpectedEpisodes}"
            : $"{EpisodesReleased.Count} eps";

        // Sort key for ordering shows by release day and time
        public (int DayIndex, TimeSpan Time) SortKey
        {
            get
            {
                var dayOrder = ReleaseDay.HasValue ? (int)ReleaseDay.Value : 99;
                var time = TimeSpan.TryParse(ReleaseTime, out var t) ? t : TimeSpan.Zero;
                return (dayOrder, time);
            }
        }

        // Refresh the episodes collection from the model
        public void RefreshEpisodes()
        {
            EpisodesReleased.Clear();
            foreach (var ep in _model.EpisodesReleased)
            {
                EpisodesReleased.Add(ep);
            }
            OnPropertyChanged(nameof(ProgressText));
        }

        // Get the latest version number for a specific episode
        public int GetLatestVersion(int episodeNumber)
        {
            var existing = EpisodesReleased
                .Where(e => e.EpisodeNumber == episodeNumber)
                .OrderByDescending(e => e.Version)
                .FirstOrDefault();
            return existing?.Version ?? 0;
        }

        // Model properties with change notification
        public string IniScriptName
        {
            get => _model.IniScriptName;
            set { _model.IniScriptName = value; OnPropertyChanged(); }
        }

        public string OutputTorrentTitle
        {
            get => _model.OutputTorrentTitle;
            set { _model.OutputTorrentTitle = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); }
        }

        public string OutputFileTitle
        {
            get => _model.OutputFileTitle;
            set { _model.OutputFileTitle = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); }
        }

        public int SeasonNumber
        {
            get => _model.SeasonNumber;
            set { _model.SeasonNumber = value; OnPropertyChanged(); }
        }

        public int NumberOfEpisodesToRemoveFromCount
        {
            get => _model.NumberOfEpisodesToRemoveFromCount;
            set { _model.NumberOfEpisodesToRemoveFromCount = value; OnPropertyChanged(); }
        }

        public string RssFeed
        {
            get => _model.RssFeed;
            set { _model.RssFeed = value; OnPropertyChanged(); }
        }

        public bool AutopostOnTrackers
        {
            get => _model.AutopostOnTrackers;
            set { _model.AutopostOnTrackers = value; OnPropertyChanged(); }
        }

        public DayOfWeek? ReleaseDay
        {
            get => _model.ReleaseDay;
            set { _model.ReleaseDay = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShortReleaseDay)); OnPropertyChanged(nameof(ReleaseDayIndex)); }
        }

        // Index for ComboBox binding (0 = Not Set, 1-7 = Sunday-Saturday)
        public int ReleaseDayIndex
        {
            get => ReleaseDay.HasValue ? (int)ReleaseDay.Value + 1 : 0;
            set
            {
                ReleaseDay = value > 0 ? (DayOfWeek)(value - 1) : null;
                OnPropertyChanged();
            }
        }

        public string ReleaseTime
        {
            get => _model.ReleaseTime;
            set { _model.ReleaseTime = value; OnPropertyChanged(); }
        }

        public int ExpectedEpisodes
        {
            get => _model.ExpectedEpisodes;
            set { _model.ExpectedEpisodes = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressText)); }
        }

        public bool IsActive
        {
            get => _model.IsActive;
            set { _model.IsActive = value; OnPropertyChanged(); }
        }

        public string CustomEpisodeRegex
        {
            get => _model.CustomEpisodeRegex;
            set { _model.CustomEpisodeRegex = value; OnPropertyChanged(); }
        }

        public string SourceGroup
        {
            get => _model.SourceGroup;
            set { _model.SourceGroup = value; OnPropertyChanged(); }
        }

        public bool IsUncensored
        {
            get => _model.IsUncensored;
            set { _model.IsUncensored = value; OnPropertyChanged(); }
        }

        // Episode tracking
        public ObservableCollection<EpisodeRelease> EpisodesReleased { get; }

        public void AddEpisode(int episodeNumber, int version, string sourceFilename, string outputFilename)
        {
            var release = new EpisodeRelease
            {
                EpisodeNumber = episodeNumber,
                Version = version,
                ReleaseDate = DateTime.Now,
                SourceFilename = sourceFilename,
                OutputFilename = outputFilename
            };

            EpisodesReleased.Add(release);
            _model.EpisodesReleased.Add(release);
            OnPropertyChanged(nameof(ProgressText));
        }

        public void RemoveLastEpisode()
        {
            if (EpisodesReleased.Count > 0)
            {
                var last = EpisodesReleased.Last();
                EpisodesReleased.Remove(last);
                _model.EpisodesReleased.Remove(last);
                OnPropertyChanged(nameof(ProgressText));
            }
        }

        public void ClearEpisodes()
        {
            EpisodesReleased.Clear();
            _model.EpisodesReleased.Clear();
            OnPropertyChanged(nameof(ProgressText));
        }

        // Create a copy of this show
        public ShowViewModel Clone()
        {
            var newModel = new WeeklyShow
            {
                IniScriptName = IniScriptName,
                OutputTorrentTitle = OutputTorrentTitle + " (Copy)",
                OutputFileTitle = OutputFileTitle + " Copy",
                SeasonNumber = SeasonNumber,
                NumberOfEpisodesToRemoveFromCount = NumberOfEpisodesToRemoveFromCount,
                RssFeed = RssFeed,
                AutopostOnTrackers = AutopostOnTrackers,
                ReleaseDay = ReleaseDay,
                ReleaseTime = ReleaseTime,
                ExpectedEpisodes = ExpectedEpisodes,
                IsActive = false, // New copies start inactive
                CustomEpisodeRegex = CustomEpisodeRegex,
                SourceGroup = SourceGroup,
                IsUncensored = IsUncensored
            };
            return new ShowViewModel(newModel);
        }
    }
}
