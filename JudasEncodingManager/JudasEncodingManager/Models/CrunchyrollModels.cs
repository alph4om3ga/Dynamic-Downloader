using System.Collections.Generic;
using Newtonsoft.Json;

namespace JudasEncodingManager.Models
{
    // ── Public models (used by ViewModels) ────────────────────────────────────

    /// <summary>A Crunchyroll season option shown in the season ComboBox.</summary>
    public class CrdSeasonOption
    {
        public string Id           { get; set; } = "";
        public string Title        { get; set; } = "";
        public int    SeasonNumber { get; set; }
        public int    EpisodeCount { get; set; }

        /// <summary>Human-readable label shown in the dropdown.</summary>
        public string DisplayText =>
            EpisodeCount > 0
                ? $"S{SeasonNumber}: {Title} ({EpisodeCount} ep)"
                : $"S{SeasonNumber}: {Title}";

        public override string ToString() => DisplayText;
    }

    /// <summary>A Crunchyroll series returned from a search.</summary>
    public class CrunchyrollSeries
    {
        public string Id          { get; set; } = "";
        public string Title       { get; set; } = "";
        public string SlugTitle   { get; set; } = "";
        public string Description { get; set; } = "";

        /// <summary>Display text in the search results list.</summary>
        public string DisplayLine => $"{Title}  [{Id}]";
    }

    // ── Internal DTOs for the Crunchyroll v2 API ─────────────────────────────

    internal class CrunchyrollTokenResponse
    {
        [JsonProperty("access_token")]  public string AccessToken  { get; set; } = "";
        [JsonProperty("refresh_token")] public string RefreshToken { get; set; } = "";
        [JsonProperty("expires_in")]    public int    ExpiresIn    { get; set; }
    }

    internal class CrunchyrollSearchResponse
    {
        [JsonProperty("data")] public List<CrunchyrollSearchGroup> Data { get; set; } = new();
    }

    internal class CrunchyrollSearchGroup
    {
        [JsonProperty("type")]  public string                   Type  { get; set; } = "";
        [JsonProperty("count")] public int                      Count { get; set; }
        [JsonProperty("items")] public List<CrunchyrollSeriesRaw> Items { get; set; } = new();
    }

    internal class CrunchyrollSeriesRaw
    {
        [JsonProperty("id")]          public string Id          { get; set; } = "";
        [JsonProperty("title")]       public string Title       { get; set; } = "";
        [JsonProperty("slug_title")]  public string SlugTitle   { get; set; } = "";
        [JsonProperty("description")] public string Description { get; set; } = "";
    }

    internal class CrunchyrollSeasonsResponse
    {
        [JsonProperty("data")] public List<CrunchyrollSeasonRaw> Data { get; set; } = new();
    }

    internal class CrunchyrollSeasonRaw
    {
        [JsonProperty("id")]            public string Id           { get; set; } = "";
        [JsonProperty("title")]         public string Title        { get; set; } = "";
        [JsonProperty("season_number")] public int    SeasonNumber { get; set; }
        [JsonProperty("episode_count")] public int    EpisodeCount { get; set; }
    }
}
