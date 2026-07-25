using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using JudasEncodingManager.Models;

namespace JudasEncodingManager.Services
{
    public class DiscordService
    {
        private readonly HttpClient _httpClient;
        private string _webhookUrl = "";
        private string _machineName = "";
        private bool _isTestMode = false;

        public DiscordService()
        {
            _httpClient = new HttpClient();
        }

        public void Configure(string webhookUrl, string machineName, bool isTestMode = false)
        {
            _webhookUrl = webhookUrl;
            _machineName = machineName;
            _isTestMode = isTestMode;
        }

        private string GetBotUsername()
        {
            if (!string.IsNullOrEmpty(_machineName))
                return $"Judas Encoder - {_machineName}";
            return "Judas Encoder";
        }

        public async Task SendMessageAsync(string message, string? title = null, DiscordEmbedColor color = DiscordEmbedColor.Blue)
        {
            if (string.IsNullOrWhiteSpace(_webhookUrl)) return;

            try
            {
                var prefix = _isTestMode ? "🧪 [TEST] " : "";
                
                var payload = new
                {
                    embeds = new[]
                    {
                        new
                        {
                            title = title,
                            description = prefix + message,
                            color = (int)color,
                            timestamp = DateTime.UtcNow.ToString("o")
                        }
                    },
                    username = GetBotUsername()
                };

                var json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                await _httpClient.PostAsync(_webhookUrl, content);
            }
            catch (Exception)
            {
                // Silently fail - we don't want Discord errors to break the pipeline
            }
        }

        /// <summary>
        /// Episode grabbed from RSS feed and added to queue
        /// </summary>
        public async Task SendEpisodeGrabbedAsync(QueueItem item)
        {
            var versionStr = item.Version > 1 ? $"v{item.Version}" : "";
            await SendMessageAsync(
                $"📥 **{item.Show.OutputFileTitle}** - {item.EpisodeString}\nSource: `{item.SourceFileName}`",
                "Episode Grabbed",
                DiscordEmbedColor.Blue);
        }

        /// <summary>
        /// Download completed, file ready for encoding
        /// </summary>
        public async Task SendDownloadCompleteAsync(QueueItem item)
        {
            await SendMessageAsync(
                $"✅ **{item.Show.OutputFileTitle}** - {item.EpisodeString}\nSize: `{item.SourceFileSizeFormatted}`",
                "Download Complete",
                DiscordEmbedColor.Green);
        }

        /// <summary>
        /// Encoding has started
        /// </summary>
        public async Task SendEncodingStartedAsync(QueueItem item, bool isQuickEncode)
        {
            var encodeType = isQuickEncode ? "Quick Test (5 min)" : "Full Episode";
            await SendMessageAsync(
                $"🎬 **{item.Show.OutputFileTitle}** - {item.EpisodeString}\nType: `{encodeType}`",
                "Encoding Started",
                DiscordEmbedColor.Blue);
        }

        /// <summary>
        /// Encoding completed successfully
        /// </summary>
        public async Task SendEncodingCompleteAsync(QueueItem item, TimeSpan duration, string fileSize)
        {
            await SendMessageAsync(
                $"✅ **{item.Show.OutputFileTitle}** - {item.EpisodeString}\nDuration: `{duration:hh\\:mm\\:ss}` | Size: `{fileSize}`",
                "Encoding Complete",
                DiscordEmbedColor.Green);
        }

        /// <summary>
        /// FTP upload started
        /// </summary>
        public async Task SendUploadStartedAsync(QueueItem item)
        {
            await SendMessageAsync(
                $"📤 **{item.Show.OutputFileTitle}** - {item.EpisodeString}",
                "Upload Started",
                DiscordEmbedColor.Blue);
        }

        /// <summary>
        /// FTP upload completed
        /// </summary>
        public async Task SendUploadCompleteAsync(QueueItem item)
        {
            await SendMessageAsync(
                $"✅ **{item.Show.OutputFileTitle}** - {item.EpisodeString}",
                "Upload Complete",
                DiscordEmbedColor.Green);
        }

        /// <summary>
        /// Torrent posted to Nyaa
        /// </summary>
        public async Task SendNyaaPostedAsync(QueueItem item, string nyaaUrl, bool isHidden)
        {
            var visibility = isHidden ? "🔒 Hidden" : "🌐 Public";
            await SendMessageAsync(
                $"🎉 **{item.TorrentDisplayName}**\n{visibility}\n{nyaaUrl}",
                "Torrent Posted!",
                DiscordEmbedColor.Purple);
        }

        /// <summary>
        /// Error occurred during processing
        /// </summary>
        public async Task SendErrorAsync(QueueItem item, string error)
        {
            await SendMessageAsync(
                $"❌ **{item.Show.OutputFileTitle}** - {item.EpisodeString}\n```{error}```",
                "Error - Queue Paused",
                DiscordEmbedColor.Red);
        }

        /// <summary>
        /// Warning message
        /// </summary>
        public async Task SendWarningAsync(string message)
        {
            await SendMessageAsync(message, "Warning", DiscordEmbedColor.Orange);
        }

        /// <summary>
        /// Queue resumed after pause
        /// </summary>
        public async Task SendQueueResumedAsync()
        {
            await SendMessageAsync("▶️ Queue has been resumed.", "Queue Resumed", DiscordEmbedColor.Green);
        }

        /// <summary>
        /// Queue paused
        /// </summary>
        public async Task SendQueuePausedAsync(string reason)
        {
            await SendMessageAsync($"⏸️ {reason}", "Queue Paused", DiscordEmbedColor.Orange);
        }

        /// <summary>
        /// Full pipeline completed successfully
        /// </summary>
        public async Task SendPipelineCompleteAsync(QueueItem item, string nyaaUrl, bool isHidden)
        {
            var visibility = isHidden ? "Hidden" : "Public";
            var totalTime = item.CompletedAt.HasValue && item.StartedAt.HasValue 
                ? (item.CompletedAt.Value - item.StartedAt.Value).ToString(@"hh\:mm\:ss")
                : "Unknown";
            
            await SendMessageAsync(
                $"🏁 **{item.TorrentDisplayName}**\nVisibility: `{visibility}`\nTotal Time: `{totalTime}`\n{nyaaUrl}",
                "Release Complete!",
                DiscordEmbedColor.Purple);
        }
    }

    public enum DiscordEmbedColor
    {
        Blue = 3447003,
        Green = 3066993,
        Red = 15158332,
        Orange = 15105570,
        Purple = 10181046,
        Gray = 9807270
    }
}
