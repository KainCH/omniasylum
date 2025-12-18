using System.Collections.Generic;
using System.Threading.Tasks;
using OmniForge.Core.Entities;

namespace OmniForge.Core.Interfaces
{
    /// <summary>
    /// Repository for PayPal donation records.
    /// Handles storage, retrieval, and deduplication of IPN transactions.
    /// </summary>
    public interface IPayPalRepository
    {
        /// <summary>
        /// Initialize the repository (create table if needed).
        /// </summary>
        Task InitializeAsync();

        /// <summary>
        /// Get a donation by transaction ID for a specific user.
        /// </summary>
        /// <param name="userId">The broadcaster's Twitch user ID.</param>
        /// <param name="transactionId">PayPal transaction ID.</param>
        /// <returns>The donation if found, null otherwise.</returns>
        Task<PayPalDonation?> GetDonationAsync(string userId, string transactionId);

        /// <summary>
        /// Check if a transaction has already been processed (for deduplication).
        /// </summary>
        /// <param name="userId">The broadcaster's Twitch user ID.</param>
        /// <param name="transactionId">PayPal transaction ID.</param>
        /// <returns>True if the transaction exists, false otherwise.</returns>
        Task<bool> TransactionExistsAsync(string userId, string transactionId);

        /// <summary>
        /// Save a new donation or update an existing one.
        /// </summary>
        /// <param name="donation">The donation to save.</param>
        Task SaveDonationAsync(PayPalDonation donation);

        /// <summary>
        /// Get all donations for a user within a time range.
        /// </summary>
        /// <param name="userId">The broadcaster's Twitch user ID.</param>
        /// <param name="limit">Maximum number of donations to return.</param>
        /// <returns>List of donations, most recent first.</returns>
        Task<IEnumerable<PayPalDonation>> GetRecentDonationsAsync(string userId, int limit = 50);

        /// <summary>
        /// Get donations that haven't had notifications sent yet.
        /// </summary>
        /// <param name="userId">The broadcaster's Twitch user ID.</param>
        /// <returns>List of donations pending notification.</returns>
        Task<IEnumerable<PayPalDonation>> GetPendingNotificationsAsync(string userId);

        /// <summary>
        /// Mark a donation as having sent notifications.
        /// </summary>
        /// <param name="userId">The broadcaster's Twitch user ID.</param>
        /// <param name="transactionId">PayPal transaction ID.</param>
        Task MarkNotificationSentAsync(string userId, string transactionId);

        /// <summary>
        /// Update the verification status of a donation.
        /// </summary>
        /// <param name="userId">The broadcaster's Twitch user ID.</param>
        /// <param name="transactionId">PayPal transaction ID.</param>
        /// <param name="status">The new verification status.</param>
        Task UpdateVerificationStatusAsync(string userId, string transactionId, PayPalVerificationStatus status);

        /// <summary>
        /// Delete old donation records (for cleanup/GDPR).
        /// </summary>
        /// <param name="userId">The broadcaster's Twitch user ID.</param>
        /// <param name="olderThanDays">Delete records older than this many days.</param>
        /// <returns>Number of records deleted.</returns>
        Task<int> DeleteOldDonationsAsync(string userId, int olderThanDays = 90);
    }
}
