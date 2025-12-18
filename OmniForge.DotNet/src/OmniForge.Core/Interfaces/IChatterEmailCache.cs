using System.Collections.Generic;
using System.Threading.Tasks;

namespace OmniForge.Core.Interfaces
{
    /// <summary>
    /// Cache service for matching PayPal donor emails to Twitch users currently in chat.
    /// Uses Twitch Helix API to fetch chatters and their emails, then stores in memory
    /// for fast lookup when PayPal IPN notifications arrive.
    /// </summary>
    public interface IChatterEmailCache
    {
        /// <summary>
        /// Sync chatters for a broadcaster - fetches current chatters and their emails from Twitch.
        /// Should be called periodically while stream is live.
        /// </summary>
        /// <param name="broadcasterId">The broadcaster's Twitch user ID.</param>
        /// <returns>Number of chatters synced.</returns>
        Task<int> SyncChattersAsync(string broadcasterId);

        /// <summary>
        /// Lookup Twitch user by email address (from PayPal donor).
        /// </summary>
        /// <param name="broadcasterId">The broadcaster's Twitch user ID (for scoping).</param>
        /// <param name="email">Email address to lookup.</param>
        /// <returns>CachedTwitchUser if found, null otherwise.</returns>
        CachedTwitchUser? GetUserByEmail(string broadcasterId, string email);

        /// <summary>
        /// Lookup Twitch user by user ID.
        /// </summary>
        /// <param name="broadcasterId">The broadcaster's Twitch user ID (for scoping).</param>
        /// <param name="userId">Twitch user ID to lookup.</param>
        /// <returns>CachedTwitchUser if found, null otherwise.</returns>
        CachedTwitchUser? GetUserById(string broadcasterId, string userId);

        /// <summary>
        /// Store a PayPal donation for later matching when chatters are synced.
        /// </summary>
        /// <param name="broadcasterId">The broadcaster's Twitch user ID.</param>
        /// <param name="donation">The donation info to store.</param>
        void StorePendingDonation(string broadcasterId, PendingPayPalDonation donation);

        /// <summary>
        /// Get pending unmatched donations for a broadcaster.
        /// </summary>
        /// <param name="broadcasterId">The broadcaster's Twitch user ID.</param>
        /// <returns>List of pending donations.</returns>
        IEnumerable<PendingPayPalDonation> GetPendingDonations(string broadcasterId);

        /// <summary>
        /// Remove a pending donation after it's been matched or processed.
        /// </summary>
        /// <param name="broadcasterId">The broadcaster's Twitch user ID.</param>
        /// <param name="transactionId">PayPal transaction ID.</param>
        void RemovePendingDonation(string broadcasterId, string transactionId);

        /// <summary>
        /// Clear all cached data for a broadcaster (e.g., when stream ends).
        /// </summary>
        /// <param name="broadcasterId">The broadcaster's Twitch user ID.</param>
        void ClearCache(string broadcasterId);

        /// <summary>
        /// Get cache statistics for debugging/monitoring.
        /// </summary>
        /// <param name="broadcasterId">The broadcaster's Twitch user ID.</param>
        /// <returns>Cache statistics.</returns>
        ChatterCacheStats GetStats(string broadcasterId);
    }

    /// <summary>
    /// Information about a Twitch user from the chatter cache.
    /// Named CachedTwitchUser to avoid conflict with TwitchUserInfo in ITwitchAuthService.
    /// </summary>
    public class CachedTwitchUser
    {
        /// <summary>Twitch user ID.</summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>Twitch login name (lowercase).</summary>
        public string Login { get; set; } = string.Empty;

        /// <summary>Twitch display name (with casing).</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>Email address (requires user:read:email scope).</summary>
        public string Email { get; set; } = string.Empty;

        /// <summary>Profile image URL.</summary>
        public string ProfileImageUrl { get; set; } = string.Empty;
    }

    /// <summary>
    /// PayPal donation pending Twitch user matching.
    /// </summary>
    public class PendingPayPalDonation
    {
        /// <summary>PayPal transaction ID.</summary>
        public string TransactionId { get; set; } = string.Empty;

        /// <summary>PayPal payer email (used for matching).</summary>
        public string PayerEmail { get; set; } = string.Empty;

        /// <summary>Donor name from PayPal.</summary>
        public string PayerName { get; set; } = string.Empty;

        /// <summary>Donation amount.</summary>
        public decimal Amount { get; set; }

        /// <summary>Currency code.</summary>
        public string Currency { get; set; } = "USD";

        /// <summary>Optional message from donor.</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>When the IPN was received.</summary>
        public System.DateTimeOffset ReceivedAt { get; set; }

        /// <summary>Matched Twitch user (if found).</summary>
        public CachedTwitchUser? MatchedUser { get; set; }
    }

    /// <summary>
    /// Statistics about the chatter email cache.
    /// </summary>
    public class ChatterCacheStats
    {
        /// <summary>Number of chatters in cache.</summary>
        public int ChatterCount { get; set; }

        /// <summary>Number of emails in cache (chatters with email scope).</summary>
        public int EmailCount { get; set; }

        /// <summary>Number of pending donations awaiting match.</summary>
        public int PendingDonationCount { get; set; }

        /// <summary>Last sync timestamp.</summary>
        public System.DateTimeOffset? LastSyncAt { get; set; }
    }
}
