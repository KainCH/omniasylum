using System;
using System.Collections.Generic;
using System.Linq;
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

        private async Task<User> EnsureUserTokenValidAsync(string userId)
        {
            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) throw new Exception("User not found");

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
                }
                else
                {
                    _logger.LogWarning("Failed to refresh token for user {UserId}", LogSanitizer.Sanitize(userId));
                    throw new Exception("Failed to refresh Twitch token");
                }
            }

            return user;
        }

        public async Task<IEnumerable<TwitchCustomReward>> GetCustomRewardsAsync(string userId)
        {
            var user = await EnsureUserTokenValidAsync(userId);
            var clientId = _configuration["Twitch:ClientId"];
            if (string.IsNullOrEmpty(clientId)) throw new Exception("Twitch ClientId is not configured");

            var rewards = await _helixWrapper.GetCustomRewardsAsync(clientId, user.AccessToken, user.TwitchUserId);

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
        }

        public async Task<TwitchCustomReward> CreateCustomRewardAsync(string userId, CreateRewardRequest request)
        {
            var user = await EnsureUserTokenValidAsync(userId);
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

            var response = await _helixWrapper.CreateCustomRewardAsync(clientId, user.AccessToken, user.TwitchUserId, createRequest);
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
        }

        public async Task DeleteCustomRewardAsync(string userId, string rewardId)
        {
            var user = await EnsureUserTokenValidAsync(userId);
            var clientId = _configuration["Twitch:ClientId"];
            if (string.IsNullOrEmpty(clientId)) throw new Exception("Twitch ClientId is not configured");

            await _helixWrapper.DeleteCustomRewardAsync(clientId, user.AccessToken, user.TwitchUserId, rewardId);
        }

        public async Task<StreamInfo?> GetStreamInfoAsync(string userId)
        {
            var user = await EnsureUserTokenValidAsync(userId);
            var clientId = _configuration["Twitch:ClientId"];
            if (string.IsNullOrEmpty(clientId)) throw new Exception("Twitch ClientId is not configured");

            var response = await _helixWrapper.GetStreamsAsync(clientId, user.AccessToken, new List<string> { user.TwitchUserId });
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
        }

        public async Task<ClipInfo?> CreateClipAsync(string userId)
        {
            var user = await EnsureUserTokenValidAsync(userId);
            var clientId = _configuration["Twitch:ClientId"];
            if (string.IsNullOrEmpty(clientId)) throw new Exception("Twitch ClientId is not configured");

            try
            {
                var response = await _helixWrapper.CreateClipAsync(clientId, user.AccessToken, user.TwitchUserId);
                var clip = response.CreatedClips.FirstOrDefault();

                if (clip == null) return null;

                return new ClipInfo
                {
                    Id = clip.Id,
                    EditUrl = clip.EditUrl
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating clip for user {UserId}", LogSanitizer.Sanitize(userId));
                return null;
            }
        }
    }
}
