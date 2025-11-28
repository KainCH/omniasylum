using System;

namespace OmniForge.Infrastructure.Services.EventHandlers
{
    /// <summary>
    /// Interface for tracking Discord notification status.
    /// </summary>
    public interface IDiscordNotificationTracker
    {
        /// <summary>
        /// Records a Discord notification attempt.
        /// </summary>
        /// <param name="userId">The Twitch user ID.</param>
        /// <param name="success">Whether the notification was successful.</param>
        void RecordNotification(string userId, bool success);

        /// <summary>
        /// Gets the last notification status for a user.
        /// </summary>
        /// <param name="userId">The Twitch user ID.</param>
        /// <returns>The last notification time and success status, or null if no notification recorded.</returns>
        (DateTimeOffset Time, bool Success)? GetLastNotification(string userId);
    }
}
