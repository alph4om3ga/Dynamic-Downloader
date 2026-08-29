using System.Collections.Generic;
using System.Linq;

namespace JudasEncodingManager.Services
{
    public static class EpisodeHistoryPolicy
    {
        public static int GetNextExpectedEpisode(
            IEnumerable<int> releasedEpisodeNumbers,
            int expectedEpisodes)
        {
            var validEpisodes = releasedEpisodeNumbers.Where(episodeNumber =>
                episodeNumber > 0 &&
                (expectedEpisodes <= 0 || episodeNumber <= expectedEpisodes));

            return validEpisodes.Any() ? validEpisodes.Max() + 1 : 1;
        }
    }
}