using System.Threading.Tasks;

namespace OmniForge.Core.Interfaces
{
    public interface ITwitchService
    {
        Task ConnectAsync();
        Task DisconnectAsync();
        bool IsConnected { get; }
        Task SendMessageAsync(string channel, string message);
    }
}
