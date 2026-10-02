using System;
using System.Text.RegularExpressions;

namespace JudasEncodingManager.Services
{
    public static class RssReleaseParser
    {
        public static (int? Episode, int Version) Parse(
            string title, string? customRegex = null, bool absoluteNumber = false)
        {
            int? episode = null;
            if (!string.IsNullOrEmpty(customRegex))
            {
                try
                {
                    var match = Regex.Match(title, customRegex);
                    if (match.Success && match.Groups.Count > 1 &&
                        int.TryParse(match.Groups[1].Value, out var number))
                        episode = number;
                }
                catch (ArgumentException) { }
            }

            // Prefer explicit episode markers over numbers in the show's title.
            var patterns = new[]
            {
                @"S\d+E(\d+)(?!\d)",
                @"E(\d+)(?:v\d+)?(?=[- _.\[\]() ]|$)",
                @"Episode\s*(\d+)",
                @"Ep\.?\s*(\d+)",
                @"#(\d+)",
                @" - (\d+)(?:v\d+)?(?=[ _.\[\]() ]|$)",
                @"[- _](\d{2,3})(?:v\d+)?(?=[- _.\[\]() ]|$)"
            };
            if (!episode.HasValue)
            {
                foreach (var pattern in patterns)
                {
                    var match = Regex.Match(title, pattern, RegexOptions.IgnoreCase);
                    if (match.Success && int.TryParse(match.Groups[1].Value, out var number))
                    {
                        episode = number;
                        break;
                    }
                }
            }

            var version = 1;
            var versionMatch = Regex.Match(title,
                @"(?:S\d+E|[- _])\d+v(\d+)(?=[- _.\[\]()]|$)",
                RegexOptions.IgnoreCase);
            if (versionMatch.Success && int.TryParse(versionMatch.Groups[1].Value, out var parsedVersion))
                version = parsedVersion;
            // Reject the full number, never truncate a four-digit episode to a
            // shorter match or substitute episode 1 for a seasonal show.
            if (!absoluteNumber && episode >= 1000)
                episode = null;
            return (episode, version);
        }
    }
}