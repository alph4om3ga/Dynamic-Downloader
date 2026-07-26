using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Newtonsoft.Json;

namespace JudasEncodingManager.Models
{
    public class AppSettings
    {
        [JsonProperty("machine_name")]
        public string MachineName { get; set; } = "";

        [JsonProperty("imgbb_api_key")]
        public string ImgbbApiKey { get; set; } = "";

        [JsonProperty("freeimage_api_key")]
        public string FreeimageApiKey { get; set; } = "";

        [JsonProperty("test_mode")]
        public bool TestMode { get; set; } = false;

        [JsonProperty("color_scheme")]
        public string ColorScheme { get; set; } = "Dark Blue";

        [JsonProperty("folders")]
        public FolderSettings Folders { get; set; } = new();

        [JsonProperty("qbittorrent_webui")]
        public QBittorrentSettings QBittorrent { get; set; } = new();

        [JsonProperty("ftp")]
        public FtpSettings Ftp { get; set; } = new();

        [JsonProperty("remuxer")]
        public RemuxerSettings Remuxer { get; set; } = new();

        [JsonProperty("discord")]
        public DiscordSettings Discord { get; set; } = new();

        [JsonProperty("telegram")]
        public TelegramSettings Telegram { get; set; } = new();

        [JsonProperty("auto_posting")]
        public AutoPostingSettings AutoPosting { get; set; } = new();

        [JsonProperty("weekly_shows")]
        public List<WeeklyShow> WeeklyShows { get; set; } = new();

        [JsonProperty("crd")]
        public CRDSettings CRD { get; set; } = new();
    }

    public class QBittorrentSettings
    {
        [JsonProperty("local_ip_port")]
        public string LocalIpPort { get; set; } = "http://127.0.0.1:8080/";

        [JsonProperty("seedbox_ip_port")]
        public string SeedboxIpPort { get; set; } = "";

        [JsonProperty("seedbox_username")]
        public string SeedboxUsername { get; set; } = "";

        [JsonProperty("seedbox_password")]
        public string SeedboxPassword { get; set; } = "";

        [JsonProperty("seedbox_releases_path")]
        public string SeedboxReleasesPath { get; set; } = "Releases/";
    }

    public class FtpSettings
    {
        [JsonProperty("host")]
        public string Host { get; set; } = "";

        [JsonProperty("username")]
        public string Username { get; set; } = "";

        [JsonProperty("password")]
        public string Password { get; set; } = "";

        [JsonProperty("releases_path")]
        public string ReleasesPath { get; set; } = "Releases/";

        [JsonProperty("torrents_path")]
        public string TorrentsPath { get; set; } = "TorrentFiles/";
    }

    public class RemuxerSettings
    {
        [JsonProperty("mkvmerge_path")]
        public string MkvmergePath { get; set; } = "C://Program Files/MKVToolNix/mkvmerge.exe";

        [JsonProperty("ffmpeg_path")]
        public string FfmpegPath { get; set; } = "C://FFmpeg/bin/ffmpeg.exe";
    }

    public class DiscordSettings
    {
        [JsonProperty("server_invite_link")]
        public string ServerInviteLink { get; set; } = "";

        [JsonProperty("bot_token")]
        public string BotToken { get; set; } = "";

        [JsonProperty("webhook_url")]
        public string WebhookUrl { get; set; } = "";
    }

    public class TelegramSettings
    {
        [JsonProperty("bot_token")]
        public string BotToken { get; set; } = "";

        [JsonProperty("chat_id")]
        public string ChatId { get; set; } = "";
    }

    public class FolderSettings
    {
        [JsonProperty("base_path")]
        public string BasePath { get; set; } = @"C:\JudasEncodingManager";

        [JsonProperty("temp_folder")]
        public string TempFolder { get; set; } = @"C:\JudasEncodingManager\Temp";

        [JsonProperty("logs_folder")]
        public string LogsFolder { get; set; } = @"C:\JudasEncodingManager\Logs";

        [JsonProperty("encoding_folder")]
        public string EncodingFolder { get; set; } = @"C:\JudasEncodingManager\Encoding";

        [JsonProperty("screenshots_folder")]
        public string ScreenshotsFolder { get; set; } = @"C:\JudasEncodingManager\Screenshots";

        [JsonProperty("seeding_folder")]
        public string SeedingFolder { get; set; } = @"C:\JudasEncodingManager\Seeding";

        [JsonProperty("auto_clear_temp_on_success")]
        public bool AutoClearTempOnSuccess { get; set; } = true;

        [JsonProperty("keep_logs_days")]
        public int KeepLogsDays { get; set; } = 30;

        // Computed paths for temp subfolders
        [JsonIgnore]
        public string VideoTempFolder => System.IO.Path.Combine(TempFolder, "video");

        [JsonIgnore]
        public string AudioSubsTempFolder => System.IO.Path.Combine(TempFolder, "audio-subs");

        [JsonIgnore]
        public string DataTempFolder => System.IO.Path.Combine(TempFolder, "data");

        [JsonIgnore]
        public string DoneTempFolder => System.IO.Path.Combine(TempFolder, "done");
    }

    public class AutoPostingSettings
    {
        [JsonProperty("nyaa_cookie_ddlg")]
        public string NyaaCookieDdlg { get; set; } = "";

        [JsonProperty("nyaa_cookie_session")]
        public string NyaaCookieSession { get; set; } = "";

        [JsonProperty("anidex_api")]
        public string AnidexApi { get; set; } = "";

        [JsonProperty("trackers")]
        public List<TrackerConfig> Trackers { get; set; } = new();
    }

    public class TrackerConfig
    {
        [JsonProperty("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("type")]
        public TrackerType Type { get; set; } = TrackerType.Custom;

        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonProperty("endpoint_url")]
        public string EndpointUrl { get; set; } = "";

        [JsonProperty("category")]
        public string Category { get; set; } = "";

        [JsonProperty("credentials")]
        public Dictionary<string, string> Credentials { get; set; } = new();

        [JsonProperty("notes")]
        public string Notes { get; set; } = "";
    }

    public enum TrackerType
    {
        Nyaa,
        AniDex,
        TokyoTosho,
        AnimeTosho,
        BakaBT,
        AnimeBytes,
        Custom
    }

    public class ColorSchemeDefinition
    {
        public string Name { get; set; } = "";
        public string PrimaryColor { get; set; } = "#1e88e5";
        public string PrimaryDarkColor { get; set; } = "#1565c0";
        public string AccentColor { get; set; } = "#ff5722";
        public string BackgroundColor { get; set; } = "#1a1a2e";
        public string SurfaceColor { get; set; } = "#16213e";
        public string CardColor { get; set; } = "#0f3460";
        public string TextColor { get; set; } = "#ffffff";
        public string TextSecondaryColor { get; set; } = "#b0b0b0";
        public string SuccessColor { get; set; } = "#4caf50";
        public string WarningColor { get; set; } = "#ff9800";
        public string ErrorColor { get; set; } = "#f44336";

        public static List<ColorSchemeDefinition> GetPresetSchemes()
        {
            return new List<ColorSchemeDefinition>
            {
                new ColorSchemeDefinition
                {
                    Name = "Dark Blue",
                    PrimaryColor = "#1e88e5",
                    PrimaryDarkColor = "#1565c0",
                    AccentColor = "#ff5722",
                    BackgroundColor = "#1a1a2e",
                    SurfaceColor = "#16213e",
                    CardColor = "#0f3460"
                },
                new ColorSchemeDefinition
                {
                    Name = "Dark Purple",
                    PrimaryColor = "#9c27b0",
                    PrimaryDarkColor = "#7b1fa2",
                    AccentColor = "#ff4081",
                    BackgroundColor = "#1a1a2e",
                    SurfaceColor = "#2d1b3d",
                    CardColor = "#4a2c5a"
                },
                new ColorSchemeDefinition
                {
                    Name = "Dark Green",
                    PrimaryColor = "#4caf50",
                    PrimaryDarkColor = "#388e3c",
                    AccentColor = "#ffeb3b",
                    BackgroundColor = "#1a2e1a",
                    SurfaceColor = "#1e3e1e",
                    CardColor = "#2d5a2d"
                },
                new ColorSchemeDefinition
                {
                    Name = "Midnight",
                    PrimaryColor = "#5c6bc0",
                    PrimaryDarkColor = "#3f51b5",
                    AccentColor = "#ff7043",
                    BackgroundColor = "#0d1117",
                    SurfaceColor = "#161b22",
                    CardColor = "#21262d"
                },
                new ColorSchemeDefinition
                {
                    Name = "Cyberpunk",
                    PrimaryColor = "#00ffff",
                    PrimaryDarkColor = "#00cccc",
                    AccentColor = "#ff00ff",
                    BackgroundColor = "#0a0a0f",
                    SurfaceColor = "#12121a",
                    CardColor = "#1a1a2a"
                },
                new ColorSchemeDefinition
                {
                    Name = "Sunset",
                    PrimaryColor = "#ff6b6b",
                    PrimaryDarkColor = "#ee5a5a",
                    AccentColor = "#feca57",
                    BackgroundColor = "#2d1b2d",
                    SurfaceColor = "#3d2a3d",
                    CardColor = "#4a3a4a"
                },
                new ColorSchemeDefinition
                {
                    Name = "Ocean",
                    PrimaryColor = "#00bcd4",
                    PrimaryDarkColor = "#0097a7",
                    AccentColor = "#ff9800",
                    BackgroundColor = "#0d2137",
                    SurfaceColor = "#0f2d4a",
                    CardColor = "#1a3d5c"
                },
                new ColorSchemeDefinition
                {
                    Name = "Monochrome",
                    PrimaryColor = "#78909c",
                    PrimaryDarkColor = "#546e7a",
                    AccentColor = "#b0bec5",
                    BackgroundColor = "#121212",
                    SurfaceColor = "#1e1e1e",
                    CardColor = "#2d2d2d"
                }
            };
        }
    }

    /// <summary>How a show's source episode is obtained.</summary>
    public enum DownloadMethod
    {
        /// <summary>Crunchy-DL (CRD) — downloads directly from Crunchyroll. Primary method.</summary>
        CRD,
        /// <summary>Nyaa RSS feed monitored via qBittorrent. Secondary method.</summary>
        RSS
    }

    public class CRDSettings
    {
        /// <summary>Path to the folder containing CRD.exe and Updater.exe.</summary>
        [JsonProperty("path")]
        public string Path { get; set; } = @"C:\JudasEncodingManager\CRD";

        /// <summary>When true, the built-in Updater.exe is run on startup.</summary>
        [JsonProperty("auto_update")]
        public bool AutoUpdate { get; set; } = false;

    }

    public class WeeklyShow
    {
        [JsonProperty("download_method")]
        public DownloadMethod DownloadMethod { get; set; } = DownloadMethod.CRD;

        /// <summary>
        /// Folder where CRD saves completed episode files for this show.
        /// JEM watches this folder on the release schedule.
        /// </summary>
        [JsonProperty("crd_output_path")]
        public string CrdOutputPath { get; set; } = "";

        /// <summary>
        /// Optional glob/wildcard used to match CRD episode files, e.g. "TowerOfGod*.mkv".
        /// Defaults to "*.mkv" when empty.
        /// </summary>
        [JsonProperty("crd_file_pattern")]
        public string CrdFilePattern { get; set; } = "";

        // ── Shared ──────────────────────────────────────────────────────────
        [JsonProperty("ini_script_name")]
        public string IniScriptName { get; set; } = "";

        [JsonProperty("output_torrent_title")]
        public string OutputTorrentTitle { get; set; } = "";

        [JsonProperty("output_file_title")]
        public string OutputFileTitle { get; set; } = "";

        [JsonProperty("season_number")]
        public int SeasonNumber { get; set; } = 1;

        [JsonProperty("number_of_episodes_to_remove_from_count")]
        public int NumberOfEpisodesToRemoveFromCount { get; set; } = 0;

        [JsonProperty("rss_feed")]
        public string RssFeed { get; set; } = "";

        [JsonProperty("autopost_on_trackers")]
        public bool AutopostOnTrackers { get; set; } = true;

        // New fields for scheduling and tracking
        [JsonProperty("release_day")]
        public DayOfWeek? ReleaseDay { get; set; }

        [JsonProperty("release_time")]
        public string ReleaseTime { get; set; } = "12:00";

        [JsonProperty("expected_episodes")]
        public int ExpectedEpisodes { get; set; } = 12;

        [JsonProperty("episodes_released")]
        public List<EpisodeRelease> EpisodesReleased { get; set; } = new();

        [JsonProperty("is_active")]
        public bool IsActive { get; set; } = true;

        [JsonProperty("custom_episode_regex")]
        public string CustomEpisodeRegex { get; set; } = "";

        [JsonProperty("source_group")]
        public string SourceGroup { get; set; } = "Erai-raws";

        // New property for uncensored releases
        [JsonProperty("is_uncensored")]
        public bool IsUncensored { get; set; } = false;
    }

    public class EpisodeRelease
    {
        [JsonProperty("episode_number")]
        public int EpisodeNumber { get; set; }

        [JsonProperty("version")]
        public int Version { get; set; } = 1;

        [JsonProperty("release_date")]
        public DateTime ReleaseDate { get; set; }

        [JsonProperty("source_filename")]
        public string SourceFilename { get; set; } = "";

        [JsonProperty("output_filename")]
        public string OutputFilename { get; set; } = "";
    }
}
