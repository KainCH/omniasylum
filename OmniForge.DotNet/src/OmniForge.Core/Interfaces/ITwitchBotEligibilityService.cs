using System.Threading;
using System.Threading.Tasks;

namespace OmniForge.Core.Interfaces
{
    public record BotEligibilityResult(bool UseBot, string? BotUserId, string? Reason);

    public interface ITwitchBotEligibilityService
    {
        /// <summary>
        /// Determines whether the global Forge bot should be used for a broadcaster's channel.
        /// Uses the broadcaster's access token to call Helix "Get Moderators" and verify the bot is a moderator.
        /// </summary>
        Task<BotEligibilityResult> GetEligibilityAsync(string broadcasterUserId, string broadcasterAccessToken, CancellationToken cancellationToken = default);
    }
}
