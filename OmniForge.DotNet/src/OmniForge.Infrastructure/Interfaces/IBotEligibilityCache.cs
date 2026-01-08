using System;
using System.Threading;
using System.Threading.Tasks;
using OmniForge.Core.Interfaces;

namespace OmniForge.Infrastructure.Interfaces
{
    public interface IBotEligibilityCache
    {
        Task<BotEligibilityResult?> TryGetAsync(string broadcasterUserId, string botLoginOrId, CancellationToken cancellationToken = default);

        Task SetAsync(
            string broadcasterUserId,
            string botLoginOrId,
            BotEligibilityResult result,
            TimeSpan ttl,
            CancellationToken cancellationToken = default);
    }
}
