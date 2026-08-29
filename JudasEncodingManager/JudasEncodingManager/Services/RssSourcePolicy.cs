using System;
using System.Text.RegularExpressions;

namespace JudasEncodingManager.Services
{
    public static class RssSourcePolicy
    {
        /// <summary>
        /// Determines whether an RSS item belongs to the configured source.
        /// A Nyaa feed restricted by its uploader query parameter is authoritative,
        /// even when the uploader account and title group use different names.
        /// </summary>
        public static bool Matches(string feedUrl, string sourceGroup, string title)
        {
            if (string.IsNullOrWhiteSpace(sourceGroup))
                return true;

            if (title.Contains(sourceGroup, StringComparison.OrdinalIgnoreCase))
                return true;

            return HasNyaaUploaderFilter(feedUrl);
        }

        private static bool HasNyaaUploaderFilter(string feedUrl)
        {
            if (string.IsNullOrWhiteSpace(feedUrl))
                return false;

            return Regex.IsMatch(
                feedUrl,
                @"(?:^|[?&])u=[^&#\s]+",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
    }
}