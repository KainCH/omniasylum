using System;
using Azure;
using Azure.Data.Tables;
using OmniForge.Core.Entities;

namespace OmniForge.Infrastructure.Entities
{
    public class ChannelPointRewardTableEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = string.Empty; // UserId
        public string RowKey { get; set; } = string.Empty; // reward-{RewardId}
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string rewardId { get; set; } = string.Empty;
        public string rewardTitle { get; set; } = string.Empty;
        public int cost { get; set; }
        public string action { get; set; } = string.Empty;
        public bool isEnabled { get; set; }
        public DateTimeOffset createdAt { get; set; }

        public ChannelPointReward ToChannelPointReward()
        {
            return new ChannelPointReward
            {
                UserId = PartitionKey,
                RewardId = rewardId,
                RewardTitle = rewardTitle,
                Cost = cost,
                Action = action,
                IsEnabled = isEnabled,
                CreatedAt = createdAt
            };
        }

        public static ChannelPointRewardTableEntity FromChannelPointReward(ChannelPointReward reward)
        {
            return new ChannelPointRewardTableEntity
            {
                PartitionKey = reward.UserId,
                RowKey = $"reward-{reward.RewardId}",
                rewardId = reward.RewardId,
                rewardTitle = reward.RewardTitle,
                cost = reward.Cost,
                action = reward.Action,
                isEnabled = reward.IsEnabled,
                createdAt = reward.CreatedAt
            };
        }
    }
}
