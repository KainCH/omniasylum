using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
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
        private readonly ILogger<TwitchApiService> _logger;

        public TwitchApiService(
            IUserRepository userRepository,
            ITwitchAuthService authService,
            IConfiguration configuration,
            ILogger<TwitchApiService> logger)
        {
            _userRepository = userRepository;
            _authService = authService;
            _configuration = configuration;
            _logger = logger;
        }

        private async Task<TwitchAPI> GetApiForUserAsync(string userId)
        {
            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) throw new Exception("User not found");

            // Check if token needs refresh (buffer of 5 minutes)
            if (user.TokenExpiry <= DateTimeOffset.UtcNow.AddMinutes(5))
            {
                _logger.LogInformation("Refreshing token for user {UserId}", userId);
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
                    _logger.LogWarning("Failed to refresh token for user {UserId}", userId);
                    throw new Exception("Failed to refresh Twitch token");
                }
            }

            var api = new TwitchAPI();
            api.Settings.ClientId = _configuration["Twitch:ClientId"];
            api.Settings.AccessToken = user.AccessToken;

            return api;
        }

        public async Task<IEnumerable<TwitchCustomReward>> GetCustomRewardsAsync(string userId)
        {
            var api = await GetApiForUserAsync(userId);
            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) throw new Exception("User not found");

            var rewards = await api.Helix.ChannelPoints.GetCustomRewardAsync(user.TwitchUserId);

            return rewards.Data.Select(r => new TwitchCustomReward
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
            var api = await GetApiForUserAsync(userId);
            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) throw new Exception("User not found");

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

            var response = await api.Helix.ChannelPoints.CreateCustomRewardsAsync(user.TwitchUserId, createRequest);
            var r = response.Data[0];

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
            var api = await GetApiForUserAsync(userId);
            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) throw new Exception("User not found");

            await api.Helix.ChannelPoints.DeleteCustomRewardAsync(user.TwitchUserId, rewardId);
        }
    }
}
