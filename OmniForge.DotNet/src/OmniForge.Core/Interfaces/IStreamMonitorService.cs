using System;
using System.Threading.Tasks;

namespace OmniForge.Core.Interfaces
{
    public interface IStreamMonitorService
    {
        Task<SubscriptionResult> SubscribeToUserAsync(string userId);
        Task<SubscriptionResult> SubscribeToUserAsAsync(string userId, string actingUserId);
        Task UnsubscribeFromUserAsync(string userId);
        Task<SubscriptionResult> ForceReconnectUserAsync(string userId);
        StreamMonitorStatus GetUserConnectionStatus(string userId);
        bool IsUserSubscribed(string userId);

        /// <summary>
        /// Returns true if the broadcaster is currently known to be live (i.e. in the heartbeat loop).
        /// Used by the WebSocket overlay manager to send an immediate live signal on reconnect so the
        /// overlay doesn't appear offline for up to one watchdog minute after a container restart.
        /// </summary>
        bool IsUserLive(string userId);
    }

    public enum SubscriptionResult
    {
        Success,
        Failed,
        Unauthorized,
        RequiresReauth  // Token is valid but missing required scopes - user must re-login
    }

    public class StreamMonitorStatus
    {
        public bool Connected { get; set; }
        public string[] Subscriptions { get; set; } = Array.Empty<string>();
        public DateTimeOffset? LastConnected { get; set; }
        public DateTimeOffset? LastDiscordNotification { get; set; }
        public bool LastDiscordNotificationSuccess { get; set; }
        public bool IsSubscribed { get; set; }
        public string? EventSubSessionId { get; set; }
        public int? EventSubKeepaliveTimeoutSeconds { get; set; }
        public string? LastEventType { get; set; }
        public DateTimeOffset? LastEventAt { get; set; }
        public string? LastEventSummary { get; set; }
        public string? LastError { get; set; }
        public double? KeepaliveAgeSeconds { get; set; }
        public bool AdminInitiated { get; set; }
    }
}
