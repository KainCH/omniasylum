using System;
using System.Collections.Concurrent;

namespace OmniForge.Infrastructure.Services.EventHandlers
{
    /// <summary>
    /// Tracks Discord notification status for rate limiting and monitoring.
    /// </summary>
    public class DiscordNotificationTracker : IDiscordNotificationTracker
    {
        private readonly ConcurrentDictionary<string, (DateTimeOffset Time, bool Success)> _notifications = new();

        public void RecordNotification(string userId, bool success)
        {
            _notifications.AddOrUpdate(userId,
                (DateTimeOffset.UtcNow, success),
                (key, old) => (DateTimeOffset.UtcNow, success));
        }

        public (DateTimeOffset Time, bool Success)? GetLastNotification(string userId)
        {
            return _notifications.TryGetValue(userId, out var status) ? status : null;
        }
    }
}
