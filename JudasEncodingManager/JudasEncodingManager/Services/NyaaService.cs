using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using JudasEncodingManager.Models;

namespace JudasEncodingManager.Services
{
    public class NyaaService
    {
        private HttpClient _httpClient;
        private HttpClientHandler _httpHandler;
        private string _cookieDdlg = "";
        private string _cookieSession = "";
        private string _discordInviteLink = "";

        public NyaaService()
        {
            _httpHandler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer(),
                UseCookies = true
            };
            _httpClient = new HttpClient(_httpHandler);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        }

        public void Configure(string cookieDdlg, string cookieSession, string discordInviteLink)
        {
            _cookieDdlg = cookieDdlg;
            _cookieSession = cookieSession;
            _discordInviteLink = discordInviteLink;

            // Recreate HttpClient with fresh cookie container containing the configured cookies
            _httpHandler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer(),
                UseCookies = true
            };

            if (!string.IsNullOrEmpty(_cookieDdlg))
            {
                _httpHandler.CookieContainer.Add(new Uri("https://nyaa.si"), new Cookie("__ddlg", _cookieDdlg));
            }
            if (!string.IsNullOrEmpty(_cookieSession))
            {
                _httpHandler.CookieContainer.Add(new Uri("https://nyaa.si"), new Cookie("session", _cookieSession));
            }

            _httpClient = new HttpClient(_httpHandler);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        }

        public string GenerateDescription(QueueItem item, string descriptionTemplate, string sourceInfo)
        {
            var description = descriptionTemplate;

            // Use the output filename format for the title in description
            // Format: Show Title (English Name) - S01E03v2
            var titleForDescription = $"{item.Show.OutputTorrentTitle} - {item.EpisodeString}";
            
            // Replace placeholders
            description = description.Replace("@@TITLE@@", titleForDescription);
            
            // Use the actual source info from the queue item if available, otherwise use passed sourceInfo
            var actualSourceInfo = !string.IsNullOrEmpty(item.SourceInfoForDescription) 
                ? item.SourceInfoForDescription 
                : sourceInfo;
            description = description.Replace("@@SOURCE@@", actualSourceInfo);
            
            description = description.Replace("@@DISCORD_LINK@@", _discordInviteLink);

            // Use actual track info for audio description
            var audioDescription = item.AudioTracksDescription;
            if (string.IsNullOrEmpty(audioDescription))
                audioDescription = "Japanese";
            description = description.Replace("@@AUDIO_TRACKS@@", audioDescription);

            // Use actual track info for subtitle description (already ordered)
            var subsDescription = item.SubtitleTracksDescription;
            if (string.IsNullOrEmpty(subsDescription))
                subsDescription = "English - Multiple Languages";
            description = description.Replace("@@SUBS_TRACKS@@", subsDescription);

            // Screenshots
            var screenshotsMarkdown = "";
            foreach (var url in item.ScreenshotUrls)
            {
                screenshotsMarkdown += $"![we]({url})\n\n";
            }
            description = description.Replace("@@SCREENSHOTS@@", screenshotsMarkdown.TrimEnd());

            return description;
        }

        public async Task<NyaaPostResult> PostToNyaaAsync(QueueItem item, string torrentFilePath, string description, bool isHidden)
        {
            var result = new NyaaPostResult();

            try
            {
                // Create multipart form content
                var content = new MultipartFormDataContent();

                // Torrent file
                var torrentBytes = await File.ReadAllBytesAsync(torrentFilePath);
                var torrentContent = new ByteArrayContent(torrentBytes);
                torrentContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-bittorrent");
                content.Add(torrentContent, "torrent_file", Path.GetFileName(torrentFilePath));

                // Display name - use the dynamic TorrentDisplayName which includes actual track tags
                content.Add(new StringContent(item.TorrentDisplayName), "display_name");

                // Category: 1_2 = Anime - English-translated
                content.Add(new StringContent("1_2"), "category");

                // Information field - Discord invite link
                content.Add(new StringContent(_discordInviteLink), "information");

                // Description
                content.Add(new StringContent(description), "description");

                // Flags based on isHidden parameter
                if (isHidden)
                {
                    // Hidden release: not visible to public, use for testing/review
                    content.Add(new StringContent("0"), "is_remake");    // NOT remake
                    content.Add(new StringContent("1"), "is_hidden");    // HIDDEN
                    content.Add(new StringContent("0"), "is_complete");  // Not complete
                }
                else
                {
                    // Normal public release: use Remake flag
                    content.Add(new StringContent("1"), "is_remake");    // Remake flag checked
                    content.Add(new StringContent("0"), "is_hidden");    // Not hidden
                    content.Add(new StringContent("0"), "is_complete");  // Not complete (weekly release)
                }

                // Add cookies to request
                _httpClient.DefaultRequestHeaders.Remove("Cookie");
                var cookieHeader = "";
                if (!string.IsNullOrEmpty(_cookieDdlg))
                    cookieHeader += $"__ddlg={_cookieDdlg}; ";
                if (!string.IsNullOrEmpty(_cookieSession))
                    cookieHeader += $"session={_cookieSession}";
                if (!string.IsNullOrEmpty(cookieHeader))
                    _httpClient.DefaultRequestHeaders.Add("Cookie", cookieHeader.TrimEnd(' ', ';'));

                var response = await _httpClient.PostAsync("https://nyaa.si/upload", content);
                
                if (response.IsSuccessStatusCode)
                {
                    // Check if redirected to the new torrent page
                    var responseUrl = response.RequestMessage?.RequestUri?.ToString();
                    if (responseUrl?.Contains("/view/") == true)
                    {
                        result.Success = true;
                        result.Url = responseUrl;
                    }
                    else
                    {
                        // Try to get URL from response
                        var responseBody = await response.Content.ReadAsStringAsync();
                        result.Success = true;
                        result.Url = response.Headers.Location?.ToString() ?? responseUrl ?? "Posted successfully";
                    }
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    result.Success = false;
                    result.Message = $"HTTP {response.StatusCode}: {errorBody}";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
            }

            return result;
        }

        public async Task<bool> TestLoginAsync()
        {
            try
            {
                _httpClient.DefaultRequestHeaders.Remove("Cookie");
                var cookieHeader = "";
                if (!string.IsNullOrEmpty(_cookieDdlg))
                    cookieHeader += $"__ddlg={_cookieDdlg}; ";
                if (!string.IsNullOrEmpty(_cookieSession))
                    cookieHeader += $"session={_cookieSession}";
                if (!string.IsNullOrEmpty(cookieHeader))
                    _httpClient.DefaultRequestHeaders.Add("Cookie", cookieHeader.TrimEnd(' ', ';'));

                var response = await _httpClient.GetAsync("https://nyaa.si/upload");
                var content = await response.Content.ReadAsStringAsync();

                // Check if we can access the upload page (logged in)
                return content.Contains("Upload Torrent") || content.Contains("Submit");
            }
            catch
            {
                return false;
            }
        }
    }

    public class NyaaPostResult
    {
        public bool Success { get; set; }
        public string Url { get; set; } = "";
        public string Message { get; set; } = "";
    }
}
