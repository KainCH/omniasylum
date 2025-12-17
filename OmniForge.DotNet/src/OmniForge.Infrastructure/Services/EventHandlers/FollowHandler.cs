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
            var payload = UnwrapEvent(eventData);

            if (!TryGetBroadcasterId(payload, out var broadcasterId) || broadcasterId == null)
            {
                return;
            }

            string displayName = GetStringProperty(payload, "user_name", "Someone");

            Logger.LogInformation("New follower {DisplayName} for broadcaster {BroadcasterId}", displayName, broadcasterId);

            using var scope = ScopeFactory.CreateScope();
            var alertRouter = scope.ServiceProvider.GetService<IAlertEventRouter>();
            var overlayNotifier = scope.ServiceProvider.GetService<IOverlayNotifier>();

            var alertData = new { user = displayName, displayName };

            if (alertRouter != null)
            {
                await alertRouter.RouteAsync(broadcasterId, "channel.follow", "follow", alertData);
                return;
            }

            // Fallback if alert router isn't available
            if (overlayNotifier != null)
            {
                await overlayNotifier.NotifyFollowerAsync(broadcasterId, displayName);
            }
        }
    }
}
