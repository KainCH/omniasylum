using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Interfaces;

namespace OmniForge.Infrastructure.Services
{
    public class MemoryBotEligibilityCache : IBotEligibilityCache
    {
        private sealed record Entry(BotEligibilityResult Result, DateTimeOffset ExpiresAtUtc);

        private readonly ConcurrentDictionary<string, Entry> _entries = new();

        public Task<BotEligibilityResult?> TryGetAsync(string broadcasterUserId, string botLoginOrId, CancellationToken cancellationToken = default)
        {
            var key = BuildKey(broadcasterUserId, botLoginOrId);
            if (_entries.TryGetValue(key, out var entry))
            {
                if (DateTimeOffset.UtcNow <= entry.ExpiresAtUtc)
                {
                    return Task.FromResult<BotEligibilityResult?>(entry.Result);
                }

                _entries.TryRemove(key, out _);
            }

            return Task.FromResult<BotEligibilityResult?>(null);
        }

        public Task SetAsync(string broadcasterUserId, string botLoginOrId, BotEligibilityResult result, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            var key = BuildKey(broadcasterUserId, botLoginOrId);
            _entries[key] = new Entry(result, DateTimeOffset.UtcNow.Add(ttl));
            return Task.CompletedTask;
        }

        private static string BuildKey(string broadcasterUserId, string botLoginOrId)
        {
            var broadcaster = (broadcasterUserId ?? string.Empty).Trim();
            var bot = (botLoginOrId ?? string.Empty).Trim().ToLowerInvariant();
            return $"botEligibility:v1:{broadcaster}:{bot}";
        }
    }
}
