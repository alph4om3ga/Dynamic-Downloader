using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace JudasEncodingManager.Services
{
    public class QBittorrentService
    {
        private readonly HttpClient _localClient;
        private readonly HttpClient _seedboxClient;
        private string _localUrl = "";
        private string _seedboxUrl = "";
        private string _seedboxUsername = "";
        private string _seedboxPassword = "";
        private bool _localLoggedIn = false;
        private bool _seedboxLoggedIn = false;

        public QBittorrentService()
        {
            var handler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer(),
                UseCookies = true
            };
            _localClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };

            var seedboxHandler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer(),
                UseCookies = true
            };
            _seedboxClient = new HttpClient(seedboxHandler) { Timeout = TimeSpan.FromSeconds(30) };
        }

        public void Configure(string localUrl, string seedboxUrl, string seedboxUsername, string seedboxPassword)
        {
            _localUrl = localUrl.TrimEnd('/');
            _seedboxUrl = seedboxUrl.TrimEnd('/');
            _seedboxUsername = seedboxUsername;
            _seedboxPassword = seedboxPassword;
            _localLoggedIn = false;
            _seedboxLoggedIn = false;
        }

        private async Task<bool> LoginLocalAsync()
        {
            if (string.IsNullOrEmpty(_localUrl)) return false;

            // Always verify connection is still valid
            try
            {
                // Local qBittorrent usually doesn't require auth if accessed from localhost
                var response = await _localClient.GetAsync($"{_localUrl}/api/v2/app/version");
                _localLoggedIn = response.IsSuccessStatusCode;
                return _localLoggedIn;
            }
            catch
            {
                _localLoggedIn = false;
                return false;
            }
        }

        private async Task<bool> LoginSeedboxAsync()
        {
            if (string.IsNullOrEmpty(_seedboxUrl)) return false;

            try
            {
                // First check if already logged in by testing an API endpoint
                if (_seedboxLoggedIn)
                {
                    var testResponse = await _seedboxClient.GetAsync($"{_seedboxUrl}/api/v2/app/version");
                    if (testResponse.IsSuccessStatusCode)
                    {
                        return true;
                    }
                    // Session expired, need to re-login
                    _seedboxLoggedIn = false;
                }

                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("username", _seedboxUsername),
                    new KeyValuePair<string, string>("password", _seedboxPassword)
                });

                var response = await _seedboxClient.PostAsync($"{_seedboxUrl}/api/v2/auth/login", content);
                var result = await response.Content.ReadAsStringAsync();
                _seedboxLoggedIn = result.Contains("Ok") || response.IsSuccessStatusCode;
                return _seedboxLoggedIn;
            }
            catch
            {
                _seedboxLoggedIn = false;
                return false;
            }
        }

        public async Task<bool> AddTorrentAsync(string torrentUrl, string savePath, bool isLocal = true)
        {
            var client = isLocal ? _localClient : _seedboxClient;
            var baseUrl = isLocal ? _localUrl : _seedboxUrl;

            if (isLocal)
                await LoginLocalAsync();
            else
                await LoginSeedboxAsync();

            try
            {
                var content = new MultipartFormDataContent();
                content.Add(new StringContent(torrentUrl), "urls");
                content.Add(new StringContent(savePath), "savepath");
                content.Add(new StringContent("false"), "autoTMM"); // Disable auto torrent management to use our save path

                var response = await client.PostAsync($"{baseUrl}/api/v2/torrents/add", content);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> AddTorrentFileAsync(string torrentFilePath, string savePath, bool isLocal = true)
        {
            var client = isLocal ? _localClient : _seedboxClient;
            var baseUrl = isLocal ? _localUrl : _seedboxUrl;

            if (isLocal)
                await LoginLocalAsync();
            else
                await LoginSeedboxAsync();

            try
            {
                var content = new MultipartFormDataContent();
                var fileBytes = await File.ReadAllBytesAsync(torrentFilePath);
                var fileContent = new ByteArrayContent(fileBytes);
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-bittorrent");
                content.Add(fileContent, "torrents", Path.GetFileName(torrentFilePath));
                content.Add(new StringContent(savePath), "savepath");

                var response = await client.PostAsync($"{baseUrl}/api/v2/torrents/add", content);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<TorrentInfo?> GetTorrentInfoAsync(string hash, bool isLocal = true)
        {
            var client = isLocal ? _localClient : _seedboxClient;
            var baseUrl = isLocal ? _localUrl : _seedboxUrl;

            if (isLocal)
                await LoginLocalAsync();
            else
                await LoginSeedboxAsync();

            try
            {
                var response = await client.GetAsync($"{baseUrl}/api/v2/torrents/info?hashes={hash}");
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync();
                var torrents = JsonConvert.DeserializeObject<List<TorrentInfo>>(json);
                return torrents?.FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        public async Task<List<TorrentInfo>> GetAllTorrentsAsync(bool isLocal = true)
        {
            var client = isLocal ? _localClient : _seedboxClient;
            var baseUrl = isLocal ? _localUrl : _seedboxUrl;

            if (isLocal)
                await LoginLocalAsync();
            else
                await LoginSeedboxAsync();

            try
            {
                var response = await client.GetAsync($"{baseUrl}/api/v2/torrents/info");
                if (!response.IsSuccessStatusCode) return new List<TorrentInfo>();

                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<List<TorrentInfo>>(json) ?? new List<TorrentInfo>();
            }
            catch
            {
                return new List<TorrentInfo>();
            }
        }

        public async Task<bool> DeleteTorrentAsync(string hash, bool deleteFiles, bool isLocal = true)
        {
            var client = isLocal ? _localClient : _seedboxClient;
            var baseUrl = isLocal ? _localUrl : _seedboxUrl;

            if (isLocal)
                await LoginLocalAsync();
            else
                await LoginSeedboxAsync();

            try
            {
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("hashes", hash),
                    new KeyValuePair<string, string>("deleteFiles", deleteFiles.ToString().ToLower())
                });

                var response = await client.PostAsync($"{baseUrl}/api/v2/torrents/delete", content);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<string?> ExportTorrentAsync(string hash, bool isLocal = true)
        {
            var client = isLocal ? _localClient : _seedboxClient;
            var baseUrl = isLocal ? _localUrl : _seedboxUrl;

            if (isLocal)
                await LoginLocalAsync();
            else
                await LoginSeedboxAsync();

            try
            {
                var response = await client.GetAsync($"{baseUrl}/api/v2/torrents/export?hash={hash}");
                if (!response.IsSuccessStatusCode) return null;

                var bytes = await response.Content.ReadAsByteArrayAsync();
                var tempPath = Path.Combine(Path.GetTempPath(), $"{hash}.torrent");
                await File.WriteAllBytesAsync(tempPath, bytes);
                return tempPath;
            }
            catch
            {
                return null;
            }
        }

        public async Task<bool> IsConnectedAsync(bool isLocal = true)
        {
            var client = isLocal ? _localClient : _seedboxClient;
            var baseUrl = isLocal ? _localUrl : _seedboxUrl;

            if (string.IsNullOrEmpty(baseUrl)) return false;

            try
            {
                if (!isLocal)
                    await LoginSeedboxAsync();

                var response = await client.GetAsync($"{baseUrl}/api/v2/app/version");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<double> GetTorrentProgressAsync(string hash, bool isLocal = true)
        {
            var info = await GetTorrentInfoAsync(hash, isLocal);
            return info?.Progress ?? 0;
        }

        public async Task<bool> IsTorrentCompleteAsync(string hash, bool isLocal = true)
        {
            var info = await GetTorrentInfoAsync(hash, isLocal);
            return info?.Progress >= 1.0 || info?.State == "uploading" || info?.State == "stalledUP" || info?.State == "pausedUP";
        }
    }

    public class TorrentInfo
    {
        [JsonProperty("hash")]
        public string Hash { get; set; } = "";

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("size")]
        public long Size { get; set; }

        [JsonProperty("progress")]
        public double Progress { get; set; }

        [JsonProperty("state")]
        public string State { get; set; } = "";

        [JsonProperty("save_path")]
        public string SavePath { get; set; } = "";

        [JsonProperty("content_path")]
        public string ContentPath { get; set; } = "";

        [JsonProperty("dlspeed")]
        public long DownloadSpeed { get; set; }

        [JsonProperty("eta")]
        public int Eta { get; set; }
    }
}
