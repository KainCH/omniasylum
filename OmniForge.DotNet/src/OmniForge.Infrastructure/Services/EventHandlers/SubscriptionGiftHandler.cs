using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Interfaces;

namespace OmniForge.Infrastructure.Services.EventHandlers
{
    /// <summary>
    /// Handles channel.subscription.gift EventSub notifications.
    /// </summary>
    public class SubscriptionGiftHandler : BaseEventSubHandler
    {
        public SubscriptionGiftHandler(
            IServiceScopeFactory scopeFactory,
            ILogger<SubscriptionGiftHandler> logger)
            : base(scopeFactory, logger)
        {
        }

        public override string SubscriptionType => "channel.subscription.gift";

        public override async Task HandleAsync(JsonElement eventData)
        {
            if (!TryGetBroadcasterId(eventData, out var broadcasterId) || broadcasterId == null)
            {
                return;
            }

            string gifterName = GetGifterName(eventData);
            int total = GetIntProperty(eventData, "total", 1);
            string tier = GetReadableTier(GetStringProperty(eventData, "tier", "1000"));

            Logger.LogInformation("{GifterName} gifted {Total} {Tier} subs for broadcaster {BroadcasterId}",
                gifterName, total, tier, broadcasterId);

            using var scope = ScopeFactory.CreateScope();
            var overlayNotifier = scope.ServiceProvider.GetService<IOverlayNotifier>();

            if (overlayNotifier != null)
            {
                // For batch gifts, recipient is "Community"
                await overlayNotifier.NotifyGiftSubAsync(broadcasterId, gifterName, "Community", tier, total);
            }
        }

        private static string GetGifterName(JsonElement eventData)
        {
            // Check if anonymous first
            if (GetBoolProperty(eventData, "is_anonymous"))
            {
                return "Anonymous";
            }

            // Try to get user_name
            if (eventData.TryGetProperty("user_name", out var nameProp) &&
                nameProp.ValueKind == JsonValueKind.String)
            {
                return nameProp.GetString() ?? "Anonymous";
            }

            return "Anonymous";
        }
    }
}
