using System.Threading.Tasks;

namespace OmniForge.Core.Interfaces
{
    public interface IBotModerationService
    {
        Task CheckAndEnforceAsync(string broadcasterId, string chatterId, string chatterLogin,
            string messageId, string message, bool isMod, bool isBroadcaster);
        void ResetSession(string broadcasterId);
    }
}
