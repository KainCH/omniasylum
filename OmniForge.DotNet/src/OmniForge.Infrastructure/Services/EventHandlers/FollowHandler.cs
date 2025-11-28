using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Interfaces;

namespace OmniForge.Infrastructure.Services.EventHandlers
{
    /// <summary>
    /// Handles channel.follow EventSub notifications.
    /// </summary>
    public class FollowHandler : BaseEventSubHandler
    {
        public FollowHandler(
            IServiceScopeFactory scopeFactory,
            ILogger<FollowHandler> logger)
            : base(scopeFactory, logger)
        {
        }

        public override string SubscriptionType => "channel.follow";

        public override async Task HandleAsync(JsonElement eventData)
        {
            if (!TryGetBroadcasterId(eventData, out var broadcasterId) || broadcasterId == null)
            {
                return;
            }

            string displayName = GetStringProperty(eventData, "user_name", "Someone");

            Logger.LogInformation("New follower {DisplayName} for broadcaster {BroadcasterId}", displayName, broadcasterId);

            using var scope = ScopeFactory.CreateScope();
            var overlayNotifier = scope.ServiceProvider.GetService<IOverlayNotifier>();

            if (overlayNotifier != null)
            {
                await overlayNotifier.NotifyFollowerAsync(broadcasterId, displayName);
            }
        }
    }
}
