using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace JudasEncodingManager.Models
{
    public enum QueueItemStatus
    {
        Pending,
        Downloading,
        DownloadFailed,
        DownloadComplete,
        AnalyzingTracks,
        Encoding,
        EncodingFailed,
        EncodingComplete,
        Muxing,
        MuxingFailed,
        TakingScreenshots,
        UploadingScreenshots,
        GeneratingDescription,
        CreatingTorrent,
        UploadingTorrentFile,
        UploadingDescription,
        UploadingEpisode,
        UploadFailed,
        AddingToSeedbox,
        PostingToNyaa,
        Completed,
        Paused,
        Error
    }

    public class AudioTrackInfo
    {
        public int Index { get; set; }
        public string Language { get; set; } = "und";
        public string LanguageDisplay { get; set; } = "Unknown";
        public string Codec { get; set; } = "";
        public int Channels { get; set; }
        public string Title { get; set; } = "";
        public bool IsDefault { get; set; }
        
        // For Judas naming
        public string GetJudasTrackName(string codec = "Opus", string bitrate = "112Kbps")
        {
            var channelDesc = Channels switch
            {
                1 => "Mono",
                2 => "Stereo",
                6 => "5.1",
                8 => "7.1",
                _ => $"{Channels}ch"
            };
            return $"[Judas] {LanguageDisplay} {channelDesc} ({codec} {bitrate})";
        }
    }

    public class SubtitleTrackInfo
    {
        public int Index { get; set; }
        public string Language { get; set; } = "und";
        public string LanguageDisplay { get; set; } = "Unknown";
        public string Codec { get; set; } = "";
        public string Title { get; set; } = "";
        public bool IsDefault { get; set; }
        public bool IsForced { get; set; }
        public bool IsSDH { get; set; }
        
        // Priority for sorting (lower = higher priority)
        public int SortPriority => GetLanguagePriority(Language, Title);

        /// <summary>
        /// Language code to write with --language, normalising generic codes to BCP-47
        /// where the source didn't already supply the specific variant.
        /// e.g. "pt" + "Brazilian" title → "pt-BR"; "es" + "Latin" title → "es-419"
        /// </summary>
        public string NormalizedLanguage
        {
            get
            {
                var langLower = Language.ToLowerInvariant();
                var titleLower = Title.ToLowerInvariant();

                // Already a specific BCP-47 tag — keep it
                if (langLower is "es-es" or "es-419" or "pt-br")
                    return Language;

                // Generic Spanish → derive from title
                if (langLower is "spa" or "es")
                    return (titleLower.Contains("lat") || titleLower.Contains("latin")) ? "es-419" : "es-ES";

                // Generic Portuguese → pt-BR when title indicates Brazil, else keep "pt"
                if (langLower is "por" or "pt")
                    return (titleLower.Contains("br") || titleLower.Contains("brazil")) ? "pt-BR" : Language;

                return Language;
            }
        }

        private static int GetLanguagePriority(string lang, string title)
        {
            var langLower = lang.ToLowerInvariant();
            var titleLower = title.ToLowerInvariant();

            // English — sub-sorted: plain (10) → CC (20) → Signs (30)
            if (langLower is "eng" or "en")
            {
                if (titleLower.Contains("cc") || titleLower.Contains("closed") || titleLower.Contains("sdh"))
                    return 20;
                if (titleLower.Contains("sign"))
                    return 30;
                return 10;
            }

            // French, German, Italian
            if (langLower is "fre" or "fr") return 40;
            if (langLower is "ger" or "de") return 50;
            if (langLower is "ita" or "it") return 60;

            // Spanish — European first, then Latin American
            if (langLower == "es-es") return 70;
            if (langLower == "es-419") return 80;
            if (langLower is "spa" or "es")
                return (titleLower.Contains("lat") || titleLower.Contains("latin")) ? 80 : 70;

            // Portuguese
            if (langLower is "por" or "pt" or "pt-br") return 90;

            // Remaining languages
            return langLower switch
            {
                "rus" or "ru"            => 100,
                "ara" or "ar"            => 110,
                "chi" or "zh" or "zho"   => 120,
                "kor" or "ko"            => 130,
                "jpn" or "ja"            => 140,
                _                        => 1000
            };
        }

        public string GetDisplayName()
        {
            var langLower = Language.ToLowerInvariant();
            var titleLower = Title.ToLowerInvariant();

            // Chinese variants
            if (titleLower.Contains("simplified") || (langLower.StartsWith("zh") && titleLower.Contains("chs")))
                return "Simplified_CR";
            if (titleLower.Contains("traditional") || (langLower.StartsWith("zh") && titleLower.Contains("cht")))
                return "Traditional_CR";
            if (langLower is "chi" or "zh" or "zho")
                return "CR";

            // Spanish — always use [ESP]/[LAT] labels regardless of what the title says
            if (langLower is "es-419" || (langLower is "spa" or "es" && (titleLower.Contains("lat") || titleLower.Contains("latin"))))
                return "Spanish[LAT]";
            if (langLower is "spa" or "es" or "es-es")
                return "Spanish[ESP]";

            // Portuguese — always [BR] label when Brazilian
            if (langLower is "pt-br" || (langLower is "por" or "pt" && (titleLower.Contains("br") || titleLower.Contains("brazil"))))
                return "Portuguese[BR]";
            if (langLower is "por" or "pt")
                return "Portuguese";

            // English CC / Signs — bracket notation
            var baseName = LanguageDisplay; // e.g. "English", "French", …
            if (titleLower.Contains("cc") || titleLower.Contains("closed") || titleLower.Contains("sdh"))
                return $"{baseName} [CC]";
            if (titleLower.Contains("sign"))
                return $"{baseName} [Signs]";

            // Everything else: use the language display name (ignores noisy source titles
            // like "Bahasa Indonesia" — LanguageDisplay is already "Indonesian")
            return LanguageDisplay;
        }
    }

    /// <summary>
    /// Indicates how a queue item's source file was obtained.
    /// </summary>
    public enum DownloadSource
    {
        /// <summary>Downloaded via Crunchy-DL (CRD) from Crunchyroll. Primary source.</summary>
        CRD,
        /// <summary>Detected via Nyaa RSS feed and downloaded through qBittorrent. Secondary source.</summary>
        Rss
    }

    public class QueueItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public string Id { get; set; } = Guid.NewGuid().ToString();
        public WeeklyShow Show { get; set; } = new();
        public int EpisodeNumber { get; set; }
        public int Version { get; set; } = 1;

        /// <summary>Tracks whether this item came from CRD or RSS/torrent.</summary>
        public DownloadSource DownloadSource { get; set; } = DownloadSource.Rss;
        
        private QueueItemStatus _status = QueueItemStatus.Pending;
        public QueueItemStatus Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusDisplay)); }
        }

        private string _statusMessage = "";
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public string StatusDisplay => Status.ToString();

        public DateTime AddedAt { get; set; } = DateTime.Now;
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        
        // Test run settings
        public bool IsTestRun { get; set; } = false;
        public int TestEncodeDurationSeconds { get; set; } = 300; // 5 minutes
        
        // File paths
        public string SourceFileName { get; set; } = "";
        public string SourceFilePath { get; set; } = "";
        public string EncodedFilePath { get; set; } = "";
        public string MuxedFilePath { get; set; } = "";  // After proper muxing
        public string TorrentFilePath { get; set; } = "";
        public string DescriptionFilePath { get; set; } = "";
        
        // Track information (populated after analysis)
        public List<AudioTrackInfo> AudioTracks { get; set; } = new();
        public List<SubtitleTrackInfo> SubtitleTracks { get; set; } = new();
        
        // Source info for description
        public string SourceGroup { get; set; } = "";
        public long SourceFileSizeBytes { get; set; }
        public string SourceFileSizeFormatted => FormatFileSize(SourceFileSizeBytes);
        
        // Screenshots
        public List<string> ScreenshotPaths { get; set; } = new();
        public List<string> ScreenshotUrls { get; set; } = new();
        
        // Torrent info
        public string TorrentHash { get; set; } = "";
        public string TorrentLink { get; set; } = "";   // Magnet / .torrent URL (RSS source only)
        public string NyaaUrl { get; set; } = "";
        
        // Progress
        private double _downloadProgress;
        public double DownloadProgress
        {
            get => _downloadProgress;
            set { _downloadProgress = value; OnPropertyChanged(); }
        }

        private double _encodeProgress;
        public double EncodeProgress
        {
            get => _encodeProgress;
            set { _encodeProgress = value; OnPropertyChanged(); }
        }

        private double _uploadProgress;
        public double UploadProgress
        {
            get => _uploadProgress;
            set { _uploadProgress = value; OnPropertyChanged(); }
        }
        
        // Error handling
        public int DownloadRetries { get; set; }
        public int UploadRetries { get; set; }
        
        private string _lastError = "";
        public string LastError
        {
            get => _lastError;
            set { _lastError = value; OnPropertyChanged(); }
        }
        
        // Output naming - use OutputFileTitle (short name) for files
        public string OutputFileName => $"[Judas] {Show.OutputFileTitle} - S{Show.SeasonNumber:D2}E{EpisodeNumber:D2}{(Version > 1 ? $"v{Version}" : "")}{(IsTestRun ? " [TEST]" : "")}";
        
        // Episode string with version for display purposes
        public string EpisodeString => $"S{Show.SeasonNumber:D2}E{EpisodeNumber:D2}{(Version > 1 ? $"v{Version}" : "")}";
        
        // Dynamic torrent display name based on actual track info
        public string TorrentDisplayName
        {
            get
            {
                var tags = new List<string> { "1080p", "HEVC x265 10bit" };
                
                // Add uncensored tag if applicable
                if (Show.IsUncensored)
                {
                    // Insert at beginning after [Judas]
                }
                
                // Determine audio tag
                var hasJapanese = AudioTracks.Any(a => a.Language.ToLowerInvariant() is "jpn" or "ja" or "japanese");
                var hasEnglish = AudioTracks.Any(a => a.Language.ToLowerInvariant() is "eng" or "en" or "english");
                
                if (hasJapanese && hasEnglish)
                    tags.Add("Dual-Audio");
                
                // Determine subtitle tag
                var subLangs = SubtitleTracks.Select(s => s.Language.ToLowerInvariant()).Distinct().ToList();
                if (subLangs.Count > 2)
                    tags.Add("Multi-Subs");
                else if (subLangs.Any(l => l is "eng" or "en"))
                    tags.Add("Eng-Subs");
                else if (subLangs.Count > 0)
                    tags.Add("Multi-Subs");
                else
                    tags.Add("Multi-Subs"); // Default fallback
                
                var tagString = string.Join("][", tags);
                var uncensoredTag = Show.IsUncensored ? " [Uncensored]" : "";
                var testTag = IsTestRun ? " [TEST]" : "";
                var versionTag = Version > 1 ? $"v{Version}" : "";
                
                return $"[Judas] {Show.OutputTorrentTitle}{uncensoredTag} - {EpisodeString} [{tagString}] (Weekly){testTag}";
            }
        }
        
        // For Nyaa description - audio tracks list
        public string AudioTracksDescription
        {
            get
            {
                if (AudioTracks.Count == 0)
                    return "Japanese";
                    
                var languages = AudioTracks
                    .Select(a => a.LanguageDisplay)
                    .Distinct()
                    .ToList();
                    
                return string.Join(" - ", languages);
            }
        }
        
        // For Nyaa description - subtitle tracks list (ordered)
        public string SubtitleTracksDescription
        {
            get
            {
                if (SubtitleTracks.Count == 0)
                    return "English - Multiple Languages";
                    
                var orderedSubs = SubtitleTracks
                    .OrderBy(s => s.SortPriority)
                    .Select(s => s.GetDisplayName())
                    .Distinct()
                    .ToList();
                    
                return string.Join(" - ", orderedSubs);
            }
        }
        
        // Source info for description (e.g., "Erai-raws (1.36 GB)")
        public string SourceInfoForDescription
        {
            get
            {
                var group = !string.IsNullOrEmpty(SourceGroup) ? SourceGroup : Show.SourceGroup;
                if (string.IsNullOrEmpty(group))
                    group = "Erai-raws";
                    
                return $"{group} ({SourceFileSizeFormatted})";
            }
        }
        
        private static string FormatFileSize(long bytes)
        {
            if (bytes >= 1073741824)
                return $"{bytes / 1073741824.0:F2} GB";
            if (bytes >= 1048576)
                return $"{bytes / 1048576.0:F2} MB";
            if (bytes >= 1024)
                return $"{bytes / 1024.0:F2} KB";
            return $"{bytes} B";
        }
    }

    public class ActivityLogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Message { get; set; } = "";
        public ActivityLogLevel Level { get; set; } = ActivityLogLevel.Info;
        public string? QueueItemId { get; set; }
        public string? ShowName { get; set; }
    }

    public enum ActivityLogLevel
    {
        Debug,
        Info,
        Warning,
        Error,
        Success
    }

    public class RssItem
    {
        public string Title { get; set; } = "";
        public string Link { get; set; } = "";
        public string TorrentUrl { get; set; } = "";
        public DateTime PublishDate { get; set; }
        public long Size { get; set; }
        public string InfoHash { get; set; } = "";

        /// <summary>
        /// Set when this item was sourced from a local file (e.g. CRD output folder)
        /// rather than an RSS feed. When non-empty the torrent download stage is skipped.
        /// </summary>
        public string LocalFilePath { get; set; } = "";

        /// <summary>True when this item represents a local file, not a torrent.</summary>
        public bool IsLocalFile => !string.IsNullOrEmpty(LocalFilePath);
    }
}
