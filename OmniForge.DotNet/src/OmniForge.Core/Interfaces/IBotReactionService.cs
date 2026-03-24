using System.Threading.Tasks;

namespace OmniForge.Core.Interfaces
{
    public interface IBotReactionService
    {
        Task HandleStreamStartAsync(string broadcasterId);
        Task HandleRaidReceivedAsync(string broadcasterId, string raiderName, int viewers);
        Task HandleNewSubAsync(string broadcasterId, string username, string tier);
        Task HandleGiftSubAsync(string broadcasterId, string gifterName, int count);
        Task HandleResubAsync(string broadcasterId, string username, int months);
        Task HandleFirstTimeChatAsync(string broadcasterId, string chatterId, string displayName);
        Task HandleClipCreatedAsync(string broadcasterId, string clipUrl);
        void ResetSession(string broadcasterId);
    }
}
