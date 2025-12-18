using System.Threading.Tasks;
using OmniForge.Core.Entities;

namespace OmniForge.Core.Interfaces
{
    /// <summary>
    /// Service for sending PayPal donation notifications via chat and overlay.
    /// </summary>
    public interface IPayPalNotificationService
    {
        /// <summary>
        /// Send donation notifications (chat message, overlay alert, Discord) based on user settings.
        /// </summary>
        /// <param name="user">The broadcaster/streamer who received the donation.</param>
        /// <param name="donation">The donation details.</param>
        /// <returns>True if notifications were sent successfully.</returns>
        Task<bool> SendDonationNotificationsAsync(User user, PayPalDonation donation);
    }
}
