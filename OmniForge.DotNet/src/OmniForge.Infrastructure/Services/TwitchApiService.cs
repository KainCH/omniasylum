using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Utilities;
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
        private readonly IConfiguration _configuration;
        private readonly ITwitchHelixWrapper _helixWrapper;
        private readonly ILogger<TwitchApiService> _logger;

        public TwitchApiService(
            IUserRepository userRepository,
            ITwitchAuthService authService,
            IConfiguration configuration,
            ITwitchHelixWrapper helixWrapper,
            ILogger<TwitchApiService> logger)
        {
            _userRepository = userRepository;
            _authService = authService;
            _configuration = configuration;
            _helixWrapper = helixWrapper;
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
                    throw new Exception("Failed to refresh Twitch token");
                }
            }

            return (user, refreshed);
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

        public async Task<AutomodSettingsDto> GetAutomodSettingsAsync(string userId)
        {
            var clientId = _configuration["Twitch:ClientId"];
            if (string.IsNullOrEmpty(clientId)) throw new Exception("Twitch ClientId is not configured");

            return await ExecuteWithRetryAsync(userId, async (accessToken) =>
            {
                await EnsureScopesAsync(userId, accessToken, new[] { "moderator:read:automod_settings" });
                var response = await _helixWrapper.GetAutomodSettingsAsync(clientId, accessToken, userId, userId);
                var settings = response.Data.FirstOrDefault();
                if (settings == null) throw new Exception("Failed to retrieve AutoMod settings");

                return MapAutomodToDto(settings);
            });
        }

        public async Task<AutomodSettingsDto> UpdateAutomodSettingsAsync(string userId, AutomodSettingsDto settings)
        {
            var clientId = _configuration["Twitch:ClientId"];
            if (string.IsNullOrEmpty(clientId)) throw new Exception("Twitch ClientId is not configured");

            return await ExecuteWithRetryAsync(userId, async (accessToken) =>
            {
                await EnsureScopesAsync(userId, accessToken, new[] { "moderator:manage:automod" });
                var automod = MapDtoToAutomod(settings);
                var response = await _helixWrapper.UpdateAutomodSettingsAsync(clientId, accessToken, userId, userId, automod);
                var updated = response.Data.FirstOrDefault();
                if (updated == null) throw new Exception("Failed to update AutoMod settings");
                return MapAutomodToDto(updated);
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
            return new AutomodSettings
            {
                OverallLevel = dto.OverallLevel,
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
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
            }

            _logger.LogWarning(ex, "Twitch API call failed with 401 for user {UserId}. Attempting token refresh and retry.", LogSanitizer.Sanitize(user.TwitchUserId));

            if (string.IsNullOrEmpty(user.RefreshToken))
            {
                _logger.LogError("Cannot retry Twitch API call for user {UserId}: refresh token is missing", LogSanitizer.Sanitize(user.TwitchUserId));
                throw new InvalidOperationException("Refresh token is required for automatic retry");
            }

            var newToken = await _authService.RefreshTokenAsync(user.RefreshToken);
            if (newToken == null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();

            user.AccessToken = newToken.AccessToken;
            user.RefreshToken = newToken.RefreshToken;
            user.TokenExpiry = DateTimeOffset.UtcNow.AddSeconds(newToken.ExpiresIn);
            await _userRepository.SaveUserAsync(user);
            return true;
        }
    }
}
