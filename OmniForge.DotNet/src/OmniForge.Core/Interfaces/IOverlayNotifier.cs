using System.Threading.Tasks;
using OmniForge.Core.Entities;

namespace OmniForge.Core.Interfaces
{
    public interface IOverlayNotifier
    {
        Task NotifyCounterUpdateAsync(string userId, Counter counter);
        Task NotifyMilestoneReachedAsync(string userId, string counterType, int milestone, int newValue, int previousMilestone);
        Task NotifySettingsUpdateAsync(string userId, OverlaySettings settings);
        Task NotifyStreamStatusUpdateAsync(string userId, string status);
        Task NotifyStreamStartedAsync(string userId, Counter counter);
        Task NotifyStreamEndedAsync(string userId, Counter counter);

        Task NotifyFollowerAsync(string userId, string displayName);
        Task NotifySubscriberAsync(string userId, string displayName, string tier, bool isGift);
        Task NotifyResubAsync(string userId, string displayName, int months, string tier, string message);
        Task NotifyGiftSubAsync(string userId, string gifterName, string recipientName, string tier, int totalGifts);
        Task NotifyBitsAsync(string userId, string displayName, int amount, string message, int totalBits);
        Task NotifyRaidAsync(string userId, string raiderName, int viewers);
        Task NotifyCustomAlertAsync(string userId, string alertType, object data);
    }
}
