using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Utilities;

namespace OmniForge.Infrastructure.Services
{
    /// <summary>
    /// In-memory cache service for matching PayPal donor emails to Twitch users.
    /// Syncs chatters from Twitch API and stores email-to-user mappings.
    /// </summary>
    public class ChatterEmailCacheService : IChatterEmailCache
    {
        private readonly ITwitchApiService _twitchApiService;
        private readonly ILogger<ChatterEmailCacheService> _logger;

        // Cache keys pattern: "{broadcasterId}:{type}"
        // Types: "chatters", "emails", "pending", "stats"

        // In-memory storage (per broadcaster)
        // Key = broadcasterId, Value = cache data
        private readonly ConcurrentDictionary<string, BroadcasterCache> _broadcasterCaches = new();

        private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(10);

        public ChatterEmailCacheService(
            ITwitchApiService twitchApiService,
            ILogger<ChatterEmailCacheService> logger)
        {
            _twitchApiService = twitchApiService;
            _logger = logger;
        }

        public async Task<int> SyncChattersAsync(string broadcasterId)
        {
            try
            {
                _logger.LogInformation("🔄 Syncing chatters for broadcaster {BroadcasterId}", LogSanitizer.Sanitize(broadcasterId));

                var cache = GetOrCreateCache(broadcasterId);

                // Step 1: Get all chatters
                var chattersResponse = await _twitchApiService.GetChattersAsync(broadcasterId);
                var chatterIds = chattersResponse.Data.Select(c => c.UserId).ToList();

                _logger.LogDebug("📋 Found {Count} chatters in channel", chatterIds.Count);

                if (!chatterIds.Any())
                {
                    cache.LastSyncAt = DateTimeOffset.UtcNow;
                    return 0;
                }

                // Store basic chatter info (display names)
                foreach (var chatter in chattersResponse.Data)
                {
                    cache.UsersByIdCache[chatter.UserId] = new CachedTwitchUser
                    {
                        UserId = chatter.UserId,
                        Login = chatter.UserLogin,
                        DisplayName = chatter.UserName,
                        Email = string.Empty, // Will be populated if we get email
                        ProfileImageUrl = string.Empty
                    };
                }

                // Step 2: Get user details with emails (batch request)
                // Note: user:read:email scope required for emails
                try
                {
                    var usersResponse = await _twitchApiService.GetUsersByIdsAsync(chatterIds);

                    foreach (var user in usersResponse.Data)
                    {
                        var userInfo = new CachedTwitchUser
                        {
                            UserId = user.Id,
                            Login = user.Login,
                            DisplayName = user.DisplayName,
                            Email = user.Email,
                            ProfileImageUrl = user.ProfileImageUrl
                        };

                        // Update user info
                        cache.UsersByIdCache[user.Id] = userInfo;

                        // Index by email if available
                        if (!string.IsNullOrEmpty(user.Email))
                        {
                            cache.UsersByEmailCache[user.Email.ToLowerInvariant()] = userInfo;
                        }
                    }

                    _logger.LogInformation("✅ Synced {TotalChatters} chatters, {WithEmail} with email for broadcaster {BroadcasterId}",
                        chattersResponse.Data.Count,
                        cache.UsersByEmailCache.Count,
                        LogSanitizer.Sanitize(broadcasterId));
                }
                catch (Exception ex)
                {
                    // May fail if scope not available - that's OK, we still have basic chatter info
                    _logger.LogWarning(ex, "⚠️ Could not fetch user emails (scope may be missing) for broadcaster {BroadcasterId}", LogSanitizer.Sanitize(broadcasterId));
                }

                cache.LastSyncAt = DateTimeOffset.UtcNow;

                // Try to match any pending donations
                await TryMatchPendingDonationsAsync(broadcasterId);

                return chattersResponse.Data.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to sync chatters for broadcaster {BroadcasterId}", LogSanitizer.Sanitize(broadcasterId));
                return 0;
            }
        }

        public CachedTwitchUser? GetUserByEmail(string broadcasterId, string email)
        {
            if (string.IsNullOrEmpty(email)) return null;

            var cache = GetOrCreateCache(broadcasterId);
            var normalizedEmail = email.ToLowerInvariant();

            if (cache.UsersByEmailCache.TryGetValue(normalizedEmail, out var user))
            {
                _logger.LogDebug("✅ Found Twitch user {DisplayName} for email {Email}",
                    LogSanitizer.Sanitize(user.DisplayName),
                    LogSanitizer.SanitizeEmail(email));
                return user;
            }

            _logger.LogDebug("❌ No Twitch user found for email {Email}", LogSanitizer.SanitizeEmail(email));
            return null;
        }

        public CachedTwitchUser? GetUserById(string broadcasterId, string userId)
        {
            if (string.IsNullOrEmpty(userId)) return null;

            var cache = GetOrCreateCache(broadcasterId);

            if (cache.UsersByIdCache.TryGetValue(userId, out var user))
            {
                return user;
            }

            return null;
        }

        public void StorePendingDonation(string broadcasterId, PendingPayPalDonation donation)
        {
            var cache = GetOrCreateCache(broadcasterId);

            // Try to match immediately if we have cached data
            var matchedUser = GetUserByEmail(broadcasterId, donation.PayerEmail);
            if (matchedUser != null)
            {
                donation.MatchedUser = matchedUser;
                _logger.LogInformation("🎯 Immediately matched PayPal donation to Twitch user {DisplayName}",
                    LogSanitizer.Sanitize(matchedUser.DisplayName));
            }

            cache.PendingDonations[donation.TransactionId] = donation;

            _logger.LogDebug("📥 Stored pending donation {TransactionId} for broadcaster {BroadcasterId}",
                LogSanitizer.Sanitize(donation.TransactionId),
                LogSanitizer.Sanitize(broadcasterId));
        }

        public IEnumerable<PendingPayPalDonation> GetPendingDonations(string broadcasterId)
        {
            var cache = GetOrCreateCache(broadcasterId);
            return cache.PendingDonations.Values.ToList();
        }

        public void RemovePendingDonation(string broadcasterId, string transactionId)
        {
            var cache = GetOrCreateCache(broadcasterId);
            cache.PendingDonations.TryRemove(transactionId, out _);
        }

        public void ClearCache(string broadcasterId)
        {
            _broadcasterCaches.TryRemove(broadcasterId, out _);
            _logger.LogInformation("🗑️ Cleared cache for broadcaster {BroadcasterId}", LogSanitizer.Sanitize(broadcasterId));
        }

        public ChatterCacheStats GetStats(string broadcasterId)
        {
            var cache = GetOrCreateCache(broadcasterId);

            return new ChatterCacheStats
            {
                ChatterCount = cache.UsersByIdCache.Count,
                EmailCount = cache.UsersByEmailCache.Count,
                PendingDonationCount = cache.PendingDonations.Count,
                LastSyncAt = cache.LastSyncAt
            };
        }

        private BroadcasterCache GetOrCreateCache(string broadcasterId)
        {
            return _broadcasterCaches.GetOrAdd(broadcasterId, _ => new BroadcasterCache());
        }

        private Task TryMatchPendingDonationsAsync(string broadcasterId)
        {
            var cache = GetOrCreateCache(broadcasterId);

            foreach (var kvp in cache.PendingDonations)
            {
                var donation = kvp.Value;

                // Skip if already matched
                if (donation.MatchedUser != null) continue;

                var matchedUser = GetUserByEmail(broadcasterId, donation.PayerEmail);
                if (matchedUser != null)
                {
                    donation.MatchedUser = matchedUser;
                    _logger.LogInformation("🎯 Matched pending donation {TransactionId} to Twitch user {DisplayName}",
                        LogSanitizer.Sanitize(donation.TransactionId),
                        LogSanitizer.Sanitize(matchedUser.DisplayName));
                }
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Per-broadcaster cache data.
        /// </summary>
        private class BroadcasterCache
        {
            /// <summary>User ID → CachedTwitchUser</summary>
            public ConcurrentDictionary<string, CachedTwitchUser> UsersByIdCache { get; } = new();

            /// <summary>Email (lowercase) → CachedTwitchUser</summary>
            public ConcurrentDictionary<string, CachedTwitchUser> UsersByEmailCache { get; } = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>Transaction ID → Pending donation</summary>
            public ConcurrentDictionary<string, PendingPayPalDonation> PendingDonations { get; } = new();

            /// <summary>Last sync timestamp</summary>
            public DateTimeOffset? LastSyncAt { get; set; }
        }
    }
}
