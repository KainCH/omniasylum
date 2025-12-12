using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using OmniForge.Core.Entities;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Configuration;

using Microsoft.Extensions.Logging; // Added
using OmniForge.Core.Utilities; // Added

namespace OmniForge.Infrastructure.Services
{
    public class TwitchAuthService : ITwitchAuthService
    {
        private readonly HttpClient _httpClient;
        private readonly TwitchSettings _settings;
        private readonly ILogger<TwitchAuthService> _logger; // Added

        public TwitchAuthService(HttpClient httpClient, IOptions<TwitchSettings> settings, ILogger<TwitchAuthService> logger) // Updated
        {
            _httpClient = httpClient;
            _settings = settings.Value;
            _logger = logger;
        }

        public string GetAuthorizationUrl(string redirectUri)
        {
            // OAuth scopes for TwitchAPI + EventSub (not IRC)
            // See: https://dev.twitch.tv/docs/authentication/scopes/
            var scopes = new List<string>
            {
                // OIDC
                "openid",

                // User profile
                "user:read:email",

                // EventSub chat (requires all three for full chat functionality)
                "user:read:chat",      // Read chat messages via EventSub
                "user:write:chat",     // Send chat messages via Helix API
                "user:bot",            // Required for EventSub chat subscriptions

                // Whispers (optional - for DM functionality)
                "user:manage:whispers",

                // Channel features
                "channel:read:subscriptions",
                "channel:read:redemptions",
                "channel:manage:polls",

                // Moderation & followers
                "moderator:read:followers",
                "moderator:read:automod_settings",
                "moderator:manage:automod",

                // Bits & clips
                "bits:read",
                "clips:edit"
            };

            var scopeString = string.Join(" ", scopes);
            var encodedScopes = System.Net.WebUtility.UrlEncode(scopeString);
            var encodedRedirect = System.Net.WebUtility.UrlEncode(redirectUri);

            return $"https://id.twitch.tv/oauth2/authorize?client_id={_settings.ClientId}&redirect_uri={encodedRedirect}&response_type=code&scope={encodedScopes}&force_verify=true";
        }

        public async Task<TwitchTokenResponse?> ExchangeCodeForTokenAsync(string code, string redirectUri)
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", _settings.ClientId),
                new KeyValuePair<string, string>("client_secret", _settings.ClientSecret),
                new KeyValuePair<string, string>("code", code),
                new KeyValuePair<string, string>("grant_type", "authorization_code"),
                new KeyValuePair<string, string>("redirect_uri", redirectUri)
            });

            var response = await _httpClient.PostAsync("https://id.twitch.tv/oauth2/token", content);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var tokenData = JsonSerializer.Deserialize<TwitchTokenResponseInternal>(json, options);

            if (tokenData == null) return null;

            return new TwitchTokenResponse
            {
                AccessToken = tokenData.AccessToken,
                RefreshToken = tokenData.RefreshToken,
                IdToken = tokenData.IdToken,
                ExpiresIn = tokenData.ExpiresIn,
                TokenType = tokenData.TokenType
            };
        }

        public async Task<TwitchUserInfo?> GetUserInfoAsync(string accessToken, string clientId)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.twitch.tv/helix/users");
            request.Headers.Add("Authorization", $"Bearer {accessToken}");
            request.Headers.Add("Client-Id", clientId);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var userData = JsonSerializer.Deserialize<TwitchUserResponseInternal>(json, options);

            if (userData?.Data == null || userData.Data.Count == 0) return null;

            var user = userData.Data[0];
            return new TwitchUserInfo
            {
                Id = user.Id,
                Login = user.Login,
                DisplayName = user.DisplayName,
                Email = user.Email,
                ProfileImageUrl = user.ProfileImageUrl
            };
        }

        public async Task<IReadOnlyList<string>> GetTokenScopesAsync(string accessToken)
        {
            var validation = await ValidateTokenAsync(accessToken);
            return validation?.Scopes ?? Array.Empty<string>();
        }

        public async Task<bool> HasScopesAsync(string accessToken, IEnumerable<string> requiredScopes)
        {
            var validation = await ValidateTokenAsync(accessToken);
            var scopes = validation?.Scopes ?? Array.Empty<string>();
            var set = scopes.ToHashSet(StringComparer.OrdinalIgnoreCase);
            return requiredScopes.All(rs => set.Contains(rs));
        }

        public async Task<TokenValidationResult?> ValidateTokenAsync(string accessToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://id.twitch.tv/oauth2/validate");
            request.Headers.Add("Authorization", $"OAuth {accessToken}");

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var validation = await response.Content.ReadFromJsonAsync<TokenValidationResponse>();
            if (validation == null) return null;

            return new TokenValidationResult
            {
                ClientId = validation.ClientId,
                UserId = validation.UserId,
                Scopes = validation.Scopes,
                ExpiresIn = validation.ExpiresIn
            };
        }

        public async Task<TwitchTokenResponse?> RefreshTokenAsync(string refreshToken)
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", _settings.ClientId),
                new KeyValuePair<string, string>("client_secret", _settings.ClientSecret),
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
                new KeyValuePair<string, string>("refresh_token", refreshToken)
            });

            var response = await _httpClient.PostAsync("https://id.twitch.tv/oauth2/token", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to refresh Twitch token. Status: {StatusCode}, Response: {Response}", response.StatusCode, LogSanitizer.Sanitize(errorContent));
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var tokenData = JsonSerializer.Deserialize<TwitchTokenResponseInternal>(json, options);

            if (tokenData == null) return null;

            return new TwitchTokenResponse
            {
                AccessToken = tokenData.AccessToken,
                RefreshToken = tokenData.RefreshToken,
                IdToken = tokenData.IdToken,
                ExpiresIn = tokenData.ExpiresIn,
                TokenType = tokenData.TokenType
            };
        }

        public async Task<string?> GetOidcKeysAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("https://id.twitch.tv/oauth2/keys");
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to fetch Twitch OIDC keys. Status: {StatusCode}", response.StatusCode);
                    return null;
                }
                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching Twitch OIDC keys");
                return null;
            }
        }

        // Internal classes for JSON deserialization
        private class TwitchTokenResponseInternal
        {
            [JsonPropertyName("access_token")]
            public string AccessToken { get; set; } = "";
            [JsonPropertyName("refresh_token")]
            public string RefreshToken { get; set; } = "";
            [JsonPropertyName("id_token")]
            public string IdToken { get; set; } = "";
            [JsonPropertyName("expires_in")]
            public int ExpiresIn { get; set; }
            [JsonPropertyName("token_type")]
            public string TokenType { get; set; } = "";
        }

        private class TokenValidationResponse
        {
            [JsonPropertyName("client_id")]
            public string ClientId { get; set; } = string.Empty;

            [JsonPropertyName("scopes")]
            public List<string> Scopes { get; set; } = new();

            [JsonPropertyName("user_id")]
            public string UserId { get; set; } = string.Empty;

            [JsonPropertyName("expires_in")]
            public int ExpiresIn { get; set; }
        }

        private class TwitchUserResponseInternal
        {
            [JsonPropertyName("data")]
            public List<TwitchUserInternal> Data { get; set; } = new();
        }

        private class TwitchUserInternal
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = "";
            [JsonPropertyName("login")]
            public string Login { get; set; } = "";
            [JsonPropertyName("display_name")]
            public string DisplayName { get; set; } = "";
            [JsonPropertyName("email")]
            public string Email { get; set; } = "";
            [JsonPropertyName("profile_image_url")]
            public string ProfileImageUrl { get; set; } = "";
        }
    }
}
