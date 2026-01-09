using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using OmniForge.Core.Entities;

namespace OmniForge.Core.Interfaces
{
    public interface ITwitchApiService
    {
        Task<IEnumerable<TwitchCustomReward>> GetCustomRewardsAsync(string userId);
        Task<TwitchCustomReward> CreateCustomRewardAsync(string userId, CreateRewardRequest request);
        Task DeleteCustomRewardAsync(string userId, string rewardId);
        Task<StreamInfo?> GetStreamInfoAsync(string userId);
        Task<ClipInfo?> CreateClipAsync(string userId);

        // Moderation
        Task<TwitchModeratorsResponse> GetModeratorsAsync(string broadcasterId, string broadcasterAccessToken, CancellationToken cancellationToken = default);

        // AutoMod settings
        Task<AutomodSettingsDto> GetAutomodSettingsAsync(string userId);
        Task<AutomodSettingsDto> UpdateAutomodSettingsAsync(string userId, AutomodSettingsDto settings);

        // Chat
        Task SendChatMessageAsync(string broadcasterId, string message, string? replyParentMessageId = null, string? senderId = null);
        Task SendChatMessageAsBotAsync(string broadcasterId, string botUserId, string message, string? replyParentMessageId = null);

        // User Lookup
        Task<TwitchUserDto?> GetUserByLoginAsync(string login, string actingUserId);
    }

    public class TwitchModeratorsResponse
    {
        public HttpStatusCode StatusCode { get; init; }
        public List<TwitchModeratorDto> Moderators { get; init; } = new();

        public TwitchModeratorDto? FindModeratorByUserIdOrLogin(string userIdOrLogin)
        {
            if (string.IsNullOrWhiteSpace(userIdOrLogin))
            {
                return null;
            }

            return Moderators.Find(m =>
                string.Equals(m.UserId, userIdOrLogin, StringComparison.OrdinalIgnoreCase)
                || string.Equals(m.UserLogin, userIdOrLogin, StringComparison.OrdinalIgnoreCase));
        }
    }

    public class TwitchModeratorDto
    {
        public string UserId { get; set; } = string.Empty;
        public string UserLogin { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
    }

    public class TwitchUserDto
    {
        public string Id { get; set; } = string.Empty;
        public string Login { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string ProfileImageUrl { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    public class StreamInfo
    {
        public bool IsLive { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Game { get; set; } = string.Empty;
        public int Viewers { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
    }

    public class ClipInfo
    {
        public string Id { get; set; } = string.Empty;
        public string EditUrl { get; set; } = string.Empty;
    }

    public class CreateRewardRequest
    {
        public string Title { get; set; } = string.Empty;
        public int Cost { get; set; }
        public string Prompt { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
        public string BackgroundColor { get; set; } = string.Empty;
        public bool IsUserInputRequired { get; set; } = false;
        public int? MaxPerStream { get; set; }
        public int? MaxPerUserPerStream { get; set; }
        public int? GlobalCooldownSeconds { get; set; }
        public bool ShouldRedemptionsSkipRequestQueue { get; set; } = false;
        public string Action { get; set; } = string.Empty;
    }

    public class TwitchCustomReward
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public int Cost { get; set; }
        public string Prompt { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public string BackgroundColor { get; set; } = string.Empty;
        public bool IsUserInputRequired { get; set; }
        public int? MaxPerStream { get; set; }
        public int? MaxPerUserPerStream { get; set; }
        public int? GlobalCooldownSeconds { get; set; }
        public bool ShouldRedemptionsSkipRequestQueue { get; set; }
    }
}
