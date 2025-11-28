using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Interfaces;

namespace OmniForge.Infrastructure.Services.EventHandlers
{
    /// <summary>
    /// Handles channel.cheer EventSub notifications.
    /// </summary>
    public class CheerHandler : BaseEventSubHandler
    {
        public CheerHandler(
            IServiceScopeFactory scopeFactory,
            ILogger<CheerHandler> logger)
            : base(scopeFactory, logger)
        {
        }

        public override string SubscriptionType => "channel.cheer";

        public override async Task HandleAsync(JsonElement eventData)
        {
            if (!TryGetBroadcasterId(eventData, out var broadcasterId) || broadcasterId == null)
            {
                return;
            }

            string displayName = GetCheerDisplayName(eventData);
            int bits = GetIntProperty(eventData, "bits");
            string message = GetStringProperty(eventData, "message");

            Logger.LogInformation("{DisplayName} cheered {Bits} bits for broadcaster {BroadcasterId}",
                displayName, bits, broadcasterId);

            using var scope = ScopeFactory.CreateScope();
            var overlayNotifier = scope.ServiceProvider.GetService<IOverlayNotifier>();

            if (overlayNotifier != null)
            {
                // totalBits is unknown from this event, passing 0
                await overlayNotifier.NotifyBitsAsync(broadcasterId, displayName, bits, message, 0);
            }
        }

        private static string GetCheerDisplayName(JsonElement eventData)
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
