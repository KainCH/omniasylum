using System.Threading.Tasks;

namespace OmniForge.Core.Interfaces
{
    public interface IAutoShoutoutService
    {
        Task HandleChatMessageAsync(string broadcasterId, string chatterUserId, string chatterLogin, string chatterDisplayName, bool isMod, bool isBroadcaster);
        void ResetSession(string broadcasterId);
    }
}
