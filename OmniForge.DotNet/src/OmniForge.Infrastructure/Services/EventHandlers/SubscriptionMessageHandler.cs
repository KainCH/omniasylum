using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Interfaces;

namespace OmniForge.Infrastructure.Services.EventHandlers
{
    /// <summary>
    /// Handles channel.subscription.message EventSub notifications (resubs with messages).
    /// </summary>
    public class SubscriptionMessageHandler : BaseEventSubHandler
    {
        private readonly IDiscordInviteSender _discordInviteSender;

        public SubscriptionMessageHandler(
            IServiceScopeFactory scopeFactory,
            ILogger<SubscriptionMessageHandler> logger,
            IDiscordInviteSender discordInviteSender)
            : base(scopeFactory, logger)
        {
            _discordInviteSender = discordInviteSender;
        }

        public override string SubscriptionType => "channel.subscription.message";

        public override async Task HandleAsync(JsonElement eventData)
        {
            if (!TryGetBroadcasterId(eventData, out var broadcasterId) || broadcasterId == null)
            {
                return;
            }

            string displayName = GetStringProperty(eventData, "user_name", "Someone");
            string message = GetMessageText(eventData);
            int months = GetIntProperty(eventData, "cumulative_months", 1);
            string tier = GetReadableTier(GetStringProperty(eventData, "tier", "1000"));

            Logger.LogInformation("{DisplayName} resubscribed for {Months} months ({Tier}) for broadcaster {BroadcasterId}",
                displayName, months, tier, broadcasterId);

            // Check for Discord keywords in resub message
            if (!string.IsNullOrEmpty(message) && ContainsDiscordKeyword(message))
            {
                await _discordInviteSender.SendDiscordInviteAsync(broadcasterId);
            }

            using var scope = ScopeFactory.CreateScope();
            var overlayNotifier = scope.ServiceProvider.GetService<IOverlayNotifier>();

            if (overlayNotifier != null)
            {
                await overlayNotifier.NotifyResubAsync(broadcasterId, displayName, months, tier, message);
            }
        }

        private static string GetMessageText(JsonElement eventData)
        {
            if (eventData.TryGetProperty("message", out var msgProp) &&
                msgProp.TryGetProperty("text", out var textProp))
            {
                return textProp.GetString() ?? "";
            }
            return "";
        }

        private static bool ContainsDiscordKeyword(string message)
        {
            return message.Contains("!discord", StringComparison.OrdinalIgnoreCase) ||
                   message.Contains("discord link", StringComparison.OrdinalIgnoreCase);
        }
    }
}
