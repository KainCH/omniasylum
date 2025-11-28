using System;
using System.Threading.Tasks;

namespace OmniForge.Infrastructure.Services.EventHandlers
{
    /// <summary>
    /// Interface for sending Discord invite links in chat.
    /// </summary>
    public interface IDiscordInviteSender
    {
        /// <summary>
        /// Sends a Discord invite link to the broadcaster's chat.
        /// Includes throttling to prevent spam.
        /// </summary>
        /// <param name="broadcasterId">The Twitch user ID of the broadcaster.</param>
        Task SendDiscordInviteAsync(string broadcasterId);
    }
}
