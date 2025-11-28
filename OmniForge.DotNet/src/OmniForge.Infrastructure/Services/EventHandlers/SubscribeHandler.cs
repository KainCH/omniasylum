using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Interfaces;

namespace OmniForge.Infrastructure.Services.EventHandlers
{
    /// <summary>
    /// Handles channel.subscribe EventSub notifications.
    /// </summary>
    public class SubscribeHandler : BaseEventSubHandler
    {
        public SubscribeHandler(
            IServiceScopeFactory scopeFactory,
            ILogger<SubscribeHandler> logger)
            : base(scopeFactory, logger)
        {
        }

        public override string SubscriptionType => "channel.subscribe";

        public override async Task HandleAsync(JsonElement eventData)
        {
            if (!TryGetBroadcasterId(eventData, out var broadcasterId) || broadcasterId == null)
            {
                return;
            }

            string displayName = GetStringProperty(eventData, "user_name", "Someone");
            string tier = GetReadableTier(GetStringProperty(eventData, "tier", "1000"));
            bool isGift = GetBoolProperty(eventData, "is_gift");

            Logger.LogInformation("New subscriber {DisplayName} ({Tier}, gift: {IsGift}) for broadcaster {BroadcasterId}",
                displayName, tier, isGift, broadcasterId);

            using var scope = ScopeFactory.CreateScope();
            var overlayNotifier = scope.ServiceProvider.GetService<IOverlayNotifier>();

            if (overlayNotifier != null)
            {
                await overlayNotifier.NotifySubscriberAsync(broadcasterId, displayName, tier, isGift);
            }
        }
    }
}
