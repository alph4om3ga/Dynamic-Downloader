using System;

namespace JudasEncodingManager.Services
{
    /// <summary>
    /// qBittorrent's WebUI uses paused states to represent a torrent that has
    /// stopped. A few compatible WebUI implementations use stopped states
    /// instead, so this policy is the single source of truth for both forms.
    /// </summary>
    public static class QBittorrentStatePolicy
    {
        public static bool IsStoppedState(string? state)
        {
            return state is not null &&
                   (state.Equals("pausedDL", StringComparison.OrdinalIgnoreCase) ||
                    state.Equals("pausedUP", StringComparison.OrdinalIgnoreCase) ||
                    state.Equals("stoppedDL", StringComparison.OrdinalIgnoreCase) ||
                    state.Equals("stoppedUP", StringComparison.OrdinalIgnoreCase) ||
                    state.Equals("stopped", StringComparison.OrdinalIgnoreCase));
        }
    }
}