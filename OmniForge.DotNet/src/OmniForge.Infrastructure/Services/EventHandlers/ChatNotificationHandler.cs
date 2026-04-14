using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Interfaces;

namespace OmniForge.Infrastructure.Services.EventHandlers
{
    /// <summary>
    /// Handles channel.chat.notification EventSub notifications.
    /// </summary>
    public class ChatNotificationHandler : BaseEventSubHandler
    {
        private readonly IDiscordInviteSender _discordInviteSender;

        public ChatNotificationHandler(
            IServiceScopeFactory scopeFactory,
            ILogger<ChatNotificationHandler> logger,
            IDiscordInviteSender discordInviteSender)
            : base(scopeFactory, logger)
        {
            _discordInviteSender = discordInviteSender;
        }

        public override string SubscriptionType => "channel.chat.notification";

        public override async Task HandleAsync(JsonElement eventData)
        {
            try
            {
                var payload = UnwrapEvent(eventData);

                if (!TryGetBroadcasterId(payload, out var broadcasterId) || broadcasterId == null)
                {
                    return;
                }

                string noticeType = GetStringProperty(payload, "notice_type");
                Logger.LogInformation("[EventSub] Chat notification: {NoticeType} for {BroadcasterId}", noticeType, broadcasterId);

                // Check for Discord keywords in the message if present
                var messageText = GetMessageText(payload);
                if (!string.IsNullOrEmpty(messageText) && ContainsDiscordKeyword(messageText))
                {
                    await _discordInviteSender.SendDiscordInviteAsync(broadcasterId);
                }

                string chatterName = GetStringProperty(payload, "chatter_user_name", "Someone");

                using var scope = ScopeFactory.CreateScope();
                var overlayNotifier = scope.ServiceProvider.GetService<IOverlayNotifier>();

                if (overlayNotifier == null)
                {
                    return;
                }

                var feedService = scope.ServiceProvider.GetService<IDashboardFeedService>();
                var botReaction = scope.ServiceProvider.GetService<IBotReactionService>();
                await HandleNoticeTypeAsync(payload, broadcasterId, chatterName, noticeType, overlayNotifier, feedService, botReaction);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[EventSub] Error handling chat notification.");
            }
        }

        private async Task HandleNoticeTypeAsync(
            JsonElement eventData,
            string broadcasterId,
            string chatterName,
            string noticeType,
            IOverlayNotifier overlayNotifier,
            IDashboardFeedService? feedService = null,
            IBotReactionService? botReaction = null)
        {
            switch (noticeType)
            {
                case "sub":
                    await HandleSubNoticeAsync(eventData, broadcasterId, chatterName, overlayNotifier);
                    if (eventData.TryGetProperty("sub", out var subFeedProp))
                    {
                        string tierRaw = GetStringProperty(subFeedProp, "sub_plan", GetStringProperty(subFeedProp, "sub_tier", "1000"));
                        string tier = GetReadableTier(tierRaw);
                        feedService?.PushEvent(broadcasterId, new DashboardEvent("sub", $"⭐ {chatterName} subscribed! ({tier})", DateTimeOffset.UtcNow));
                        if (botReaction != null) await botReaction.HandleNewSubAsync(broadcasterId, chatterName, tier);
                    }
                    break;

                case "resub":
                    await HandleResubNoticeAsync(eventData, broadcasterId, chatterName, overlayNotifier);
                    break;

                case "sub_gift":
                    await HandleSubGiftNoticeAsync(eventData, broadcasterId, chatterName, overlayNotifier);
                    break;

                case "community_sub_gift":
                    await HandleCommunitySubGiftNoticeAsync(eventData, broadcasterId, chatterName, overlayNotifier);
                    break;

                case "raid":
                    await HandleRaidNoticeAsync(eventData, broadcasterId, chatterName, overlayNotifier);
                    break;

                case "announcement":
                    // Optional: Handle announcements
                    break;
            }
        }

        private static async Task HandleSubNoticeAsync(
            JsonElement eventData,
            string broadcasterId,
            string chatterName,
            IOverlayNotifier overlayNotifier)
        {
            if (eventData.TryGetProperty("sub", out var subProp))
            {
                string tierRaw = GetStringProperty(subProp, "sub_plan", GetStringProperty(subProp, "sub_tier", "1000"));
                string tier = GetReadableTier(tierRaw);
                bool isGift = GetBoolProperty(subProp, "is_gift");

                await overlayNotifier.NotifySubscriberAsync(broadcasterId, chatterName, tier, isGift);
            }
        }

        private async Task HandleResubNoticeAsync(
            JsonElement eventData,
            string broadcasterId,
            string chatterName,
            IOverlayNotifier overlayNotifier)
        {
            if (eventData.TryGetProperty("resub", out var resubProp))
            {
                int months = GetIntProperty(resubProp, "cumulative_months", 1);
                string tierRaw = GetStringProperty(resubProp, "sub_plan", GetStringProperty(resubProp, "sub_tier", "1000"));
                string tier = GetReadableTier(tierRaw);
                string message = GetMessageText(eventData);

                await overlayNotifier.NotifyResubAsync(broadcasterId, chatterName, months, tier, message);
            }
        }

        private static async Task HandleSubGiftNoticeAsync(
            JsonElement eventData,
            string broadcasterId,
            string chatterName,
            IOverlayNotifier overlayNotifier)
        {
            if (eventData.TryGetProperty("sub_gift", out var giftProp))
            {
                string tierRaw = GetStringProperty(giftProp, "sub_plan", GetStringProperty(giftProp, "sub_tier", "1000"));
                string tier = GetReadableTier(tierRaw);
                string recipientName = GetStringProperty(giftProp, "recipient_user_name", "Someone");

                await overlayNotifier.NotifyGiftSubAsync(broadcasterId, chatterName, recipientName, tier, 1);
            }
        }

        private static async Task HandleCommunitySubGiftNoticeAsync(
            JsonElement eventData,
            string broadcasterId,
            string chatterName,
            IOverlayNotifier overlayNotifier)
        {
            if (eventData.TryGetProperty("community_sub_gift", out var commGiftProp))
            {
                int total = GetIntProperty(commGiftProp, "total", 1);
                string tierRaw = GetStringProperty(commGiftProp, "sub_plan", GetStringProperty(commGiftProp, "sub_tier", "1000"));
                string tier = GetReadableTier(tierRaw);

                await overlayNotifier.NotifyGiftSubAsync(broadcasterId, chatterName, "Community", tier, total);
            }
        }

        private static async Task HandleRaidNoticeAsync(
            JsonElement eventData,
            string broadcasterId,
            string chatterName,
            IOverlayNotifier overlayNotifier)
        {
            if (eventData.TryGetProperty("raid", out var raidProp))
            {
                int viewers = GetIntProperty(raidProp, "viewer_count");

                await overlayNotifier.NotifyRaidAsync(broadcasterId, chatterName, viewers);
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
