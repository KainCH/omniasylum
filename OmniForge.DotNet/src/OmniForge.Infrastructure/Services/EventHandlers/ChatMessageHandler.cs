using System;
using System.Linq;
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
        private readonly IUserRepository _userRepository;
        private readonly ITwitchBotEligibilityService _botEligibilityService;

        public ChatMessageHandler(
            IServiceScopeFactory scopeFactory,
            ILogger<ChatMessageHandler> logger,
            IDiscordInviteSender discordInviteSender,
            IChatCommandProcessor chatCommandProcessor,
            ITwitchApiService twitchApiService,
            IUserRepository userRepository,
            ITwitchBotEligibilityService botEligibilityService)
            : base(scopeFactory, logger)
        {
            _discordInviteSender = discordInviteSender;
            _chatCommandProcessor = chatCommandProcessor;
            _twitchApiService = twitchApiService;
            _userRepository = userRepository;
            _botEligibilityService = botEligibilityService;
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

                    Func<string, string, Task> sendMessage = async (uid, msg) =>
                    {
                        try
                        {
                            var broadcaster = await _userRepository.GetUserAsync(uid);
                            if (broadcaster != null && !string.IsNullOrEmpty(broadcaster.AccessToken))
                            {
                                var eligibility = await _botEligibilityService.GetEligibilityAsync(uid, broadcaster.AccessToken);
                                if (eligibility.UseBot && !string.IsNullOrEmpty(eligibility.BotUserId))
                                {
                                    await _twitchApiService.SendChatMessageAsBotAsync(uid, eligibility.BotUserId!, msg, replyParentMessageId: messageId);
                                    return;
                                }
                            }

                            await _twitchApiService.SendChatMessageAsync(uid, msg, replyParentMessageId: messageId);
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError(ex, "Error sending chat reply.");
                        }
                    };
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
                foreach (var badge in badgesElem.EnumerateArray().Where(badge =>
                             badge.TryGetProperty("set_id", out var setIdProp) &&
                             setIdProp.ValueKind == JsonValueKind.String &&
                             string.Equals(setIdProp.GetString(), badgeSetId, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
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
