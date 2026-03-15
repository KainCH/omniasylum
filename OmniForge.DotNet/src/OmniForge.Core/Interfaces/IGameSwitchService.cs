using System.Threading.Tasks;

namespace OmniForge.Core.Interfaces
{
    public interface IGameSwitchService
    {
        Task HandleGameDetectedAsync(string userId, string gameId, string gameName, string? boxArtUrl = null);

        Task ApplyActiveCoreCountersSelectionAsync(string userId, string gameId);
    }
}
