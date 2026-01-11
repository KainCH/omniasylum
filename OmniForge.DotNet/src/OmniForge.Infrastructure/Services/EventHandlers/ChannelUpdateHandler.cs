using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Utilities;

namespace OmniForge.Infrastructure.Services.EventHandlers
{
    /// <summary>
    /// Handles channel.update EventSub notifications.
    /// Used to detect game/category changes and swap counters accordingly.
    /// </summary>
    public class ChannelUpdateHandler : BaseEventSubHandler
    {
        public ChannelUpdateHandler(IServiceScopeFactory scopeFactory, ILogger<ChannelUpdateHandler> logger)
            : base(scopeFactory, logger)
        {
        }

        public override string SubscriptionType => "channel.update";

        public override async Task HandleAsync(JsonElement eventData)
        {
            var broadcasterId = GetStringProperty(eventData, "broadcaster_user_id");
            if (string.IsNullOrWhiteSpace(broadcasterId))
            {
                return;
            }

            // EventSub fields are typically category_id/category_name.
            var categoryId = GetStringProperty(eventData, "category_id") ?? string.Empty;
            var categoryName = GetStringProperty(eventData, "category_name") ?? string.Empty;

            if (string.IsNullOrWhiteSpace(categoryId))
            {
                Logger.LogDebug("channel.update received for user {UserId} but category_id missing", LogSanitizer.Sanitize(broadcasterId));
                return;
            }

            using var scope = ScopeFactory.CreateScope();
            var gameSwitch = scope.ServiceProvider.GetRequiredService<IGameSwitchService>();
            await gameSwitch.HandleGameDetectedAsync(broadcasterId, categoryId, categoryName);

            Logger.LogInformation("🎮 channel.update detected game change for {UserId}: {GameId} ({GameName})",
                LogSanitizer.Sanitize(broadcasterId), LogSanitizer.Sanitize(categoryId), LogSanitizer.Sanitize(categoryName));
        }
    }
}
