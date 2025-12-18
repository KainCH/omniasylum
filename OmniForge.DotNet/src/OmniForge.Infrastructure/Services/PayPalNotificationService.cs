using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Utilities;

namespace OmniForge.Infrastructure.Services
{
    /// <summary>
    /// Service for sending PayPal donation notifications via chat, overlay, and Discord.
    /// </summary>
    public class PayPalNotificationService : IPayPalNotificationService
    {
        private readonly ITwitchClientManager _twitchClientManager;
        private readonly IOverlayNotifier _overlayNotifier;
        private readonly IDiscordService _discordService;
        private readonly IPayPalRepository _paypalRepository;
        private readonly ILogger<PayPalNotificationService> _logger;

        public PayPalNotificationService(
            ITwitchClientManager twitchClientManager,
            IOverlayNotifier overlayNotifier,
            IDiscordService discordService,
            IPayPalRepository paypalRepository,
            ILogger<PayPalNotificationService> logger)
        {
            _twitchClientManager = twitchClientManager;
            _overlayNotifier = overlayNotifier;
            _discordService = discordService;
            _paypalRepository = paypalRepository;
            _logger = logger;
        }

        public async Task<bool> SendDonationNotificationsAsync(User user, PayPalDonation donation)
        {
            if (donation.NotificationSent)
            {
                _logger.LogInformation("⚠️ Notification already sent for transaction {TxnId}", donation.TransactionId);
                return false;
            }

            var settings = user.Features.PayPalSettings;
            var donorName = GetDonorDisplayName(donation, settings);
            var success = true;

            _logger.LogInformation("📤 Sending PayPal donation notifications for ${Amount} from {Donor}",
                donation.Amount, donorName);

            try
            {
                // 1. Send Chat Message
                if (settings.ChatNotifications)
                {
                    await SendChatNotificationAsync(user, donation, donorName, settings);
                }

                // 2. Send Overlay Alert
                if (settings.OverlayAlerts)
                {
                    await SendOverlayNotificationAsync(user, donation, donorName);
                }

                // 3. Send Discord Notification
                if (user.Features.DiscordNotifications &&
                    (!string.IsNullOrEmpty(user.DiscordChannelId) || !string.IsNullOrEmpty(user.DiscordWebhookUrl)))
                {
                    await SendDiscordNotificationAsync(user, donation, donorName);
                }

                // Mark notification as sent
                donation.NotificationSent = true;
                await _paypalRepository.MarkNotificationSentAsync(donation.UserId, donation.TransactionId);

                _logger.LogInformation("✅ PayPal donation notifications sent successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error sending PayPal donation notifications");
                success = false;
            }

            return success;
        }

        private async Task SendChatNotificationAsync(User user, PayPalDonation donation, string donorName, PayPalSettings settings)
        {
            try
            {
                var message = FormatChatMessage(settings.ChatMessageTemplate, donation, donorName);
                await _twitchClientManager.SendMessageAsync(user.TwitchUserId, message);
                _logger.LogInformation("💬 Chat notification sent: {Message}", message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error sending chat notification");
            }
        }

        private async Task SendOverlayNotificationAsync(User user, PayPalDonation donation, string donorName)
        {
            try
            {
                var matchedTwitchUser = !string.IsNullOrEmpty(donation.MatchedTwitchUserId);
                await _overlayNotifier.NotifyPayPalDonationAsync(
                    user.TwitchUserId,
                    donorName,
                    donation.Amount,
                    donation.Currency,
                    donation.Message,
                    matchedTwitchUser);
                _logger.LogInformation("🎬 Overlay notification sent");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error sending overlay notification");
            }
        }

        private async Task SendDiscordNotificationAsync(User user, PayPalDonation donation, string donorName)
        {
            try
            {
                var matchedInfo = !string.IsNullOrEmpty(donation.MatchedTwitchUserId)
                    ? $" (Twitch: {donation.MatchedTwitchDisplayName})"
                    : string.Empty;

                await _discordService.SendNotificationAsync(user, "paypal_donation", new
                {
                    donorName,
                    amount = donation.Amount,
                    currency = donation.Currency,
                    message = donation.Message,
                    transactionId = donation.TransactionId,
                    fields = new[]
                    {
                        new { name = "💸 Amount", value = $"${donation.Amount:F2} {donation.Currency}", inline = true },
                        new { name = "👤 Donor", value = $"{donorName}{matchedInfo}", inline = true },
                        new { name = "💬 Message", value = string.IsNullOrEmpty(donation.Message) ? "(No message)" : donation.Message, inline = false }
                    }
                });
                _logger.LogInformation("📣 Discord notification sent");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error sending Discord notification");
            }
        }

        private static string GetDonorDisplayName(PayPalDonation donation, PayPalSettings settings)
        {
            // If Twitch matching is enabled and we found a match, use Twitch display name
            if (settings.ShowMatchedTwitchName && !string.IsNullOrEmpty(donation.MatchedTwitchDisplayName))
            {
                return donation.MatchedTwitchDisplayName;
            }

            // Fall back to PayPal payer name
            return !string.IsNullOrEmpty(donation.PayerName) ? donation.PayerName : "Anonymous";
        }

        private static string FormatChatMessage(string template, PayPalDonation donation, string donorName)
        {
            if (string.IsNullOrEmpty(template))
            {
                template = "💸 Thanks {name} for the ${amount} donation!";
            }

            return template
                .Replace("{name}", donorName, StringComparison.OrdinalIgnoreCase)
                .Replace("{amount}", donation.Amount.ToString("F2"), StringComparison.OrdinalIgnoreCase)
                .Replace("{currency}", donation.Currency, StringComparison.OrdinalIgnoreCase)
                .Replace("{message}", donation.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
    }
}
