namespace JudasEncodingManager.Services
{
    /// <summary>
    /// Reports which qBittorrent control command succeeded, or why stopping
    /// a torrent could not be requested.
    /// </summary>
    public sealed class TorrentStopResult
    {
        public bool Success { get; private init; }
        public string Command { get; private init; } = "";
        public string Details { get; private init; } = "";

        public static TorrentStopResult Succeeded(string command)
        {
            return new TorrentStopResult
            {
                Success = true,
                Command = command,
                Details = $"qBittorrent accepted the {command} command."
            };
        }

        public static TorrentStopResult Failed(string details)
        {
            return new TorrentStopResult
            {
                Success = false,
                Details = details
            };
        }
    }
}