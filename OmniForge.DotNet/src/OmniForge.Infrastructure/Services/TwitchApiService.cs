using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
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
            public string? UserId { get; set; }
            public string? UserLogin { get; set; }
            public string? UserName { get; set; }
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

                await SendChatMessageWithTokenAsync(botCreds.AccessToken, botUserId, broadcasterId, message, replyParentMessageId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error sending chat message as bot for broadcaster {BroadcasterId}", LogSanitizer.Sanitize(broadcasterId));
                await SendChatMessageAsync(broadcasterId, message, replyParentMessageId);
            }
        }

        private async Task SendChatMessageWithTokenAsync(string accessToken, string senderId, string broadcasterId, string message, string? replyParentMessageId)
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
            }
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
            return await ExecuteWithRetryAsync(actingUserId, async (accessToken) =>
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
            });
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
