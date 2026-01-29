using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniForge.Core.Utilities;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Configuration;
using OmniForge.Infrastructure.Interfaces;

namespace OmniForge.Infrastructure.Services.EventHandlers
{
    /// <summary>
    /// Handles channel.chat.message EventSub notifications.
    /// </summary>
    public class ChatMessageHandler : BaseEventSubHandler
    {
        private readonly TwitchSettings _twitchSettings;
        private readonly IDiscordInviteSender _discordInviteSender;
        private readonly IChatCommandProcessor _chatCommandProcessor;
        private readonly ITwitchApiService _twitchApiService;
        private readonly IMonitoringRegistry _monitoringRegistry;
        private readonly ITwitchBotEligibilityService _botEligibilityService;
        private readonly IUserRepository _userRepository;
        private readonly ILogValueSanitizer _logValueSanitizer;

        public ChatMessageHandler(
            IServiceScopeFactory scopeFactory,
            ILogger<ChatMessageHandler> logger,
            IOptions<TwitchSettings> twitchSettings,
            IDiscordInviteSender discordInviteSender,
            IChatCommandProcessor chatCommandProcessor,
            ITwitchApiService twitchApiService,
            IMonitoringRegistry monitoringRegistry,
            ITwitchBotEligibilityService botEligibilityService,
            IUserRepository userRepository,
            ILogValueSanitizer logValueSanitizer)
            : base(scopeFactory, logger)
        {
            _twitchSettings = twitchSettings.Value;
            _discordInviteSender = discordInviteSender;
            _chatCommandProcessor = chatCommandProcessor;
            _twitchApiService = twitchApiService;
            _monitoringRegistry = monitoringRegistry;
            _botEligibilityService = botEligibilityService;
            _userRepository = userRepository;
            _logValueSanitizer = logValueSanitizer;
        }

        public override string SubscriptionType => "channel.chat.message";

        public override async Task HandleAsync(JsonElement eventData)
        {
            try
            {
                eventData = UnwrapEvent(eventData);

                if (!TryGetBroadcasterId(eventData, out var broadcasterId) || broadcasterId == null)
                {
                    return;
                }

                var messageText = GetMessageText(eventData) ?? string.Empty;
                if (string.IsNullOrEmpty(messageText))
                {
                    return;
                }

                // Normalize unicode whitespace and trim; EventSub provides message.text, but we want consistent parsing.
                messageText = messageText.Replace('\u00A0', ' ').Trim();
                var safeMessageText = messageText!;

                var messageId = GetMessageId(eventData);

                var chatterId = GetStringProperty(eventData, "chatter_user_id", string.Empty);
                var chatterLogin = GetStringProperty(eventData, "chatter_user_login", string.Empty);
                var broadcasterLogin = GetStringProperty(eventData, "broadcaster_user_login", string.Empty);
                var messageType = GetStringProperty(eventData, "message_type", string.Empty);

                // Process chat commands via shared processor (EventSub path)
                var safeBroadcasterId = broadcasterId;
                var isBroadcaster = !string.IsNullOrEmpty(chatterId) && chatterId == safeBroadcasterId;
                var isModerator = isBroadcaster || HasBadge(eventData, "moderator");
                var isSubscriber = HasBadge(eventData, "subscriber") || HasBadge(eventData, "founder");

                if (_twitchSettings.LogChatMessages)
                {
                    Logger.LogInformation(
                        "💬 EventSub chat: broadcaster={BroadcasterLogin}({BroadcasterId}) chatter={ChatterLogin}({ChatterId}) type={MessageType} mod={IsMod} sub={IsSub} msgId={MessageId} text=\"{Text}\"",
                        _logValueSanitizer.Safe(broadcasterLogin),
                        _logValueSanitizer.Safe(broadcasterId),
                        _logValueSanitizer.Safe(chatterLogin),
                        _logValueSanitizer.Safe(chatterId),
                        _logValueSanitizer.Safe(messageType),
                        isModerator,
                        isSubscriber,
                        _logValueSanitizer.Safe(messageId),
                        _logValueSanitizer.Safe(safeMessageText));

                    if (safeMessageText!.StartsWith("!"))
                    {
                        var firstToken = safeMessageText!.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? safeMessageText;
                        Logger.LogInformation(
                            "🧩 Chat command candidate: {Token} full=\"{Text}\"",
                            _logValueSanitizer.Safe(firstToken),
                            _logValueSanitizer.Safe(safeMessageText));
                    }
                }

                if (_twitchSettings.LogChatMessagePayload)
                {
                    Logger.LogDebug(
                        "📦 EventSub chat payload (event): {Payload}",
                        eventData.GetRawText().Replace("\r", "\\r").Replace("\n", "\\n"));
                }

                if (!string.IsNullOrEmpty(safeMessageText) && safeMessageText!.StartsWith("!"))
                {
                    var context = new ChatCommandContext
                    {
                        UserId = safeBroadcasterId,
                        Message = safeMessageText,
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
                                (uid ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError(ex, "Error sending chat reply.");
                        }
                    };
                    await _chatCommandProcessor.ProcessAsync(context, sendMessage);
                }

                // Check for Discord keywords
                if (ContainsDiscordKeyword(safeMessageText!))
                {
                    await _discordInviteSender.SendDiscordInviteAsync(safeBroadcasterId);
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
