using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using JudasEncodingManager.Models;

namespace JudasEncodingManager.Services
{
    public class RssService
    {
        private readonly HttpClient _httpClient;

        public RssService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task<List<RssItem>> FetchFeedAsync(string feedUrl)
        {
            var items = new List<RssItem>();

            try
            {
                var response = await _httpClient.GetStringAsync(feedUrl);
                var xml = XDocument.Parse(response);

                // Handle both standard RSS and Nyaa's namespace
                XNamespace nyaa = "https://nyaa.si/xmlns/nyaa";

                foreach (var item in xml.Descendants("item"))
                {
                    var rssItem = new RssItem
                    {
                        Title = item.Element("title")?.Value ?? "",
                        Link = item.Element("link")?.Value ?? "",
                        TorrentUrl = item.Element("link")?.Value ?? "",
                        InfoHash = item.Element(nyaa + "infoHash")?.Value ?? "",
                    };

                    // Parse size if available
                    var sizeStr = item.Element(nyaa + "size")?.Value;
                    if (!string.IsNullOrEmpty(sizeStr))
                    {
                        rssItem.Size = ParseSize(sizeStr);
                    }

                    // Parse publish date
                    var pubDateStr = item.Element("pubDate")?.Value;
                    if (DateTime.TryParse(pubDateStr, out var pubDate))
                    {
                        rssItem.PublishDate = pubDate;
                    }

                    items.Add(rssItem);
                }
            }
            catch (Exception)
            {
                // Return empty list on error
            }

            return items;
        }

        public int? ExtractEpisodeNumber(string title, string? customRegex = null)
        {
            // Try custom regex first if provided
            if (!string.IsNullOrEmpty(customRegex))
            {
                try
                {
                    var customMatch = Regex.Match(title, customRegex);
                    if (customMatch.Success && customMatch.Groups.Count > 1)
                    {
                        if (int.TryParse(customMatch.Groups[1].Value, out int epNum))
                        {
                            return epNum;
                        }
                    }
                }
                catch
                {
                    // Invalid regex, continue with default patterns
                }
            }

            // Standard patterns (same as PowerShell script)
            var patterns = new[]
            {
                @"- (\d+) \[",           // Erai naming: Show - 01 [1080p]
                @"- (\d+) END \[",       // Erai last ep: Show - 12 END [1080p]
                @"- (\d+) \(",           // SubsPlease: Show - 01 (1080p)
                @"- (\d+) -",            // AniDB: Show - 01 - Episode Title
                @"S\d+E(\d+)",           // TVDB: Show S01E01
                @"Episode (\d+)",        // Episode 01
                @"Ep\.? ?(\d+)",         // Ep01 or Ep. 01
                @"E(\d+)",               // E01
                @"#(\d+)",               // #01
                @"\[(\d+)\]",            // [01]
                @" (\d{2,3}) ",          // Space-padded number
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(title, pattern, RegexOptions.IgnoreCase);
                if (match.Success && match.Groups.Count > 1)
                {
                    if (int.TryParse(match.Groups[1].Value, out int epNum))
                    {
                        return epNum;
                    }
                }
            }

            return null;
        }

        public bool IsV2OrRepack(string title)
        {
            var patterns = new[]
            {
                @"v2",
                @"v3",
                @"v4",
                @"repack",
                @"fixed",
                @"proper"
            };

            return patterns.Any(p => Regex.IsMatch(title, p, RegexOptions.IgnoreCase));
        }

        public int ExtractVersion(string title)
        {
            // Check for explicit version number
            var versionMatch = Regex.Match(title, @"v(\d+)", RegexOptions.IgnoreCase);
            if (versionMatch.Success && int.TryParse(versionMatch.Groups[1].Value, out int version))
            {
                return version;
            }

            // Check for repack/fixed/proper (treat as v2)
            if (Regex.IsMatch(title, @"repack|fixed|proper", RegexOptions.IgnoreCase))
            {
                return 2;
            }

            return 1;
        }

        public async Task<RssItem?> FindNewEpisodeAsync(WeeklyShow show, List<EpisodeRelease> releasedEpisodes)
        {
            if (string.IsNullOrWhiteSpace(show.RssFeed)) return null;

            var feedItems = await FetchFeedAsync(show.RssFeed);

            foreach (var item in feedItems.OrderByDescending(i => i.PublishDate))
            {
                var episodeNum = ExtractEpisodeNumber(item.Title, show.CustomEpisodeRegex);
                if (!episodeNum.HasValue) continue;

                // Apply episode offset
                var adjustedEpNum = episodeNum.Value + show.NumberOfEpisodesToRemoveFromCount;
                if (adjustedEpNum <= 0) continue;

                var version = ExtractVersion(item.Title);

                // Check if we already have this episode/version
                var existing = releasedEpisodes.FirstOrDefault(e => e.EpisodeNumber == adjustedEpNum);
                
                if (existing == null)
                {
                    // New episode
                    return item;
                }
                else if (version > existing.Version)
                {
                    // New version (v2/repack)
                    return item;
                }
            }

            return null;
        }

        private long ParseSize(string sizeStr)
        {
            try
            {
                var match = Regex.Match(sizeStr, @"([\d.]+)\s*(GiB|MiB|KiB|GB|MB|KB|B)?", RegexOptions.IgnoreCase);
                if (!match.Success) return 0;

                var value = double.Parse(match.Groups[1].Value);
                var unit = match.Groups[2].Value.ToUpper();

                return unit switch
                {
                    "GIB" or "GB" => (long)(value * 1024 * 1024 * 1024),
                    "MIB" or "MB" => (long)(value * 1024 * 1024),
                    "KIB" or "KB" => (long)(value * 1024),
                    _ => (long)value
                };
            }
            catch
            {
                return 0;
            }
        }
    }
}
