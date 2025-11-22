using System.Threading.Tasks;

namespace OmniForge.Core.Interfaces
{
    public interface ITwitchClientManager
    {
        Task ConnectUserAsync(string userId);
        Task DisconnectUserAsync(string userId);
        Task SendMessageAsync(string userId, string message);
    }
}
