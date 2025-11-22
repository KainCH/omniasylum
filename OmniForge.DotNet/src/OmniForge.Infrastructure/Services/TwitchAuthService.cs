using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Configuration;

namespace OmniForge.Infrastructure.Services
{
    public class TwitchAuthService : ITwitchAuthService
    {
        private readonly HttpClient _httpClient;
        private readonly TwitchSettings _settings;

        public TwitchAuthService(HttpClient httpClient, IOptions<TwitchSettings> settings)
        {
            _httpClient = httpClient;
            _settings = settings.Value;
        }

        public string GetAuthorizationUrl(string redirectUri)
        {
            var scopes = new List<string>
            {
                "user:read:email",
                "chat:read",
                "chat:edit",
                "user:manage:whispers",
                "channel:read:subscriptions",
                "channel:read:redemptions",
                "moderator:read:followers",
                "bits:read",
                "clips:edit"
            };

            var scopeString = string.Join(" ", scopes);
            var encodedScopes = System.Net.WebUtility.UrlEncode(scopeString);
            var encodedRedirect = System.Net.WebUtility.UrlEncode(redirectUri);

            return $"https://id.twitch.tv/oauth2/authorize?client_id={_settings.ClientId}&redirect_uri={encodedRedirect}&response_type=code&scope={encodedScopes}";
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
                ExpiresIn = tokenData.ExpiresIn,
                TokenType = tokenData.TokenType
            };
        }

        // Internal classes for JSON deserialization
        private class TwitchTokenResponseInternal
        {
            [JsonPropertyName("access_token")]
            public string AccessToken { get; set; } = "";
            [JsonPropertyName("refresh_token")]
            public string RefreshToken { get; set; } = "";
            [JsonPropertyName("expires_in")]
            public int ExpiresIn { get; set; }
            [JsonPropertyName("token_type")]
            public string TokenType { get; set; } = "";
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
