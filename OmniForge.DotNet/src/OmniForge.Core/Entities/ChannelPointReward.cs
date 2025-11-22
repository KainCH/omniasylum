using System;

namespace OmniForge.Core.Entities
{
    public class ChannelPointReward
    {
        public string UserId { get; set; } = string.Empty;
        public string RewardId { get; set; } = string.Empty;
        public string RewardTitle { get; set; } = string.Empty;
        public int Cost { get; set; }
        public string Action { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
        public DateTimeOffset CreatedAt { get; set; }
    }
}
