using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniForge.Core.Entities;
using OmniForge.Core.Exceptions;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Utilities;
using OmniForge.Infrastructure.Configuration;
using OmniForge.Infrastructure.Interfaces;
using TwitchLib.Api;
using TwitchLib.Api.Core.Enums;
using TwitchLib.Api.Helix.Models.ChannelPoints.CreateCustomReward;
using TwitchLib.Api.Helix.Models.Moderation.AutomodSettings;

namespace OmniForge.Infrastructure.Services
{
    public class TwitchApiService : ITwitchApiService
    {
        private readonly IUserRepository _userRepository;
        private readonly ITwitchAuthService _authService;
        private readonly IBotCredentialRepository _botCredentialRepository;
        private readonly IConfiguration _configuration;
        private readonly TwitchSettings _twitchSettings;
        private readonly ITwitchHelixWrapper _helixWrapper;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<TwitchApiService> _logger;

        public TwitchApiService(
            IUserRepository userRepository,
            ITwitchAuthService authService,
            IBotCredentialRepository botCredentialRepository,
            IConfiguration configuration,
            IOptions<TwitchSettings> twitchSettings,
            ITwitchHelixWrapper helixWrapper,
            IHttpClientFactory httpClientFactory,
            ILogger<TwitchApiService> logger)
        {
            _userRepository = userRepository;
            _authService = authService;
            _botCredentialRepository = botCredentialRepository;
            _configuration = configuration;
            _twitchSettings = twitchSettings.Value;
            _helixWrapper = helixWrapper;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        private async Task<(User User, bool Refreshed)> EnsureUserTokenValidAsync(string userId)
        {
            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) throw new Exception("User not found");

            bool refreshed = false;
            // Check if token needs refresh (buffer of 5 minutes)
            if (user.TokenExpiry <= DateTimeOffset.UtcNow.AddMinutes(5))
            {
                _logger.LogInformation("Refreshing token for user {UserId}", LogSanitizer.Sanitize(userId));
                var newToken = await _authService.RefreshTokenAsync(user.RefreshToken);
                if (newToken != null)
                {
                    user.AccessToken = newToken.AccessToken;
                    user.RefreshToken = newToken.RefreshToken;
                    user.TokenExpiry = DateTimeOffset.UtcNow.AddSeconds(newToken.ExpiresIn);
                    await _userRepository.SaveUserAsync(user);
                    refreshed = true;
                }
                else
                {
                    _logger.LogWarning("Failed to refresh token for user {UserId}", LogSanitizer.Sanitize(userId));
                    throw new ReauthRequiredException("Twitch authentication expired. Please sign in again.");
                }
            }

            return (user, refreshed);
        }

        public async Task<TwitchModeratorsResponse> GetModeratorsAsync(string broadcasterId, string broadcasterAccessToken, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("🔎 Looking up channel moderators via Helix: broadcaster_id={BroadcasterId}", LogSanitizer.Sanitize(broadcasterId));

                var clientId = _twitchSettings.ClientId;
                if (string.IsNullOrEmpty(clientId)) throw new Exception("Twitch ClientId is not configured");

                _logger.LogInformation("🪪 Helix Get Moderators using client_id={ClientId}", LogSanitizer.Sanitize(clientId));

                var client = _httpClientFactory.CreateClient();

                var allModerators = new List<TwitchModeratorDto>();
                string? cursor = null;

                // Helix max is 100 per page. Keep paging until done or we hit a reasonable limit.
                for (var page = 0; page < 10; page++)
                {
                    var url = $"https://api.twitch.tv/helix/moderation/moderators?broadcaster_id={Uri.EscapeDataString(broadcasterId)}&first=100";
                    if (!string.IsNullOrEmpty(cursor))
                    {
                        url += $"&after={Uri.EscapeDataString(cursor)}";
                    }

                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Add("Client-Id", clientId);
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", broadcasterAccessToken);

                    using var response = await client.SendAsync(request, cancellationToken);
                    if (!response.IsSuccessStatusCode)
                    {
                        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                        var helixError = TryParseHelixError(errorBody);

                        _logger.LogWarning("❌ Helix Get Moderators returned non-success: broadcaster_id={BroadcasterId}, status={Status}, moderators_collected_so_far={Count}",
                            LogSanitizer.Sanitize(broadcasterId),
                            (int)response.StatusCode,
                            allModerators.Count);

                        if (!string.IsNullOrWhiteSpace(helixError?.Message))
                        {
                            _logger.LogWarning("🧾 Helix Get Moderators error message: {Message}", LogSanitizer.Sanitize(helixError.Message));
                        }

                        return new TwitchModeratorsResponse
                        {
                            StatusCode = response.StatusCode,
                            Moderators = allModerators
                        };
                    }

                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    var parsed = JsonSerializer.Deserialize<GetModeratorsHelixResponse>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (parsed?.Data != null)
                    {
                        foreach (var mod in parsed.Data)
                        {
                            allModerators.Add(new TwitchModeratorDto
                            {
                                UserId = mod.UserId ?? string.Empty,
                                UserLogin = mod.UserLogin ?? string.Empty,
                                UserName = mod.UserName ?? string.Empty
                            });
                        }
                    }

                    cursor = parsed?.Pagination?.Cursor;
                    if (string.IsNullOrEmpty(cursor))
                    {
                        break;
                    }
                }

                _logger.LogInformation("📋 Helix Get Moderators completed: broadcaster_id={BroadcasterId}, moderators_count={Count}",
                    LogSanitizer.Sanitize(broadcasterId),
                    allModerators.Count);

                return new TwitchModeratorsResponse
                {
                    StatusCode = HttpStatusCode.OK,
                    Moderators = allModerators
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error calling Helix Get Moderators for broadcaster {BroadcasterId}", LogSanitizer.Sanitize(broadcasterId));
                return new TwitchModeratorsResponse
                {
                    StatusCode = HttpStatusCode.InternalServerError,
                    Moderators = new List<TwitchModeratorDto>()
                };
            }
        }

        private static HelixErrorResponse? TryParseHelixError(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<HelixErrorResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch
            {
                return null;
            }
        }

        private class HelixErrorResponse
        {
            public string? Error { get; set; }
            public int Status { get; set; }
            public string? Message { get; set; }
        }

        private class GetModeratorsHelixResponse
        {
            public List<GetModeratorsHelixData> Data { get; set; } = new();
            public GetModeratorsHelixPagination? Pagination { get; set; }
        }

        private class GetModeratorsHelixPagination
        {
            public string? Cursor { get; set; }
        }

        private class GetModeratorsHelixData
        {
            [JsonPropertyName("user_id")]
            public string? UserId { get; set; }

            [JsonPropertyName("user_login")]
            public string? UserLogin { get; set; }

            [JsonPropertyName("user_name")]
            public string? UserName { get; set; }
        }

        private class SearchCategoriesHelixResponse
        {
            public List<SearchCategoriesHelixData> Data { get; set; } = new();
        }

        private class SearchCategoriesHelixData
        {
            [JsonPropertyName("id")]
            public string? Id { get; set; }

            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("box_art_url")]
            public string? BoxArtUrl { get; set; }
        }

        private class GetChannelsHelixResponse
        {
            public List<GetChannelsHelixData> Data { get; set; } = new();
        }

        private class GetChannelsHelixData
        {
            [JsonPropertyName("broadcaster_id")]
            public string? BroadcasterId { get; set; }

            [JsonPropertyName("game_id")]
            public string? GameId { get; set; }

            [JsonPropertyName("game_name")]
            public string? GameName { get; set; }

            [JsonPropertyName("content_classification_labels")]
            public List<ContentClassificationLabel>? ContentClassificationLabels { get; set; }
        }

        private class ModifyChannelInformationRequest
        {
            [JsonPropertyName("game_id")]
            public string? GameId { get; set; }

            [JsonPropertyName("content_classification_labels")]
            public List<ContentClassificationLabel>? ContentClassificationLabels { get; set; }
        }

        [JsonConverter(typeof(ContentClassificationLabelConverter))]
        private class ContentClassificationLabel
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("is_enabled")]
            public bool IsEnabled { get; set; }
        }

        private sealed class ContentClassificationLabelConverter : JsonConverter<ContentClassificationLabel>
        {
            public override ContentClassificationLabel Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.String)
                {
                    return new ContentClassificationLabel
                    {
                        Id = reader.GetString() ?? string.Empty,
                        IsEnabled = true
                    };
                }

                if (reader.TokenType == JsonTokenType.StartObject)
                {
                    using var doc = JsonDocument.ParseValue(ref reader);
                    var root = doc.RootElement;

                    string id = string.Empty;
                    bool isEnabled = false;

                    if (root.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                    {
                        id = idProp.GetString() ?? string.Empty;
                    }

                    if (root.TryGetProperty("is_enabled", out var enabledProp) && enabledProp.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    {
                        isEnabled = enabledProp.GetBoolean();
                    }

                    return new ContentClassificationLabel
                    {
                        Id = id,
                        IsEnabled = isEnabled
                    };
                }

                if (reader.TokenType == JsonTokenType.Null)
                {
                    return new ContentClassificationLabel();
                }

                throw new JsonException($"Unexpected token {reader.TokenType} for content_classification_labels element");
            }

            public override void Write(Utf8JsonWriter writer, ContentClassificationLabel value, JsonSerializerOptions options)
            {
                writer.WriteStartObject();
                writer.WriteString("id", value.Id);
                writer.WriteBoolean("is_enabled", value.IsEnabled);
                writer.WriteEndObject();
            }
        }

        public async Task<IEnumerable<TwitchCustomReward>> GetCustomRewardsAsync(string userId)
        {
            return await ExecuteWithRetryAsync(userId, async (accessToken) =>
            {
                var clientId = _configuration["Twitch:ClientId"];
                if (string.IsNullOrEmpty(clientId)) throw new Exception("Twitch ClientId is not configured");

                var rewards = await _helixWrapper.GetCustomRewardsAsync(clientId, accessToken, userId); // Note: userId here is assumed to be broadcasterId which matches TwitchUserId

                return rewards.Select(r => new TwitchCustomReward
                {
                    Id = r.Id,
                    Title = r.Title,
                    Cost = r.Cost,
                    Prompt = r.Prompt,
                    IsEnabled = r.IsEnabled,
                    BackgroundColor = r.BackgroundColor,
                    IsUserInputRequired = r.IsUserInputRequired,
                    MaxPerStream = r.MaxPerStreamSetting.IsEnabled ? r.MaxPerStreamSetting.MaxPerStream : (int?)null,
                    MaxPerUserPerStream = r.MaxPerUserPerStreamSetting.IsEnabled ? r.MaxPerUserPerStreamSetting.MaxPerUserPerStream : (int?)null,
                    GlobalCooldownSeconds = r.GlobalCooldownSetting.IsEnabled ? r.GlobalCooldownSetting.GlobalCooldownSeconds : (int?)null
                });
            });
        }

        public async Task<TwitchCustomReward> CreateCustomRewardAsync(string userId, CreateRewardRequest request)
        {
            return await ExecuteWithRetryAsync(userId, async (accessToken) =>
            {
                var clientId = _configuration["Twitch:ClientId"];
                if (string.IsNullOrEmpty(clientId)) throw new Exception("Twitch ClientId is not configured");

                var createRequest = new CreateCustomRewardsRequest
                {
                    Title = request.Title,
                    Cost = request.Cost,
                    Prompt = request.Prompt,
                    IsEnabled = request.IsEnabled,
                    BackgroundColor = request.BackgroundColor,
                    IsUserInputRequired = request.IsUserInputRequired,
                    ShouldRedemptionsSkipRequestQueue = request.ShouldRedemptionsSkipRequestQueue,
                    IsMaxPerStreamEnabled = request.MaxPerStream.HasValue,
                    MaxPerStream = request.MaxPerStream ?? 0,
                    IsMaxPerUserPerStreamEnabled = request.MaxPerUserPerStream.HasValue,
                    MaxPerUserPerStream = request.MaxPerUserPerStream ?? 0,
                    IsGlobalCooldownEnabled = request.GlobalCooldownSeconds.HasValue,
                    GlobalCooldownSeconds = request.GlobalCooldownSeconds ?? 0
                };

                var response = await _helixWrapper.CreateCustomRewardAsync(clientId, accessToken, userId, createRequest);
                var r = response.FirstOrDefault();

                if (r == null) throw new Exception("Failed to create custom reward");

                return new TwitchCustomReward
                {
                    Id = r.Id,
                    Title = r.Title,
                    Cost = r.Cost,
                    Prompt = r.Prompt,
                    IsEnabled = r.IsEnabled,
                    BackgroundColor = r.BackgroundColor,
                    IsUserInputRequired = r.IsUserInputRequired,
                    MaxPerStream = r.MaxPerStreamSetting.IsEnabled ? r.MaxPerStreamSetting.MaxPerStream : (int?)null,
                    MaxPerUserPerStream = r.MaxPerUserPerStreamSetting.IsEnabled ? r.MaxPerUserPerStreamSetting.MaxPerUserPerStream : (int?)null,
                    GlobalCooldownSeconds = r.GlobalCooldownSetting.IsEnabled ? r.GlobalCooldownSetting.GlobalCooldownSeconds : (int?)null
                };
            });
        }

        public async Task DeleteCustomRewardAsync(string userId, string rewardId)
        {
            await ExecuteWithRetryAsync(userId, async (accessToken) =>
            {
                var clientId = _configuration["Twitch:ClientId"];
                if (string.IsNullOrEmpty(clientId)) throw new Exception("Twitch ClientId is not configured");

                await _helixWrapper.DeleteCustomRewardAsync(clientId, accessToken, userId, rewardId);
            });
        }

        public async Task<StreamInfo?> GetStreamInfoAsync(string userId)
        {
            return await ExecuteWithRetryAsync(userId, async (accessToken) =>
            {
                var clientId = _configuration["Twitch:ClientId"];
                if (string.IsNullOrEmpty(clientId)) throw new Exception("Twitch ClientId is not configured");

                var response = await _helixWrapper.GetStreamsAsync(clientId, accessToken, new List<string> { userId });
                var stream = response.Streams.FirstOrDefault();

                if (stream == null)
                {
                    return new StreamInfo { IsLive = false };
                }

                return new StreamInfo
                {
                    IsLive = true,
                    Title = stream.Title,
                    Game = stream.GameName,
                    Viewers = stream.ViewerCount,
                    StartedAt = stream.StartedAt
                };
            });
        }

        public async Task<ClipInfo?> CreateClipAsync(string userId)
        {
            try
            {
                return await ExecuteWithRetryAsync(userId, async (accessToken) =>
                {
                    var clientId = _configuration["Twitch:ClientId"];
                    if (string.IsNullOrEmpty(clientId)) throw new Exception("Twitch ClientId is not configured");

                    var response = await _helixWrapper.CreateClipAsync(clientId, accessToken, userId);
                    var clip = response.CreatedClips.FirstOrDefault();

                    if (clip == null) return null;

                    return new ClipInfo
                    {
                        Id = clip.Id,
                        EditUrl = clip.EditUrl
                    };
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating clip for user {UserId}", LogSanitizer.Sanitize(userId));
                return null;
            }
        }

        public async Task<IReadOnlyList<TwitchCategoryDto>> SearchCategoriesAsync(string userId, string query, int first = 20)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return Array.Empty<TwitchCategoryDto>();
            }

            var requestedFirst = Math.Clamp(first, 1, 100);

            async Task<IReadOnlyList<TwitchCategoryDto>> ExecuteAsync(string accessToken)
            {
                var clientId = _configuration["Twitch:ClientId"];
                if (string.IsNullOrEmpty(clientId)) throw new Exception("Twitch ClientId is not configured");

                var client = _httpClientFactory.CreateClient();
                var url = $"https://api.twitch.tv/helix/search/categories?query={Uri.EscapeDataString(query)}&first={requestedFirst}";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Client-Id", clientId);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

                using var response = await client.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var helixError = TryParseHelixError(body);
                    _logger.LogWarning(
                        "❌ Helix Search Categories failed. Status={StatusCode} Error={Error} Message={Message}",
                        (int)response.StatusCode,
                        LogSanitizer.Sanitize(helixError?.Error ?? ""),
                        LogSanitizer.Sanitize(helixError?.Message ?? ""));
                    throw new Exception($"Twitch Search Categories failed: {(int)response.StatusCode} {response.ReasonPhrase}");
                }

                var parsed = JsonSerializer.Deserialize<SearchCategoriesHelixResponse>(body, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return (IReadOnlyList<TwitchCategoryDto>)(parsed?.Data ?? new List<SearchCategoriesHelixData>())
                    .Where(d => !string.IsNullOrWhiteSpace(d.Id) && !string.IsNullOrWhiteSpace(d.Name))
                    .Select(d => new TwitchCategoryDto
                    {
                        Id = d.Id ?? string.Empty,
                        Name = d.Name ?? string.Empty,
                        BoxArtUrl = d.BoxArtUrl ?? string.Empty
                    })
                    .ToList();
            }

            var appToken = await _authService.GetAppAccessTokenAsync();
            if (!string.IsNullOrEmpty(appToken))
            {
                try
                {
                    return await ExecuteAsync(appToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ SearchCategories with app token failed; falling back to user token. user_id={UserId}", LogSanitizer.Sanitize(userId));
                }
            }

            return await ExecuteWithRetryAsync(userId, ExecuteAsync);
        }

        public async Task<TwitchChannelCategoryDto?> GetChannelCategoryAsync(string userId)
        {
            async Task<TwitchChannelCategoryDto?> ExecuteAsync(string accessToken)
            {
                var clientId = _configuration["Twitch:ClientId"];
                if (string.IsNullOrEmpty(clientId)) throw new Exception("Twitch ClientId is not configured");

                var client = _httpClientFactory.CreateClient();
                var url = $"https://api.twitch.tv/helix/channels?broadcaster_id={Uri.EscapeDataString(userId)}";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Client-Id", clientId);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

                using var response = await client.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var helixError = TryParseHelixError(body);
                    _logger.LogWarning(
                        "❌ Helix Get Channel Category failed. Status={StatusCode} Error={Error} Message={Message}",
                        (int)response.StatusCode,
                        LogSanitizer.Sanitize(helixError?.Error ?? ""),
                        LogSanitizer.Sanitize(helixError?.Message ?? ""));
                    throw new Exception($"Twitch Get Channel Category failed: {(int)response.StatusCode} {response.ReasonPhrase}");
                }

                var parsed = JsonSerializer.Deserialize<GetChannelsHelixResponse>(body, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                var data = parsed?.Data?.FirstOrDefault();
                if (data == null)
                {
                    return null;
                }

                return new TwitchChannelCategoryDto
                {
                    BroadcasterId = data.BroadcasterId ?? userId,
                    GameId = data.GameId ?? string.Empty,
                    GameName = data.GameName ?? string.Empty
                };
            }

            var appToken = await _authService.GetAppAccessTokenAsync();
            if (!string.IsNullOrEmpty(appToken))
            {
                try
                {
                    return await ExecuteAsync(appToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ GetChannelCategory with app token failed; falling back to user token. user_id={UserId}", LogSanitizer.Sanitize(userId));
                }
            }

            return await ExecuteWithRetryAsync(userId, ExecuteAsync);
        }

        public async Task UpdateChannelInformationAsync(string userId, string gameId, IReadOnlyCollection<string> enabledContentClassificationLabels)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(gameId))
            {
                return;
            }

            await ExecuteWithRetryAsync(userId, async (accessToken) =>
            {
                var clientId = _configuration["Twitch:ClientId"];
                if (string.IsNullOrEmpty(clientId)) throw new Exception("Twitch ClientId is not configured");

                var scopes = await _authService.GetTokenScopesAsync(accessToken);
                await EnsureScopesAsync(scopes, new[] { "channel:manage:broadcast" });

                // Twitch currently allows only these 6 Helix CCL ids (max 6 items in payload).
                var knownLabelIds = new[]
                {
                    "DebatedSocialIssuesAndPolitics",
                    "DrugsIntoxication",
                    "SexualThemes",
                    "ViolentGraphic",
                    "Gambling",
                    "ProfanityVulgarity"
                };

                var requested = enabledContentClassificationLabels?.ToHashSet(StringComparer.OrdinalIgnoreCase)
                    ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var requestedCount = enabledContentClassificationLabels?.Count ?? 0;

                var known = knownLabelIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var enabled = requested.Where(id => known.Contains(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (requestedCount > 0)
                {
                    var unknown = requested.Where(id => !known.Contains(id)).ToList();
                    if (unknown.Count > 0)
                    {
                        _logger.LogWarning(
                            "⚠️ Unknown CCL ids requested; they will be ignored. user_id={UserId} unknown={Unknown}",
                            LogSanitizer.Sanitize(userId),
                            string.Join(", ", unknown.Select(LogSanitizer.Sanitize)));
                    }
                }

                // Only include content_classification_labels in the payload when explicitly requested.
                // - null => don't touch existing CCLs
                // - empty array => clear all CCLs
                // - 6-item array => explicitly set enable/disable for each known id
                List<ContentClassificationLabel>? labels = null;
                var wantsToSetCcls = enabledContentClassificationLabels != null;
                if (wantsToSetCcls)
                {
                    if (requestedCount == 0)
                    {
                        // Per Helix docs: empty array clears all labels.
                        labels = new List<ContentClassificationLabel>();
                    }
                    else if (enabled.Count == 0)
                    {
                        // Caller asked to set labels but only provided unknown/unsupported ids.
                        // Do not clear labels in this case; omit CCL update entirely.
                        labels = null;
                    }
                    else
                    {
                        // Helix contract: array length must be <= 6.
                        labels = knownLabelIds
                            .Select(id => new ContentClassificationLabel { Id = id, IsEnabled = enabled.Contains(id) })
                            .ToList();
                    }
                }

                var client = _httpClientFactory.CreateClient();
                var url = $"https://api.twitch.tv/helix/channels?broadcaster_id={Uri.EscapeDataString(userId)}";

                // Fetch current channel info so we can log + skip no-op updates.
                try
                {
                    using var getRequest = new HttpRequestMessage(HttpMethod.Get, url);
                    getRequest.Headers.Add("Client-Id", clientId);
                    getRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

                    using var getResponse = await client.SendAsync(getRequest);
                    var getBody = await getResponse.Content.ReadAsStringAsync();

                    if (getResponse.IsSuccessStatusCode)
                    {
                        var parsed = JsonSerializer.Deserialize<GetChannelsHelixResponse>(getBody, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        var current = parsed?.Data?.FirstOrDefault();
                        var currentEnabled = (current?.ContentClassificationLabels ?? new List<ContentClassificationLabel>())
                            .Where(l => l.IsEnabled && !string.IsNullOrWhiteSpace(l.Id))
                            .Select(l => l.Id)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);

                        _logger.LogInformation(
                            "✅ Retrieved channel info for user {UserId}: current_game_id={CurrentGameId}, current_enabled_ccls={CurrentCcls}",
                            LogSanitizer.Sanitize(userId),
                            LogSanitizer.Sanitize(current?.GameId ?? string.Empty),
                            string.Join(", ", currentEnabled.OrderBy(s => s).Select(LogSanitizer.Sanitize)));

                        var gameSame = string.Equals(current?.GameId ?? string.Empty, gameId, StringComparison.OrdinalIgnoreCase);
                        var cclsSame = labels == null || currentEnabled.SetEquals(enabled);

                        if (gameSame && cclsSame)
                        {
                            _logger.LogInformation(
                                "✅ Channel already up to date; skipping update. user_id={UserId} game_id={GameId} enabled_ccls={Ccls}",
                                LogSanitizer.Sanitize(userId),
                                LogSanitizer.Sanitize(gameId),
                                string.Join(", ", enabled.OrderBy(s => s).Select(LogSanitizer.Sanitize)));
                            return;
                        }

                        _logger.LogInformation(
                            "🔄 Updating channel info for user {UserId}: game_id {OldGameId} -> {NewGameId}, enabled_ccls {OldCcls} -> {NewCcls}",
                            LogSanitizer.Sanitize(userId),
                            LogSanitizer.Sanitize(current?.GameId ?? string.Empty),
                            LogSanitizer.Sanitize(gameId),
                            string.Join(", ", currentEnabled.OrderBy(s => s).Select(LogSanitizer.Sanitize)),
                            string.Join(", ", enabled.OrderBy(s => s).Select(LogSanitizer.Sanitize)));
                    }
                    else
                    {
                        var helixError = TryParseHelixError(getBody);
                        _logger.LogWarning(
                            "⚠️ Helix Get Channel Information failed before update attempt. user_id={UserId} status={StatusCode} error={Error} message={Message}",
                            LogSanitizer.Sanitize(userId),
                            (int)getResponse.StatusCode,
                            LogSanitizer.Sanitize(helixError?.Error ?? ""),
                            LogSanitizer.Sanitize(helixError?.Message ?? ""));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Failed retrieving channel info before update attempt. user_id={UserId}", LogSanitizer.Sanitize(userId));
                }

                var payload = new ModifyChannelInformationRequest
                {
                    GameId = gameId,
                    ContentClassificationLabels = labels
                };

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                });

                using var request = new HttpRequestMessage(HttpMethod.Patch, url);
                request.Headers.Add("Client-Id", clientId);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                using var response = await client.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var helixError = TryParseHelixError(body);
                    _logger.LogWarning(
                        "❌ Helix Modify Channel Information failed. user_id={UserId} status={StatusCode} error={Error} message={Message}",
                        LogSanitizer.Sanitize(userId),
                        (int)response.StatusCode,
                        LogSanitizer.Sanitize(helixError?.Error ?? ""),
                        LogSanitizer.Sanitize(helixError?.Message ?? ""));

                    throw new Exception($"Twitch Modify Channel Information failed: {(int)response.StatusCode} {response.ReasonPhrase}");
                }

                _logger.LogInformation(
                    "✅ Updated channel information for user {UserId}: game_id={GameId}, enabled_ccls={CclCount}",
                    LogSanitizer.Sanitize(userId),
                    LogSanitizer.Sanitize(gameId),
                    enabled.Count);
            });
        }

        public async Task SendChatMessageAsync(string broadcasterId, string message, string? replyParentMessageId = null, string? senderId = null)
        {
            var (user, _) = await EnsureUserTokenValidAsync(senderId ?? broadcasterId);

            try
            {
                await SendChatMessageWithTokenAsync(user.AccessToken, senderId ?? user.TwitchUserId, broadcasterId, message, replyParentMessageId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending chat message for broadcaster {BroadcasterId}", LogSanitizer.Sanitize(broadcasterId));
            }
        }

        public async Task SendChatMessageAsBotAsync(string broadcasterId, string botUserId, string message, string? replyParentMessageId = null)
        {
            try
            {
                var botCreds = await _botCredentialRepository.GetAsync();
                if (botCreds == null || string.IsNullOrEmpty(botCreds.RefreshToken))
                {
                    _logger.LogWarning("⚠️ Cannot send as bot: bot credentials missing. Falling back to broadcaster send.");
                    await SendChatMessageAsync(broadcasterId, message, replyParentMessageId);
                    return;
                }

                // Refresh if expiring (buffer 5 min)
                if (botCreds.TokenExpiry <= DateTimeOffset.UtcNow.AddMinutes(5))
                {
                    _logger.LogInformation("🔄 Refreshing Forge bot token for {Username}", LogSanitizer.Sanitize(botCreds.Username));
                    var refreshed = await _authService.RefreshTokenAsync(botCreds.RefreshToken);
                    if (refreshed == null)
                    {
                        _logger.LogError("❌ Failed to refresh Forge bot token; falling back to broadcaster send");
                        await SendChatMessageAsync(broadcasterId, message, replyParentMessageId);
                        return;
                    }

                    botCreds.AccessToken = refreshed.AccessToken;
                    botCreds.RefreshToken = refreshed.RefreshToken;
                    botCreds.TokenExpiry = DateTimeOffset.UtcNow.AddSeconds(refreshed.ExpiresIn);
                    await _botCredentialRepository.SaveAsync(botCreds);
                }

                if (string.IsNullOrEmpty(botCreds.AccessToken))
                {
                    _logger.LogWarning("⚠️ Bot access token missing; falling back to broadcaster send");
                    await SendChatMessageAsync(broadcasterId, message, replyParentMessageId);
                    return;
                }

                var botScopes = (await _authService.GetTokenScopesAsync(botCreds.AccessToken)) ?? Array.Empty<string>();

                // Prefer app access token for Send Chat Message (per Twitch docs).
                // Twitch enforces additional requirements when using app tokens (e.g., bot authorized with user:bot, and bot is mod).
                var appChatToken = await _authService.GetAppAccessTokenAsync(new[] { "user:write:chat" });
                if (!string.IsNullOrEmpty(appChatToken))
                {
                    if (botScopes.Count > 0 && !botScopes.Contains("user:bot", StringComparer.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning(
                            "⚠️ Bot token appears to be missing user:bot. App-token chat send may fail. bot_scopes={Scopes}",
                            string.Join(", ", botScopes.Select(LogSanitizer.Sanitize)));
                    }

                    var appSendOk = await SendChatMessageWithTokenAsync(appChatToken, botUserId, broadcasterId, message, replyParentMessageId);
                    if (appSendOk)
                    {
                        return;
                    }

                    _logger.LogWarning("⚠️ App-token chat send failed; attempting bot user token send");
                }

                // Bot user token fallback path (requires user token chat scopes)
                var requiredBotChatScopes = new[] { "user:write:chat", "user:bot", "channel:bot" };
                var missingBotChatScopes = requiredBotChatScopes.Where(rs => !botScopes.Contains(rs, StringComparer.OrdinalIgnoreCase)).ToList();
                if (botScopes.Count > 0 && missingBotChatScopes.Any())
                {
                    _logger.LogWarning(
                        "⚠️ Bot token missing required chat scopes. missing={Missing} scopes={Scopes}. Falling back to broadcaster send.",
                        string.Join(", ", missingBotChatScopes.Select(LogSanitizer.Sanitize)),
                        string.Join(", ", botScopes.Select(LogSanitizer.Sanitize)));
                    await SendChatMessageAsync(broadcasterId, message, replyParentMessageId);
                    return;
                }

                var botSendOk = await SendChatMessageWithTokenAsync(botCreds.AccessToken, botUserId, broadcasterId, message, replyParentMessageId);
                if (!botSendOk)
                {
                    _logger.LogWarning("⚠️ Bot-token chat send failed; falling back to broadcaster send");
                    await SendChatMessageAsync(broadcasterId, message, replyParentMessageId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error sending chat message as bot for broadcaster {BroadcasterId}", LogSanitizer.Sanitize(broadcasterId));
                await SendChatMessageAsync(broadcasterId, message, replyParentMessageId);
            }
        }

        private async Task<bool> SendChatMessageWithTokenAsync(string accessToken, string senderId, string broadcasterId, string message, string? replyParentMessageId)
        {
            var clientId = _configuration["Twitch:ClientId"];
            if (string.IsNullOrEmpty(clientId)) throw new Exception("Twitch ClientId is not configured");

            var client = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.twitch.tv/helix/chat/messages");

            request.Headers.Add("Client-Id", clientId);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var payload = new
            {
                broadcaster_id = broadcasterId,
                sender_id = senderId,
                message,
                reply_parent_message_id = replyParentMessageId
            };

            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to send chat message via API. Status: {StatusCode}, Error: {Error}", response.StatusCode, errorContent);
                return false;
            }

            return true;
        }

        public async Task<AutomodSettingsDto> GetAutomodSettingsAsync(string userId)
        {
            var clientId = _configuration["Twitch:ClientId"];
            if (string.IsNullOrEmpty(clientId)) throw new Exception("Twitch ClientId is not configured");

            var operationId = Guid.NewGuid().ToString("N");
            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["AutoModOperationId"] = operationId,
                ["AutoModOperation"] = "GET",
                ["AutoModUserId"] = LogSanitizer.Sanitize(userId)
            });

            _logger.LogInformation("AutoMod GET requested for user {UserId}", LogSanitizer.Sanitize(userId));

            return await ExecuteWithRetryAsync(userId, async (accessToken) =>
            {
                try
                {
                    var validation = await _authService.ValidateTokenAsync(accessToken);
                    if (validation == null) throw new ReauthRequiredException("Twitch authentication expired. Please sign in again.");

                    _logger.LogInformation(
                        "AutoMod GET token validated. ValidationUserId={ValidationUserId} ScopesCount={ScopesCount} ExpiresIn={ExpiresIn}",
                        LogSanitizer.Sanitize(validation.UserId),
                        validation.Scopes?.Count ?? 0,
                        validation.ExpiresIn);

                    if (validation.Scopes != null && validation.Scopes.Count > 0)
                    {
                        _logger.LogInformation(
                            "AutoMod GET token scopes: {Scopes}",
                            string.Join(", ", validation.Scopes.Select(LogSanitizer.Sanitize)));
                    }

                    await EnsureScopesAsync(validation.Scopes ?? Array.Empty<string>(), new[] { "moderator:read:automod_settings" });

                    if (!string.IsNullOrEmpty(validation.UserId) && !string.Equals(validation.UserId, userId, StringComparison.Ordinal))
                    {
                        _logger.LogWarning(
                            "AutoMod GET token user mismatch. RequestedUserId={RequestedUserId} TokenUserId={TokenUserId}",
                            LogSanitizer.Sanitize(userId),
                            LogSanitizer.Sanitize(validation.UserId));
                        throw new ReauthRequiredException("Twitch session mismatch. Please sign in again.");
                    }

                    var moderatorId = userId;

                    _logger.LogInformation(
                        "AutoMod GET calling Helix. BroadcasterId={BroadcasterId} ModeratorId={ModeratorId}",
                        LogSanitizer.Sanitize(userId),
                        LogSanitizer.Sanitize(moderatorId));

                    var response = await _helixWrapper.GetAutomodSettingsAsync(clientId, accessToken, userId, moderatorId);
                    var settings = response.Data.FirstOrDefault();
                    if (settings == null) throw new Exception("Failed to retrieve AutoMod settings");

                    _logger.LogInformation("AutoMod GET succeeded for user {UserId}", LogSanitizer.Sanitize(userId));
                    return MapAutomodToDto(settings);
                }
                catch (TwitchLib.Api.Core.Exceptions.BadRequestException ex)
                {
                    _logger.LogError(ex, "Twitch AutoMod GET failed for user {UserId}: {Message}", LogSanitizer.Sanitize(userId), LogSanitizer.Sanitize(ex.Message));
                    throw;
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(ex, "AutoMod GET blocked for user {UserId}: {Message}", LogSanitizer.Sanitize(userId), LogSanitizer.Sanitize(ex.Message));
                    throw;
                }
                catch (ReauthRequiredException ex)
                {
                    _logger.LogWarning(ex, "AutoMod GET requires reauth for user {UserId}: {Message}", LogSanitizer.Sanitize(userId), LogSanitizer.Sanitize(ex.Message));
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "AutoMod GET failed for user {UserId}: {Message}", LogSanitizer.Sanitize(userId), LogSanitizer.Sanitize(ex.Message));
                    throw;
                }
            });
        }

        public async Task<AutomodSettingsDto> UpdateAutomodSettingsAsync(string userId, AutomodSettingsDto settings)
        {
            var clientId = _configuration["Twitch:ClientId"];
            if (string.IsNullOrEmpty(clientId)) throw new Exception("Twitch ClientId is not configured");

            var operationId = Guid.NewGuid().ToString("N");
            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["AutoModOperationId"] = operationId,
                ["AutoModOperation"] = "UPDATE",
                ["AutoModUserId"] = LogSanitizer.Sanitize(userId)
            });

            _logger.LogInformation(
                "AutoMod UPDATE requested for user {UserId}. OverallLevel={OverallLevel}",
                LogSanitizer.Sanitize(userId),
                settings.OverallLevel);

            return await ExecuteWithRetryAsync(userId, async (accessToken) =>
            {
                try
                {
                    var validation = await _authService.ValidateTokenAsync(accessToken);
                    if (validation == null) throw new ReauthRequiredException("Twitch authentication expired. Please sign in again.");

                    _logger.LogInformation(
                        "AutoMod UPDATE token validated. ValidationUserId={ValidationUserId} ScopesCount={ScopesCount} ExpiresIn={ExpiresIn}",
                        LogSanitizer.Sanitize(validation.UserId),
                        validation.Scopes?.Count ?? 0,
                        validation.ExpiresIn);

                    if (validation.Scopes != null && validation.Scopes.Count > 0)
                    {
                        _logger.LogInformation(
                            "AutoMod UPDATE token scopes: {Scopes}",
                            string.Join(", ", validation.Scopes.Select(LogSanitizer.Sanitize)));
                    }

                    await EnsureAnyScopeAsync(
                        validation.Scopes ?? Array.Empty<string>(),
                        new[] { "moderator:manage:automod_settings", "moderator:manage:automod" });
                    if (!string.IsNullOrEmpty(validation.UserId) && !string.Equals(validation.UserId, userId, StringComparison.Ordinal))
                    {
                        _logger.LogWarning(
                            "AutoMod UPDATE token user mismatch. RequestedUserId={RequestedUserId} TokenUserId={TokenUserId}",
                            LogSanitizer.Sanitize(userId),
                            LogSanitizer.Sanitize(validation.UserId));
                        throw new ReauthRequiredException("Twitch session mismatch. Please sign in again.");
                    }

                    var moderatorId = userId;
                    var automod = MapDtoToAutomod(settings);

                    _logger.LogInformation(
                        "AutoMod UPDATE calling Helix. BroadcasterId={BroadcasterId} ModeratorId={ModeratorId}",
                        LogSanitizer.Sanitize(userId),
                        LogSanitizer.Sanitize(moderatorId));

                    var response = await _helixWrapper.UpdateAutomodSettingsAsync(clientId, accessToken, userId, moderatorId, automod);
                    var updated = response.Data.FirstOrDefault();
                    if (updated == null) throw new Exception("Failed to update AutoMod settings");

                    _logger.LogInformation("AutoMod UPDATE succeeded for user {UserId}", LogSanitizer.Sanitize(userId));
                    return MapAutomodToDto(updated);
                }
                catch (TwitchLib.Api.Core.Exceptions.BadRequestException ex)
                {
                    _logger.LogError(ex, "Twitch AutoMod UPDATE failed for user {UserId}: {Message}", LogSanitizer.Sanitize(userId), LogSanitizer.Sanitize(ex.Message));
                    throw;
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(ex, "AutoMod UPDATE blocked for user {UserId}: {Message}", LogSanitizer.Sanitize(userId), LogSanitizer.Sanitize(ex.Message));
                    throw;
                }
                catch (ReauthRequiredException ex)
                {
                    _logger.LogWarning(ex, "AutoMod UPDATE requires reauth for user {UserId}: {Message}", LogSanitizer.Sanitize(userId), LogSanitizer.Sanitize(ex.Message));
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "AutoMod UPDATE failed for user {UserId}: {Message}", LogSanitizer.Sanitize(userId), LogSanitizer.Sanitize(ex.Message));
                    throw;
                }
            });
        }

        private static AutomodSettingsDto MapAutomodToDto(TwitchLib.Api.Helix.Models.Moderation.AutomodSettings.AutomodSettingsResponseModel settings)
        {
            return new AutomodSettingsDto
            {
                OverallLevel = settings.OverallLevel,
                Aggression = settings.Aggression ?? 0,
                Bullying = settings.Bullying ?? 0,
                Disability = settings.Disability ?? 0,
                Misogyny = settings.Misogyny ?? 0,
                RaceEthnicityOrReligion = settings.RaceEthnicityOrReligion ?? 0,
                SexBasedTerms = settings.SexBasedTerms ?? 0,
                SexualitySexOrGender = settings.SexualitySexOrGender ?? 0,
                Swearing = settings.Swearing ?? 0
            };
        }

        private static AutomodSettingsDto MapAutomodToDto(TwitchLib.Api.Helix.Models.Moderation.AutomodSettings.AutomodSettings settings)
        {
            return new AutomodSettingsDto
            {
                OverallLevel = settings.OverallLevel,
                Aggression = settings.Aggression ?? 0,
                Bullying = settings.Bullying ?? 0,
                Disability = settings.Disability ?? 0,
                Misogyny = settings.Misogyny ?? 0,
                RaceEthnicityOrReligion = settings.RaceEthnicityOrReligion ?? 0,
                SexBasedTerms = settings.SexBasedTerms ?? 0,
                SexualitySexOrGender = settings.SexualitySexOrGender ?? 0,
                Swearing = settings.Swearing ?? 0
            };
        }

        private static AutomodSettings MapDtoToAutomod(AutomodSettingsDto dto)
        {
            if (dto == null) throw new InvalidOperationException("AutoMod settings payload is required");

            // Twitch requires 0-4 for all values.
            if (dto.OverallLevel.HasValue)
            {
                ValidateAutomodValue("overall_level", dto.OverallLevel.Value);

                // Mutually exclusive: if overall level is set, do NOT include individual fields.
                return new AutomodSettings
                {
                    OverallLevel = dto.OverallLevel.Value,
                    Aggression = null,
                    Bullying = null,
                    Disability = null,
                    Misogyny = null,
                    RaceEthnicityOrReligion = null,
                    SexBasedTerms = null,
                    SexualitySexOrGender = null,
                    Swearing = null
                };
            }

            ValidateAutomodValue("aggression", dto.Aggression);
            ValidateAutomodValue("bullying", dto.Bullying);
            ValidateAutomodValue("disability", dto.Disability);
            ValidateAutomodValue("misogyny", dto.Misogyny);
            ValidateAutomodValue("race_ethnicity_or_religion", dto.RaceEthnicityOrReligion);
            ValidateAutomodValue("sex_based_terms", dto.SexBasedTerms);
            ValidateAutomodValue("sexuality_sex_or_gender", dto.SexualitySexOrGender);
            ValidateAutomodValue("swearing", dto.Swearing);

            return new AutomodSettings
            {
                OverallLevel = null,
                Aggression = dto.Aggression,
                Bullying = dto.Bullying,
                Disability = dto.Disability,
                Misogyny = dto.Misogyny,
                RaceEthnicityOrReligion = dto.RaceEthnicityOrReligion,
                SexBasedTerms = dto.SexBasedTerms,
                SexualitySexOrGender = dto.SexualitySexOrGender,
                Swearing = dto.Swearing
            };
        }

        public async Task<TwitchUserDto?> GetUserByLoginAsync(string login, string actingUserId)
        {
            async Task<TwitchUserDto?> ExecuteAsync(string accessToken)
            {
                var clientId = _configuration["Twitch:ClientId"];
                if (string.IsNullOrEmpty(clientId)) throw new Exception("Twitch ClientId is not configured");

                var response = await _helixWrapper.GetUsersAsync(clientId, accessToken, logins: new List<string> { login });

                if (response.Users.Length == 0) return null;

                var user = response.Users[0];
                return new TwitchUserDto
                {
                    Id = user.Id,
                    Login = user.Login,
                    DisplayName = user.DisplayName,
                    ProfileImageUrl = user.ProfileImageUrl,
                    Email = user.Email
                };
            }

            var appToken = await _authService.GetAppAccessTokenAsync();
            if (!string.IsNullOrEmpty(appToken))
            {
                try
                {
                    return await ExecuteAsync(appToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ GetUserByLogin with app token failed; falling back to user token. acting_user_id={UserId}", LogSanitizer.Sanitize(actingUserId));
                }
            }

            return await ExecuteWithRetryAsync(actingUserId, ExecuteAsync);
        }

        private static void ValidateAutomodValue(string name, int value)
        {
            if (value < 0 || value > 4)
            {
                throw new InvalidOperationException($"Invalid AutoMod setting '{name}' value '{value}'. Valid range is 0-4.");
            }
        }

        private async Task<T> ExecuteWithRetryAsync<T>(string userId, Func<string, Task<T>> action)
        {
            var (user, wasRefreshed) = await EnsureUserTokenValidAsync(userId);
            try
            {
                return await action(user.AccessToken);
            }
            catch (Exception ex)
            {
                if (await TryHandleUnauthorizedAndRefresh(ex, user, wasRefreshed))
                {
                    return await action(user.AccessToken);
                }
                throw;
            }
        }

        private async Task EnsureScopesAsync(string userId, string accessToken, IEnumerable<string> requiredScopes)
        {
            var hasScopes = await _authService.HasScopesAsync(accessToken, requiredScopes);
            if (!hasScopes)
            {
                throw new InvalidOperationException($"Missing required Twitch scopes for {string.Join(", ", requiredScopes)}. Please re-authorize.");
            }
        }

        private Task EnsureScopesAsync(IReadOnlyList<string> scopes, IEnumerable<string> requiredScopes)
        {
            var set = scopes.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = requiredScopes.Where(rs => !set.Contains(rs)).ToList();
            if (missing.Any())
            {
                _logger.LogWarning(
                    "Missing required Twitch scopes: {MissingScopes}",
                    string.Join(", ", missing.Select(LogSanitizer.Sanitize)));
                throw new ReauthRequiredException($"Missing required Twitch scopes for {string.Join(", ", missing)}. Please sign in again.");
            }
            return Task.CompletedTask;
        }

        private Task EnsureAnyScopeAsync(IReadOnlyList<string> scopes, IEnumerable<string> acceptedScopes)
        {
            var set = scopes.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var accepted = acceptedScopes.ToList();
            if (!accepted.Any(s => set.Contains(s)))
            {
                _logger.LogWarning(
                    "Missing required Twitch scope (any-of). AcceptedScopes={AcceptedScopes}",
                    string.Join(", ", accepted.Select(LogSanitizer.Sanitize)));
                throw new ReauthRequiredException(
                    $"Missing required Twitch scope. Need one of: {string.Join(", ", accepted)}. Please sign in again.");
            }
            return Task.CompletedTask;
        }

        private async Task ExecuteWithRetryAsync(string userId, Func<string, Task> action)
        {
            var (user, wasRefreshed) = await EnsureUserTokenValidAsync(userId);
            try
            {
                await action(user.AccessToken);
            }
            catch (Exception ex)
            {
                if (await TryHandleUnauthorizedAndRefresh(ex, user, wasRefreshed))
                {
                    await action(user.AccessToken);
                    return;
                }
                throw;
            }
        }

        private async Task<bool> TryHandleUnauthorizedAndRefresh(Exception ex, User user, bool wasRefreshed)
        {
            bool isUnauthorized = false;
            if (ex is HttpRequestException httpEx && httpEx.StatusCode == HttpStatusCode.Unauthorized)
            {
                isUnauthorized = true;
            }
            else if (ex.Message.Contains("401") || ex.Message.Contains("Unauthorized"))
            {
                isUnauthorized = true;
            }

            if (!isUnauthorized) return false;

            if (wasRefreshed)
            {
                _logger.LogWarning(ex, "Twitch API call failed with 401 immediately after refresh for user {UserId}. Aborting retry.", LogSanitizer.Sanitize(user.TwitchUserId));
                throw new ReauthRequiredException("Twitch authentication expired. Please sign in again.", ex);
            }

            _logger.LogWarning(ex, "Twitch API call failed with 401 for user {UserId}. Attempting token refresh and retry.", LogSanitizer.Sanitize(user.TwitchUserId));

            if (string.IsNullOrEmpty(user.RefreshToken))
            {
                _logger.LogError("Cannot retry Twitch API call for user {UserId}: refresh token is missing", LogSanitizer.Sanitize(user.TwitchUserId));
                throw new ReauthRequiredException("Twitch authentication expired. Please sign in again.");
            }

            var newToken = await _authService.RefreshTokenAsync(user.RefreshToken);
            if (newToken == null)
            {
                _logger.LogWarning(ex, "Twitch token refresh failed for user {UserId}", LogSanitizer.Sanitize(user.TwitchUserId));
                throw new ReauthRequiredException("Twitch authentication expired. Please sign in again.", ex);
            }

            user.AccessToken = newToken.AccessToken;
            user.RefreshToken = newToken.RefreshToken;
            user.TokenExpiry = DateTimeOffset.UtcNow.AddSeconds(newToken.ExpiresIn);
            await _userRepository.SaveUserAsync(user);
            return true;
        }
    }
}
