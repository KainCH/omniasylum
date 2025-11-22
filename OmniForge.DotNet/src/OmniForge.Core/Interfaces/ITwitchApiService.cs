using System.Collections.Generic;
using System.Threading.Tasks;
using OmniForge.Core.Entities;

namespace OmniForge.Core.Interfaces
{
    public interface ITwitchApiService
    {
        Task<IEnumerable<TwitchCustomReward>> GetCustomRewardsAsync(string userId);
        Task<TwitchCustomReward> CreateCustomRewardAsync(string userId, CreateRewardRequest request);
        Task DeleteCustomRewardAsync(string userId, string rewardId);
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
