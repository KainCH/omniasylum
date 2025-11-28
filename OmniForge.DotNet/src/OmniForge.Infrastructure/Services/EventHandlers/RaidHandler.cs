using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Interfaces;

namespace OmniForge.Infrastructure.Services.EventHandlers
{
    /// <summary>
    /// Handles channel.raid EventSub notifications.
    /// </summary>
    public class RaidHandler : BaseEventSubHandler
    {
        public RaidHandler(
            IServiceScopeFactory scopeFactory,
            ILogger<RaidHandler> logger)
            : base(scopeFactory, logger)
        {
        }

        public override string SubscriptionType => "channel.raid";

        public override async Task HandleAsync(JsonElement eventData)
        {
            // Raid event has "to_broadcaster_user_id" as the target (us)
            string? broadcasterId = GetStringProperty(eventData, "to_broadcaster_user_id");

            if (string.IsNullOrEmpty(broadcasterId))
            {
                return;
            }

            string raiderName = GetStringProperty(eventData, "from_broadcaster_user_name", "Someone");
            int viewers = GetIntProperty(eventData, "viewers");

            Logger.LogInformation("{RaiderName} raided with {Viewers} viewers for broadcaster {BroadcasterId}",
                raiderName, viewers, broadcasterId);

            using var scope = ScopeFactory.CreateScope();
            var overlayNotifier = scope.ServiceProvider.GetService<IOverlayNotifier>();

            if (overlayNotifier != null)
            {
                await overlayNotifier.NotifyRaidAsync(broadcasterId, raiderName, viewers);
            }
        }
    }
}
