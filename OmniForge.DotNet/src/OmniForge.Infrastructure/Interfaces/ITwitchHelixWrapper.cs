using System.Collections.Generic;
using System.Threading.Tasks;
using OmniForge.Core.Entities;
using TwitchLib.Api.Core.Enums;
using TwitchLib.Api.Helix.Models.ChannelPoints.CreateCustomReward;

namespace OmniForge.Infrastructure.Interfaces
{
    public interface ITwitchHelixWrapper
    {
        Task<List<HelixCustomReward>> GetCustomRewardsAsync(string clientId, string accessToken, string broadcasterId);
        Task<List<HelixCustomReward>> CreateCustomRewardAsync(string clientId, string accessToken, string broadcasterId, CreateCustomRewardsRequest request);
        Task DeleteCustomRewardAsync(string clientId, string accessToken, string broadcasterId, string rewardId);
        Task CreateEventSubSubscriptionAsync(string clientId, string accessToken, string type, string version, Dictionary<string, string> condition, EventSubTransportMethod method, string sessionId);
    }
}
