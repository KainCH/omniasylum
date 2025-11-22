using System.Threading.Tasks;
using OmniForge.Core.Entities;

namespace OmniForge.Core.Interfaces
{
    public interface IOverlayNotifier
    {
        Task NotifyCounterUpdateAsync(string userId, Counter counter);
        Task NotifyMilestoneReachedAsync(string userId, string counterType, int milestone, int newValue, int previousMilestone);
    }
}
