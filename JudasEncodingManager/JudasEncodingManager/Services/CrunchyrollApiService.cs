using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using JudasEncodingManager.Models;
using Newtonsoft.Json;

namespace JudasEncodingManager.Services
{
    /// <summary>
    /// Crunchyroll v2 API client — authentication, series search, and season listing.
    /// Uses standard Crunchyroll web-client credentials; the user supplies their
    /// own email and password so JEM can obtain a Bearer token on their behalf.
    /// </summary>
    public class CrunchyrollApiService
    {
        // Public client ID used by the Crunchyroll web player.
        // This is not a secret — it is embedded in every page load.
        private const string ClientId     = "noaihdevm_6iyg0a8l0q";
        private const string ClientSecret = "";

        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        private string   _accessToken  = "";
        private string   _refreshToken = "";
        private DateTime _tokenExpiry  = DateTime.MinValue;

        public bool IsAuthenticated =>
            !string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiry;

        static CrunchyrollApiService()
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        }

        // ==================== AUTH ====================

        /// <summary>
        /// Authenticates with Crunchyroll using the user's email and password.
        /// Returns (true, "") on success or (false, errorMessage) on failure.
        /// </summary>
        public async Task<(bool ok, string error)> AuthenticateAsync(string email, string password)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                return (false, "Email and password are required.");

            try
            {
                var basicCreds = Convert.ToBase64String(
                    Encoding.ASCII.GetBytes($"{ClientId}:{ClientSecret}"));

                var req = new HttpRequestMessage(HttpMethod.Post,
                    "https://www.crunchyroll.com/auth/v1/token");
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicCreds);
                req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "password",
                    ["username"]   = email,
                    ["password"]   = password,
                    ["scope"]      = "offline_access"
                });

                var resp = await _http.SendAsync(req).ConfigureAwait(false);
                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!resp.IsSuccessStatusCode)
                    return (false, $"HTTP {(int)resp.StatusCode} — check your email/password.");

                var token = JsonConvert.DeserializeObject<CrunchyrollTokenResponse>(body);
                if (token == null || string.IsNullOrEmpty(token.AccessToken))
                    return (false, "Received an empty token — the API may have changed.");

                StoreToken(token);
                return (true, "");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Refreshes the access token silently if it has expired.
        /// Returns true when an active token is available.
        /// </summary>
        public async Task<bool> RefreshIfNeededAsync()
        {
            if (IsAuthenticated) return true;
            if (string.IsNullOrEmpty(_refreshToken)) return false;

            try
            {
                var basicCreds = Convert.ToBase64String(
                    Encoding.ASCII.GetBytes($"{ClientId}:{ClientSecret}"));

                var req = new HttpRequestMessage(HttpMethod.Post,
                    "https://www.crunchyroll.com/auth/v1/token");
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicCreds);
                req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"]    = "refresh_token",
                    ["refresh_token"] = _refreshToken,
                    ["scope"]         = "offline_access"
                });

                var resp = await _http.SendAsync(req).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return false;

                var body  = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                var token = JsonConvert.DeserializeObject<CrunchyrollTokenResponse>(body);
                if (token == null || string.IsNullOrEmpty(token.AccessToken)) return false;

                StoreToken(token);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void StoreToken(CrunchyrollTokenResponse token)
        {
            _accessToken  = token.AccessToken;
            _refreshToken = token.RefreshToken;
            // Subtract 30 s to avoid using a token right at expiry
            _tokenExpiry  = DateTime.UtcNow.AddSeconds(Math.Max(token.ExpiresIn - 30, 0));
        }

        // ==================== SEARCH ====================

        /// <summary>
        /// Searches Crunchyroll for series matching <paramref name="query"/>.
        /// Returns up to 20 results ordered by relevance.
        /// </summary>
        public async Task<(List<CrunchyrollSeries> results, string error)> SearchSeriesAsync(string query)
        {
            if (!await RefreshIfNeededAsync().ConfigureAwait(false))
                return (new(), "Not signed in — use 'Sign In' in General settings first.");

            try
            {
                var url = "https://www.crunchyroll.com/content/v2/discover/search" +
                          $"?q={Uri.EscapeDataString(query)}&n=20&type=series&locale=en-US";

                var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

                var resp = await _http.SendAsync(req).ConfigureAwait(false);
                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!resp.IsSuccessStatusCode)
                    return (new(), $"HTTP {(int)resp.StatusCode}");

                var raw = JsonConvert.DeserializeObject<CrunchyrollSearchResponse>(body);

                // Flatten all groups → deduplicate by ID → map to public model
                var results = (raw?.Data ?? new())
                    .SelectMany(g => g.Items)
                    .GroupBy(s => s.Id)
                    .Select(g => g.First())
                    .Where(s => !string.IsNullOrEmpty(s.Id) && !string.IsNullOrEmpty(s.Title))
                    .Select(s => new CrunchyrollSeries
                    {
                        Id          = s.Id,
                        Title       = s.Title,
                        SlugTitle   = s.SlugTitle,
                        Description = s.Description
                    })
                    .ToList();

                return (results, "");
            }
            catch (Exception ex)
            {
                return (new(), ex.Message);
            }
        }

        // ==================== SEASONS ====================

        /// <summary>
        /// Returns all seasons for a Crunchyroll series, ordered by season number.
        /// </summary>
        public async Task<(List<CrdSeasonOption> seasons, string error)> GetSeasonsAsync(string seriesId)
        {
            if (string.IsNullOrWhiteSpace(seriesId))
                return (new(), "Series ID is empty.");

            if (!await RefreshIfNeededAsync().ConfigureAwait(false))
                return (new(), "Not signed in — use 'Sign In' in General settings first.");

            try
            {
                var url = $"https://www.crunchyroll.com/content/v2/cms/series/{seriesId}/seasons?locale=en-US";

                var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

                var resp = await _http.SendAsync(req).ConfigureAwait(false);
                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!resp.IsSuccessStatusCode)
                    return (new(), $"HTTP {(int)resp.StatusCode} — check the Series ID.");

                var raw = JsonConvert.DeserializeObject<CrunchyrollSeasonsResponse>(body);
                var seasons = (raw?.Data ?? new())
                    .OrderBy(s => s.SeasonNumber)
                    .Select(s => new CrdSeasonOption
                    {
                        Id           = s.Id,
                        Title        = s.Title,
                        SeasonNumber = s.SeasonNumber,
                        EpisodeCount = s.EpisodeCount
                    })
                    .ToList();

                return (seasons, "");
            }
            catch (Exception ex)
            {
                return (new(), ex.Message);
            }
        }
    }
}
