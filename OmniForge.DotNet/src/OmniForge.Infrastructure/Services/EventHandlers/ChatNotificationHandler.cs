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
                if (!TryGetBroadcasterId(eventData, out var broadcasterId) || broadcasterId == null)
                {
                    return;
                }

                string noticeType = GetStringProperty(eventData, "notice_type");
                Logger.LogInformation("Chat Notification: {NoticeType} for {BroadcasterId}", noticeType, broadcasterId);

                // Check for Discord keywords in the message if present
                var messageText = GetMessageText(eventData);
                if (!string.IsNullOrEmpty(messageText) && ContainsDiscordKeyword(messageText))
                {
                    await _discordInviteSender.SendDiscordInviteAsync(broadcasterId);
                }

                string chatterName = GetStringProperty(eventData, "chatter_user_name", "Someone");

                using var scope = ScopeFactory.CreateScope();
                var overlayNotifier = scope.ServiceProvider.GetService<IOverlayNotifier>();

                if (overlayNotifier == null)
                {
                    return;
                }

                await HandleNoticeTypeAsync(eventData, broadcasterId, chatterName, noticeType, overlayNotifier);
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
            IOverlayNotifier overlayNotifier)
        {
            switch (noticeType)
            {
                case "sub":
                    await HandleSubNoticeAsync(eventData, broadcasterId, chatterName, overlayNotifier);
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
                string tier = GetReadableTier(GetStringProperty(subProp, "sub_tier", "1000"));
                await overlayNotifier.NotifySubscriberAsync(broadcasterId, chatterName, tier, false);
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
                string tier = GetReadableTier(GetStringProperty(resubProp, "sub_tier", "1000"));
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
                string tier = GetReadableTier(GetStringProperty(giftProp, "sub_tier", "1000"));
                string recipientName = GetStringProperty(giftProp, "recipient_user_name", "Someone");

                // This is a single gift, so totalGifts = 1
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
                string tier = GetReadableTier(GetStringProperty(commGiftProp, "sub_tier", "1000"));

                // For community gifts, recipient is "Community"
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
                // The chatter is the raider
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
