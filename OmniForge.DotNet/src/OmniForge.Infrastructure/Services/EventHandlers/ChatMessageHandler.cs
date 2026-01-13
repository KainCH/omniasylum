using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Interfaces;

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
        private readonly IMonitoringRegistry _monitoringRegistry;
        private readonly ITwitchBotEligibilityService _botEligibilityService;
        private readonly IUserRepository _userRepository;

        public ChatMessageHandler(
            IServiceScopeFactory scopeFactory,
            ILogger<ChatMessageHandler> logger,
            IDiscordInviteSender discordInviteSender,
            IChatCommandProcessor chatCommandProcessor,
            ITwitchApiService twitchApiService,
            IMonitoringRegistry monitoringRegistry,
            ITwitchBotEligibilityService botEligibilityService,
            IUserRepository userRepository)
            : base(scopeFactory, logger)
        {
            _discordInviteSender = discordInviteSender;
            _chatCommandProcessor = chatCommandProcessor;
            _twitchApiService = twitchApiService;
            _monitoringRegistry = monitoringRegistry;
            _botEligibilityService = botEligibilityService;
            _userRepository = userRepository;
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
                            // We already decided bot ownership when monitoring started.
                            // Avoid calling Helix Get Moderators from hot chat paths.
                            if (_monitoringRegistry.TryGetState(uid, out var state) && state.UseBot && !string.IsNullOrEmpty(state.BotUserId))
                            {
                                await _twitchApiService.SendChatMessageAsBotAsync(uid, state.BotUserId!, msg, replyParentMessageId: messageId);
                                return;
                            }

                            // Multi-instance fallback: MonitoringRegistry is per-instance.
                            // If the current instance doesn't have state, use the cached eligibility lookup.
                            // This keeps replies consistently on the bot/app-token path when eligible.
                            var broadcaster = await _userRepository.GetUserAsync(uid);
                            if (broadcaster != null && !string.IsNullOrWhiteSpace(broadcaster.AccessToken))
                            {
                                var eligibility = await _botEligibilityService.GetEligibilityAsync(uid, broadcaster.AccessToken);

                                // Cache decision locally for this instance.
                                _monitoringRegistry.SetState(uid, new MonitoringState(eligibility.UseBot, eligibility.BotUserId, DateTimeOffset.UtcNow));

                                if (eligibility.UseBot && !string.IsNullOrWhiteSpace(eligibility.BotUserId))
                                {
                                    await _twitchApiService.SendChatMessageAsBotAsync(uid, eligibility.BotUserId!, msg, replyParentMessageId: messageId);
                                    return;
                                }
                            }

                            Logger.LogWarning(
                                "⚠️ Skipping chat reply (must use app/bot token only). broadcaster_user_id={BroadcasterUserId}",
                                OmniForge.Core.Utilities.LogSanitizer.Sanitize(uid));
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
