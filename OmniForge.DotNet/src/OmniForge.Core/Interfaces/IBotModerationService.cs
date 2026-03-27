using System.Threading.Tasks;

namespace OmniForge.Core.Interfaces
{
    public interface IBotModerationService
    {
        /// <returns>True if enforcement action was taken (message deleted or user banned), false otherwise.</returns>
        Task<bool> CheckAndEnforceAsync(string broadcasterId, string chatterId, string chatterLogin,
            string messageId, string message, bool isMod, bool isBroadcaster);
        void ResetSession(string broadcasterId);
    }
}
