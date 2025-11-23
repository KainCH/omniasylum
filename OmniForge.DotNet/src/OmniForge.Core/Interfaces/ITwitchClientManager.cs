using System.Threading.Tasks;

namespace OmniForge.Core.Interfaces
{
    public interface ITwitchClientManager
    {
        Task ConnectUserAsync(string userId);
        Task DisconnectUserAsync(string userId);
        Task SendMessageAsync(string userId, string message);
        BotStatus GetUserBotStatus(string userId);
    }

    public class BotStatus
    {
        public bool Connected { get; set; }
        public string? Error { get; set; }
        public string Reason { get; set; } = "Unknown";
    }
}
