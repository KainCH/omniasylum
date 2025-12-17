using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Interfaces;

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
                Logger.LogInformation("Chat Notification: {NoticeType} for {BroadcasterId}", noticeType, broadcasterId);

                // Check for Discord keywords in the message if present
                var messageText = GetMessageText(payload);
                if (!string.IsNullOrEmpty(messageText) && ContainsDiscordKeyword(messageText))
                {
                    await _discordInviteSender.SendDiscordInviteAsync(broadcasterId);
                }

                string chatterName = GetStringProperty(payload, "chatter_user_name", "Someone");

                using var scope = ScopeFactory.CreateScope();
                var alertRouter = scope.ServiceProvider.GetService<IAlertEventRouter>();
                var overlayNotifier = scope.ServiceProvider.GetService<IOverlayNotifier>();

                if (alertRouter == null && overlayNotifier == null)
                {
                    return;
                }

                await HandleNoticeTypeAsync(payload, broadcasterId, chatterName, noticeType, alertRouter, overlayNotifier);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error handling chat notification.");
            }
        }

        private async Task HandleNoticeTypeAsync(
            JsonElement eventData,
            string broadcasterId,
            string chatterName,
            string noticeType,
            IAlertEventRouter? alertRouter,
            IOverlayNotifier? overlayNotifier)
        {
            switch (noticeType)
            {
                case "sub":
                    await HandleSubNoticeAsync(eventData, broadcasterId, chatterName, alertRouter, overlayNotifier);
                    break;

                case "resub":
                    await HandleResubNoticeAsync(eventData, broadcasterId, chatterName, alertRouter, overlayNotifier);
                    break;

                case "sub_gift":
                    await HandleSubGiftNoticeAsync(eventData, broadcasterId, chatterName, alertRouter, overlayNotifier);
                    break;

                case "community_sub_gift":
                    await HandleCommunitySubGiftNoticeAsync(eventData, broadcasterId, chatterName, alertRouter, overlayNotifier);
                    break;

                case "raid":
                    await HandleRaidNoticeAsync(eventData, broadcasterId, chatterName, alertRouter, overlayNotifier);
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
            IAlertEventRouter? alertRouter,
            IOverlayNotifier? overlayNotifier)
        {
            if (eventData.TryGetProperty("sub", out var subProp))
            {
                string tierRaw = GetStringProperty(subProp, "sub_plan", GetStringProperty(subProp, "sub_tier", "1000"));
                string tier = GetReadableTier(tierRaw);
                bool isGift = GetBoolProperty(subProp, "is_gift");

                var alertData = new { user = chatterName, displayName = chatterName, tier, isGift };

                if (alertRouter != null)
                {
                    await alertRouter.RouteAsync(broadcasterId, "chat_notification_subscribe", "subscription", alertData);
                    return;
                }

                if (overlayNotifier != null)
                {
                    await overlayNotifier.NotifySubscriberAsync(broadcasterId, chatterName, tier, isGift);
                }
            }
        }

        private async Task HandleResubNoticeAsync(
            JsonElement eventData,
            string broadcasterId,
            string chatterName,
            IAlertEventRouter? alertRouter,
            IOverlayNotifier? overlayNotifier)
        {
            if (eventData.TryGetProperty("resub", out var resubProp))
            {
                int months = GetIntProperty(resubProp, "cumulative_months", 1);
                string tierRaw = GetStringProperty(resubProp, "sub_plan", GetStringProperty(resubProp, "sub_tier", "1000"));
                string tier = GetReadableTier(tierRaw);
                bool isGift = GetBoolProperty(resubProp, "is_gift");
                string message = GetMessageText(eventData);

                var alertData = new { user = chatterName, displayName = chatterName, months, tier, isGift, message };

                if (alertRouter != null)
                {
                    await alertRouter.RouteAsync(broadcasterId, "chat_notification_resub", "resub", alertData);
                    return;
                }

                if (overlayNotifier != null)
                {
                    await overlayNotifier.NotifyResubAsync(broadcasterId, chatterName, months, tier, message);
                }
            }
        }

        private static async Task HandleSubGiftNoticeAsync(
            JsonElement eventData,
            string broadcasterId,
            string chatterName,
            IAlertEventRouter? alertRouter,
            IOverlayNotifier? overlayNotifier)
        {
            if (eventData.TryGetProperty("sub_gift", out var giftProp))
            {
                string tierRaw = GetStringProperty(giftProp, "sub_plan", GetStringProperty(giftProp, "sub_tier", "1000"));
                string tier = GetReadableTier(tierRaw);
                string recipientName = GetStringProperty(giftProp, "recipient_user_name", "Someone");

                // This is a single gift, so totalGifts = 1
                var alertData = new { user = chatterName, gifterName = chatterName, recipientName, tier, totalGifts = 1 };

                if (alertRouter != null)
                {
                    await alertRouter.RouteAsync(broadcasterId, "chat_notification_gift_sub", "giftsub", alertData);
                    return;
                }

                if (overlayNotifier != null)
                {
                    await overlayNotifier.NotifyGiftSubAsync(broadcasterId, chatterName, recipientName, tier, 1);
                }
            }
        }

        private static async Task HandleCommunitySubGiftNoticeAsync(
            JsonElement eventData,
            string broadcasterId,
            string chatterName,
            IAlertEventRouter? alertRouter,
            IOverlayNotifier? overlayNotifier)
        {
            if (eventData.TryGetProperty("community_sub_gift", out var commGiftProp))
            {
                int total = GetIntProperty(commGiftProp, "total", 1);
                string tierRaw = GetStringProperty(commGiftProp, "sub_plan", GetStringProperty(commGiftProp, "sub_tier", "1000"));
                string tier = GetReadableTier(tierRaw);

                // For community gifts, recipient is "Community"
                var alertData = new { user = chatterName, gifterName = chatterName, recipientName = "Community", tier, totalGifts = total };

                if (alertRouter != null)
                {
                    await alertRouter.RouteAsync(broadcasterId, "chat_notification_community_gift", "giftsub", alertData);
                    return;
                }

                if (overlayNotifier != null)
                {
                    await overlayNotifier.NotifyGiftSubAsync(broadcasterId, chatterName, "Community", tier, total);
                }
            }
        }

        private static async Task HandleRaidNoticeAsync(
            JsonElement eventData,
            string broadcasterId,
            string chatterName,
            IAlertEventRouter? alertRouter,
            IOverlayNotifier? overlayNotifier)
        {
            if (eventData.TryGetProperty("raid", out var raidProp))
            {
                int viewers = GetIntProperty(raidProp, "viewer_count");
                // The chatter is the raider
                var alertData = new { user = chatterName, raiderName = chatterName, viewers };

                if (alertRouter != null)
                {
                    await alertRouter.RouteAsync(broadcasterId, "chat_notification_raid", "raid", alertData);
                    return;
                }

                if (overlayNotifier != null)
                {
                    await overlayNotifier.NotifyRaidAsync(broadcasterId, chatterName, viewers);
                }
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
