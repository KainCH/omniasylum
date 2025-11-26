using System.Threading.Tasks;
using OmniForge.Core.Entities;

namespace OmniForge.Core.Interfaces
{
    public interface IDiscordService
    {
        Task SendNotificationAsync(User user, string eventType, object data);
        Task SendTestNotificationAsync(User user);
        Task<bool> ValidateWebhookAsync(string webhookUrl);
    }
}
