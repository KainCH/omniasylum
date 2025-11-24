using System;
using System.Threading.Tasks;

namespace OmniForge.Core.Interfaces
{
    public interface IStreamMonitorService
    {
        Task<SubscriptionResult> SubscribeToUserAsync(string userId);
        Task UnsubscribeFromUserAsync(string userId);
        Task<SubscriptionResult> ForceReconnectUserAsync(string userId);
        StreamMonitorStatus GetUserConnectionStatus(string userId);
        bool IsUserSubscribed(string userId);
    }

    public enum SubscriptionResult
    {
        Success,
        Failed,
        Unauthorized
    }

    public class StreamMonitorStatus
    {
        public bool Connected { get; set; }
        public string[] Subscriptions { get; set; } = Array.Empty<string>();
        public DateTimeOffset? LastConnected { get; set; }
        public DateTimeOffset? LastDiscordNotification { get; set; }
        public bool LastDiscordNotificationSuccess { get; set; }
    }
}
