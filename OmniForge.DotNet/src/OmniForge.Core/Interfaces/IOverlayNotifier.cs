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
        Task NotifyTemplateChangedAsync(string userId, string templateStyle, Template template);

        /// <summary>
        /// Notify overlay of a PayPal donation.
        /// </summary>
        /// <param name="userId">Broadcaster's Twitch user ID.</param>
        /// <param name="donorName">Display name of donor (Twitch name if matched, PayPal name otherwise).</param>
        /// <param name="amount">Donation amount.</param>
        /// <param name="currency">Currency code (e.g., "USD").</param>
        /// <param name="message">Optional donation message.</param>
        /// <param name="matchedTwitchUser">Whether donor was matched to a Twitch user.</param>
        Task NotifyPayPalDonationAsync(string userId, string donorName, decimal amount, string currency, string message, bool matchedTwitchUser);
    }
}
