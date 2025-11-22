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

        public string RewardId { get; set; } = string.Empty;
        public string RewardTitle { get; set; } = string.Empty;
        public int Cost { get; set; }
        public string Action { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public DateTimeOffset CreatedAt { get; set; }

        public ChannelPointReward ToChannelPointReward()
        {
            return new ChannelPointReward
            {
                UserId = PartitionKey,
                RewardId = RewardId,
                RewardTitle = RewardTitle,
                Cost = Cost,
                Action = Action,
                IsEnabled = IsEnabled,
                CreatedAt = CreatedAt
            };
        }

        public static ChannelPointRewardTableEntity FromChannelPointReward(ChannelPointReward reward)
        {
            return new ChannelPointRewardTableEntity
            {
                PartitionKey = reward.UserId,
                RowKey = $"reward-{reward.RewardId}",
                RewardId = reward.RewardId,
                RewardTitle = reward.RewardTitle,
                Cost = reward.Cost,
                Action = reward.Action,
                IsEnabled = reward.IsEnabled,
                CreatedAt = reward.CreatedAt
            };
        }
    }
}
