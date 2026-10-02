using System;

namespace JudasEncodingManager.Services
{
    public static class EpisodeNumberingPolicy
    {
        public static int ToReleaseEpisode(int sourceEpisode, int offset) =>
            checked(sourceEpisode - offset);

        public static int RequireReleaseEpisode(int sourceEpisode, int offset)
        {
            var episode = ToReleaseEpisode(sourceEpisode, offset);
            if (episode <= 0)
                throw new InvalidOperationException(
                    $"Source episode {sourceEpisode} with offset {offset} produces invalid release episode {episode}.");
            return episode;
        }

        public static string Format(int episode, int season, bool absoluteNumber, int version) =>
            (absoluteNumber ? $"{episode:D2}" : $"S{season:D2}E{episode:D2}") +
            (version > 1 ? $"v{version}" : "");
    }
}