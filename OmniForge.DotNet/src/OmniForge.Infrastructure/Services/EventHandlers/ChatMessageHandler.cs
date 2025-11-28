using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace OmniForge.Infrastructure.Services.EventHandlers
{
    /// <summary>
    /// Handles channel.chat.message EventSub notifications.
    /// </summary>
    public class ChatMessageHandler : BaseEventSubHandler
    {
        private readonly IDiscordInviteSender _discordInviteSender;

        public ChatMessageHandler(
            IServiceScopeFactory scopeFactory,
            ILogger<ChatMessageHandler> logger,
            IDiscordInviteSender discordInviteSender)
            : base(scopeFactory, logger)
        {
            _discordInviteSender = discordInviteSender;
        }

        public override string SubscriptionType => "channel.chat.message";

        public override async Task HandleAsync(JsonElement eventData)
        {
            try
            {
                if (!TryGetBroadcasterId(eventData, out var broadcasterId) || broadcasterId == null)
                {
                    return;
                }

                var messageText = GetMessageText(eventData);
                if (string.IsNullOrEmpty(messageText))
                {
                    return;
                }

                // Check for Discord keywords
                if (ContainsDiscordKeyword(messageText))
                {
                    await _discordInviteSender.SendDiscordInviteAsync(broadcasterId);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error handling chat message.");
            }
        }

        private static string GetMessageText(JsonElement eventData)
        {
            if (eventData.TryGetProperty("message", out var messageProp) &&
                messageProp.TryGetProperty("text", out var textProp))
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
