using System.Threading.Tasks;
using OmniForge.Core.Entities;

namespace OmniForge.Core.Interfaces
{
    public interface INotificationService
    {
        Task CheckAndSendMilestoneNotificationsAsync(User user, string counterType, int previousValue, int newValue);
    }
}
