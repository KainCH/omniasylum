using System.Threading.Tasks;

namespace OmniForge.Core.Interfaces
{
    public interface IGameSwitchService
    {
        Task HandleGameDetectedAsync(string userId, string gameId, string gameName, string? boxArtUrl = null);

        Task ApplyActiveCoreCountersSelectionAsync(string userId, string gameId);

        /// <summary>
        /// Sends the mod-channel counter announcement for a new stream start.
        /// Builds active counter descriptions for the current game and posts them to the mod channel.
        /// </summary>
        Task SendStreamOnlineAnnouncementsAsync(string userId, string gameId, string gameName);
    }
}
