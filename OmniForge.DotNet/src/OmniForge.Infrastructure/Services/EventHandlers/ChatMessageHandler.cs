using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Services;

namespace OmniForge.Infrastructure.Services.EventHandlers
{
    /// <summary>
    /// Handles channel.chat.message EventSub notifications.
    /// </summary>
    public class ChatMessageHandler : BaseEventSubHandler
    {
        private readonly IDiscordInviteSender _discordInviteSender;
        private readonly IChatCommandProcessor _chatCommandProcessor;
        private readonly ITwitchApiService _twitchApiService;

        public ChatMessageHandler(
            IServiceScopeFactory scopeFactory,
            ILogger<ChatMessageHandler> logger,
            IDiscordInviteSender discordInviteSender,
            IChatCommandProcessor chatCommandProcessor,
            ITwitchApiService twitchApiService)
            : base(scopeFactory, logger)
        {
            _discordInviteSender = discordInviteSender;
            _chatCommandProcessor = chatCommandProcessor;
            _twitchApiService = twitchApiService;
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

                var messageId = GetMessageId(eventData);

                // Process chat commands via shared processor (EventSub path)
                var chatterId = GetStringProperty(eventData, "chatter_user_id", string.Empty);
                var isBroadcaster = !string.IsNullOrEmpty(chatterId) && chatterId == broadcasterId;
                var isModerator = isBroadcaster || HasBadge(eventData, "moderator");
                var isSubscriber = HasBadge(eventData, "subscriber") || HasBadge(eventData, "founder");

                if (!string.IsNullOrEmpty(messageText) && messageText.StartsWith("!"))
                {
                    var context = new ChatCommandContext
                    {
                        UserId = broadcasterId,
                        Message = messageText,
                        IsModerator = isModerator,
                        IsBroadcaster = isBroadcaster,
                        IsSubscriber = isSubscriber
                    };

                    Func<string, string, Task> sendMessage = (uid, msg) => _twitchApiService.SendChatMessageAsync(uid, msg, replyParentMessageId: messageId);
                    await _chatCommandProcessor.ProcessAsync(context, sendMessage);
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

        private static bool HasBadge(JsonElement eventData, string badgeSetId)
        {
            if (eventData.TryGetProperty("badges", out var badgesElem) && badgesElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var badge in badgesElem.EnumerateArray())
                {
                    if (badge.TryGetProperty("set_id", out var setIdProp))
                    {
                        var setId = setIdProp.GetString();
                        if (!string.IsNullOrEmpty(setId) && string.Equals(setId, badgeSetId, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }
        private static string? GetMessageId(JsonElement eventData)
        {
            if (eventData.TryGetProperty("message_id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
            {
                return idProp.GetString();
            }
            return null;
        }
    }
}
