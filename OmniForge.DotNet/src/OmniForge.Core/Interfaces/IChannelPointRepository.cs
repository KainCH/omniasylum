using System.Collections.Generic;
using System.Threading.Tasks;
using OmniForge.Core.Entities;

namespace OmniForge.Core.Interfaces
{
    public interface IChannelPointRepository
    {
        Task<IEnumerable<ChannelPointReward>> GetRewardsAsync(string userId);
        Task<ChannelPointReward?> GetRewardAsync(string userId, string rewardId);
        Task SaveRewardAsync(ChannelPointReward reward);
        Task DeleteRewardAsync(string userId, string rewardId);
    }
}
