using System;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Utilities;

namespace OmniForge.Web.Services
{
    public class WebSocketOverlayNotifier : IOverlayNotifier
    {
        private readonly IWebSocketOverlayManager _webSocketManager;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<WebSocketOverlayNotifier> _logger;

        public WebSocketOverlayNotifier(
            IWebSocketOverlayManager webSocketManager,
            IServiceScopeFactory scopeFactory,
            ILogger<WebSocketOverlayNotifier> logger)
        {
            _webSocketManager = webSocketManager;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task NotifyCounterUpdateAsync(string userId, Counter counter)
        {
            LogOverlayAction(userId, "counterUpdate");

            await _webSocketManager.SendToUserAsync(userId, "counterUpdate", counter ?? new Counter { TwitchUserId = userId });
        }

        public async Task NotifyMilestoneReachedAsync(string userId, string counterType, int milestone, int newValue, int previousMilestone)
        {
            LogOverlayAction(userId, "milestoneReached");

            await _webSocketManager.SendToUserAsync(userId, "milestoneReached", new { counterType, milestone, newValue, previousMilestone });
        }

        public async Task NotifySettingsUpdateAsync(string userId, OverlaySettings settings)
        {
            LogOverlayAction(userId, "settingsUpdate");

            await _webSocketManager.SendToUserAsync(userId, "settingsUpdate", settings);
        }

        public async Task NotifyStreamStatusUpdateAsync(string userId, string status)
        {
            LogOverlayAction(userId, "streamStatusUpdate");

            await _webSocketManager.SendToUserAsync(userId, "streamStatusUpdate", new { streamStatus = status });
        }

        public async Task NotifyStreamStartedAsync(string userId, Counter counter)
        {
            LogOverlayAction(userId, "streamStarted");

            await _webSocketManager.SendToUserAsync(userId, "streamStarted", counter);
        }

        public async Task NotifyStreamEndedAsync(string userId, Counter counter)
        {
            LogOverlayAction(userId, "streamEnded");

            await _webSocketManager.SendToUserAsync(userId, "streamEnded", counter);
        }

        public async Task NotifyFollowerAsync(string userId, string displayName)
        {
            LogOverlayAction(userId, "follow");

            var data = new { name = displayName, displayName, textPrompt = $"New Follower: {displayName}" };
            var (enricher, scope) = CreateEnricher();
            using (scope)
            {
                var payload = await enricher.EnrichPayloadAsync(userId, "follow", data);
                if (enricher.IsSuppressed(payload)) return;
                await _webSocketManager.SendToUserAsync(userId, "follow", payload);
            }
        }

        public async Task NotifySubscriberAsync(string userId, string displayName, string tier, bool isGift)
        {
            LogOverlayAction(userId, "subscription");

            var data = new { name = displayName, displayName, tier, isGift, textPrompt = $"New Subscriber: {displayName}" };
            var (enricher, scope) = CreateEnricher();
            using (scope)
            {
                var payload = await enricher.EnrichPayloadAsync(userId, "subscription", data);
                if (enricher.IsSuppressed(payload)) return;
                await _webSocketManager.SendToUserAsync(userId, "subscription", payload);
            }
        }

        public async Task NotifyResubAsync(string userId, string displayName, int months, string tier, string message)
        {
            LogOverlayAction(userId, "resub");

            var data = new { name = displayName, displayName, months, tier, textPrompt = $"{displayName} Resubscribed x{months}" };
            var (enricher, scope) = CreateEnricher();
            using (scope)
            {
                var payload = await enricher.EnrichPayloadAsync(userId, "resub", data);
                if (enricher.IsSuppressed(payload)) return;
                await _webSocketManager.SendToUserAsync(userId, "resub", payload);
            }
        }

        public async Task NotifyGiftSubAsync(string userId, string gifterName, string recipientName, string tier, int totalGifts)
        {
            LogOverlayAction(userId, "giftsub");

            var data = new { name = gifterName, gifterName, recipientName, tier, totalGifts, textPrompt = $"{gifterName} Gifted {totalGifts} Subs" };
            var (enricher, scope) = CreateEnricher();
            using (scope)
            {
                var payload = await enricher.EnrichPayloadAsync(userId, "giftsub", data);
                if (enricher.IsSuppressed(payload)) return;
                await _webSocketManager.SendToUserAsync(userId, "giftsub", payload);
            }
        }

        public async Task NotifyBitsAsync(string userId, string displayName, int amount, string message, int totalBits)
        {
            LogOverlayAction(userId, "bits");

            var data = new { name = displayName, displayName, amount, totalBits, textPrompt = $"{displayName} Cheered {amount} Bits" };
            var (enricher, scope) = CreateEnricher();
            using (scope)
            {
                var payload = await enricher.EnrichPayloadAsync(userId, "bits", data);
                if (enricher.IsSuppressed(payload)) return;
                await _webSocketManager.SendToUserAsync(userId, "bits", payload);
            }
        }

        public async Task NotifyRaidAsync(string userId, string raiderName, int viewers)
        {
            LogOverlayAction(userId, "raid");

            var data = new { name = raiderName, raiderName, viewers, textPrompt = $"Raid: {raiderName} ({viewers})" };
            var (enricher, scope) = CreateEnricher();
            using (scope)
            {
                var payload = await enricher.EnrichPayloadAsync(userId, "raid", data);
                if (enricher.IsSuppressed(payload)) return;
                await _webSocketManager.SendToUserAsync(userId, "raid", payload);
            }
        }

        public async Task NotifyCustomAlertAsync(string userId, string alertType, object data)
        {
            var safeUserId = userId ?? string.Empty;
            var safeAlertType = alertType ?? string.Empty;

            LogOverlayAction(safeUserId, "customAlert", safeAlertType);

            if (IsOverlayNotificationPayloadLoggingEnabled())
            {
                try
                {
                    var json = JsonSerializer.Serialize(data);
                    if (json.Length > 2000)
                    {
                        json = json.Substring(0, 2000) + "…";
                    }
                    _logger.LogInformation(
                        "Overlay payload: user_id={UserId}, alert_type={AlertType}, data={Data}",
                        LogValue.Safe(userId),
                        LogValue.Safe(alertType),
                        json);
                }
                catch
                {
                    // Ignore serialization issues for logging.
                }
            }

            var (enricher, scope) = CreateEnricher();
            using (scope)
            {
                var payload = await enricher.EnrichPayloadAsync(safeUserId, safeAlertType, data);
                await _webSocketManager.SendToUserAsync(safeUserId, "customAlert", new { alertType = safeAlertType, data = payload });
            }
        }

        public async Task NotifyTemplateChangedAsync(string userId, string templateStyle, Template template)
        {
            await _webSocketManager.SendToUserAsync(userId, "templateChanged", new { templateStyle, template });
        }

        private (IAlertPayloadEnricher enricher, IServiceScope scope) CreateEnricher()
        {
            var scope = _scopeFactory.CreateScope();
            var enricher = scope.ServiceProvider.GetRequiredService<IAlertPayloadEnricher>();
            return (enricher, scope);
        }

        private void LogOverlayAction(string userId, string action, string? alertType = null)
        {
            if (IsOverlayNotificationLoggingDisabled())
            {
                return;
            }

            var isHeartbeat = string.Equals(action, "streamStatusUpdate", StringComparison.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(alertType))
            {
                if (isHeartbeat)
                {
                    _logger.LogDebug(
                        "Overlay heartbeat: user_id={UserId}, action={Action}",
                        LogValue.Safe(userId),
                        LogValue.Safe(action));
                }
                else
                {
                    _logger.LogInformation(
                        "Overlay send: user_id={UserId}, action={Action}",
                        LogValue.Safe(userId),
                        LogValue.Safe(action));
                }
                return;
            }

            if (isHeartbeat)
            {
                _logger.LogDebug(
                    "Overlay heartbeat: user_id={UserId}, action={Action}, alert_type={AlertType}",
                    LogValue.Safe(userId),
                    LogValue.Safe(action),
                    LogValue.Safe(alertType));
            }
            else
            {
                _logger.LogInformation(
                    "Overlay send: user_id={UserId}, action={Action}, alert_type={AlertType}",
                    LogValue.Safe(userId),
                    LogValue.Safe(action),
                    LogValue.Safe(alertType));
            }
        }

        private static bool IsOverlayNotificationLoggingDisabled()
        {
            var raw = Environment.GetEnvironmentVariable("OMNIFORGE_DISABLE_OVERLAY_NOTIFICATION_LOGS");
            return string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsOverlayNotificationPayloadLoggingEnabled()
        {
            var raw = Environment.GetEnvironmentVariable("OMNIFORGE_LOG_OVERLAY_NOTIFICATION_PAYLOADS");
            return string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase);
        }
    }
}
