using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Interfaces;

namespace OmniForge.Infrastructure.Services
{
    public class AutoShoutoutService : IAutoShoutoutService
    {
        private readonly ILogger<AutoShoutoutService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        private readonly ConcurrentDictionary<string, HashSet<string>> _shoutedThisSession = new();
        private readonly ConcurrentDictionary<string, DateTimeOffset> _lastChannelShoutout = new();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, DateTimeOffset>> _lastUserShoutout = new();
        private readonly ConcurrentDictionary<string, (DateTimeOffset ExpiresAt, bool IsFollowing)> _followCache = new();

        private static readonly TimeSpan ChannelCooldown = TimeSpan.FromSeconds(65);
        private static readonly TimeSpan UserCooldown = TimeSpan.FromMinutes(2.5);
        private static readonly TimeSpan FollowCacheTtl = TimeSpan.FromMinutes(10);

        public AutoShoutoutService(ILogger<AutoShoutoutService> logger, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        public async Task HandleChatMessageAsync(string broadcasterId, string chatterUserId, string chatterLogin,
            string chatterDisplayName, bool isMod, bool isBroadcaster)
        {
            if (isBroadcaster || isMod) return;

            using var scope = _scopeFactory.CreateScope();
            var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var twitchApiService = scope.ServiceProvider.GetRequiredService<ITwitchApiService>();

            var user = await userRepository.GetUserAsync(broadcasterId);
            if (user == null) return;

            foreach (var excluded in user.AutoShoutoutExcludeList)
            {
                if (string.Equals(excluded, chatterLogin, StringComparison.OrdinalIgnoreCase)) return;
            }

            var sessionSet = _shoutedThisSession.GetOrAdd(broadcasterId, _ => new HashSet<string>());

            // Atomically reserve the slot — both Contains and Add inside one lock to eliminate the race
            // window where two concurrent messages could both pass the check before either adds.
            lock (sessionSet)
            {
                if (sessionSet.Contains(chatterUserId)) return;
                sessionSet.Add(chatterUserId); // reserved; removed below on failure
            }

            try
            {
                var cacheKey = $"{broadcasterId}:{chatterUserId}";
                bool isFollowing;
                if (_followCache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
                {
                    isFollowing = cached.IsFollowing;
                }
                else
                {
                    isFollowing = await twitchApiService.IsFollowingAsync(broadcasterId, chatterUserId);
                    _followCache[cacheKey] = (DateTimeOffset.UtcNow.Add(FollowCacheTtl), isFollowing);
                }

                if (!isFollowing)
                {
                    lock (sessionSet) { sessionSet.Remove(chatterUserId); }
                    return;
                }

                var now = DateTimeOffset.UtcNow;
                if (_lastChannelShoutout.TryGetValue(broadcasterId, out var lastChannel) && now - lastChannel < ChannelCooldown)
                {
                    _logger.LogDebug("⏳ AutoShoutout channel cooldown active for {Broadcaster}", broadcasterId);
                    lock (sessionSet) { sessionSet.Remove(chatterUserId); }
                    return;
                }

                var userShoutouts = _lastUserShoutout.GetOrAdd(broadcasterId, _ => new ConcurrentDictionary<string, DateTimeOffset>());
                if (userShoutouts.TryGetValue(chatterUserId, out var lastUser) && now - lastUser < UserCooldown)
                {
                    _logger.LogDebug("⏳ AutoShoutout per-user cooldown active for {User} in {Broadcaster}", chatterUserId, broadcasterId);
                    lock (sessionSet) { sessionSet.Remove(chatterUserId); }
                    return;
                }

                var success = await twitchApiService.SendShoutoutAsync(broadcasterId, chatterUserId);
                if (success)
                {
                    _lastChannelShoutout[broadcasterId] = now;
                    userShoutouts[chatterUserId] = now;
                    _logger.LogInformation("✅ AutoShoutout sent for {Chatter} in {Broadcaster}", chatterDisplayName, broadcasterId);
                }
                else
                {
                    lock (sessionSet) { sessionSet.Remove(chatterUserId); }
                }
            }
            catch
            {
                lock (sessionSet) { sessionSet.Remove(chatterUserId); }
                throw;
            }
        }

        public void ResetSession(string broadcasterId)
        {
            if (_shoutedThisSession.TryGetValue(broadcasterId, out var set))
                lock (set) { set.Clear(); }

            _lastChannelShoutout.TryRemove(broadcasterId, out _);
            _lastUserShoutout.TryRemove(broadcasterId, out _);

            // Purge follow cache entries for this broadcaster to avoid stale data next stream
            var prefix = broadcasterId + ":";
            foreach (var key in _followCache.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                    _followCache.TryRemove(key, out _);
            }
        }
    }
}
