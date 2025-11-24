using System;
using System.Threading.Tasks;

namespace OmniForge.Core.Interfaces
{
    public interface IStreamMonitorService
    {
        Task<bool> SubscribeToUserAsync(string userId);
        Task UnsubscribeFromUserAsync(string userId);
        Task<bool> ForceReconnectUserAsync(string userId);
        StreamMonitorStatus GetUserConnectionStatus(string userId);
        bool IsUserSubscribed(string userId);
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
