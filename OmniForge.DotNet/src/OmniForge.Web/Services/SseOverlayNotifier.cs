using System;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Utilities;

namespace OmniForge.Web.Services
{
    public class SseOverlayNotifier : IOverlayNotifier
    {
        private readonly SseConnectionManager _sseManager;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SseOverlayNotifier> _logger;

        public SseOverlayNotifier(
            SseConnectionManager sseManager,
            IServiceScopeFactory scopeFactory,
            ILogger<SseOverlayNotifier> logger)
        {
            _sseManager = sseManager;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task NotifyCounterUpdateAsync(string userId, Counter counter)
        {
            LogOverlayAction(userId, "counter");

            var c = counter ?? new Counter { TwitchUserId = userId };
            await _sseManager.SendEventAsync(userId, "counter", new
            {
                c.Deaths,
                c.Swears,
                c.Screams,
                c.Bits,
                c.CustomCounters
            });
        }

        public async Task NotifyMilestoneReachedAsync(string userId, string counterType, int milestone, int newValue, int previousMilestone)
        {
            LogOverlayAction(userId, "alert");

            using var scope = _scopeFactory.CreateScope();
            var enricher = scope.ServiceProvider.GetRequiredService<IAlertPayloadEnricher>();

            var data = new { counterType, milestone, newValue, previousMilestone, textPrompt = $"Milestone: {milestone} {counterType}!" };
            var payload = await enricher.EnrichPayloadAsync(userId, "milestone", data);
            if (enricher.IsSuppressed(payload)) return;

            await _sseManager.SendEventAsync(userId, "alert", payload);
        }

        public async Task NotifySettingsUpdateAsync(string userId, OverlaySettings settings)
        {
            LogOverlayAction(userId, "config");

            await _sseManager.SendEventAsync(userId, "config", settings);
        }

        public async Task NotifyStreamStatusUpdateAsync(string userId, string status)
        {
            LogOverlayAction(userId, "stream");

            await _sseManager.SendEventAsync(userId, "stream", new { status, streamStarted = (DateTimeOffset?)null });
        }

        public async Task NotifyStreamStartedAsync(string userId, Counter counter)
        {
            LogOverlayAction(userId, "stream");

            await _sseManager.SendEventAsync(userId, "stream", new
            {
                status = "live",
                streamStarted = counter?.StreamStarted
            });
        }

        public async Task NotifyStreamEndedAsync(string userId, Counter counter)
        {
            LogOverlayAction(userId, "stream");

            await _sseManager.SendEventAsync(userId, "stream", new
            {
                status = "offline",
                streamStarted = (DateTimeOffset?)null
            });
        }

        public async Task NotifyFollowerAsync(string userId, string displayName)
        {
            LogOverlayAction(userId, "alert");

            var data = new { name = displayName, displayName, textPrompt = $"New Follower: {displayName}" };
            await SendEnrichedAlertAsync(userId, "follow", data);
        }

        public async Task NotifySubscriberAsync(string userId, string displayName, string tier, bool isGift)
        {
            LogOverlayAction(userId, "alert");

            var data = new { name = displayName, displayName, tier, isGift, textPrompt = $"New Subscriber: {displayName}" };
            await SendEnrichedAlertAsync(userId, "subscription", data);
        }

        public async Task NotifyResubAsync(string userId, string displayName, int months, string tier, string message)
        {
            LogOverlayAction(userId, "alert");

            var data = new { name = displayName, displayName, months, tier, textPrompt = $"{displayName} Resubscribed x{months}" };
            await SendEnrichedAlertAsync(userId, "resub", data);
        }

        public async Task NotifyGiftSubAsync(string userId, string gifterName, string recipientName, string tier, int totalGifts)
        {
            LogOverlayAction(userId, "alert");

            var data = new { name = gifterName, gifterName, recipientName, tier, totalGifts, textPrompt = $"{gifterName} Gifted {totalGifts} Subs" };
            await SendEnrichedAlertAsync(userId, "giftsub", data);
        }

        public async Task NotifyBitsAsync(string userId, string displayName, int amount, string message, int totalBits)
        {
            LogOverlayAction(userId, "alert");

            var data = new { name = displayName, displayName, amount, totalBits, textPrompt = $"{displayName} Cheered {amount} Bits" };
            await SendEnrichedAlertAsync(userId, "bits", data);
        }

        public async Task NotifyRaidAsync(string userId, string raiderName, int viewers)
        {
            LogOverlayAction(userId, "alert");

            var data = new { name = raiderName, raiderName, viewers, textPrompt = $"Raid: {raiderName} ({viewers})" };
            await SendEnrichedAlertAsync(userId, "raid", data);
        }

        public async Task NotifyCustomAlertAsync(string userId, string alertType, object data)
        {
            var safeUserId = userId ?? string.Empty;
            var safeAlertType = alertType ?? string.Empty;

            LogOverlayAction(safeUserId, "alert", safeAlertType);

            using var scope = _scopeFactory.CreateScope();
            var enricher = scope.ServiceProvider.GetRequiredService<IAlertPayloadEnricher>();

            var payload = await enricher.EnrichPayloadAsync(safeUserId, safeAlertType, data);
            // For SSE v2, custom alerts are sent as "alert" events with the alertType embedded
            await _sseManager.SendEventAsync(safeUserId, "alert", new { alertType = safeAlertType, data = payload });
        }

        public async Task NotifyTemplateChangedAsync(string userId, string templateStyle, Template template)
        {
            LogOverlayAction(userId, "template");

            await _sseManager.SendEventAsync(userId, "template", new { templateStyle, template });
        }

        public async Task NotifySceneChangedAsync(string userId, string sceneName)
        {
            LogOverlayAction(userId, "scene");

            await _sseManager.SendEventAsync(userId, "scene", new { sceneName });
        }

        private async Task SendEnrichedAlertAsync(string userId, string alertType, object data)
        {
            using var scope = _scopeFactory.CreateScope();
            var enricher = scope.ServiceProvider.GetRequiredService<IAlertPayloadEnricher>();

            var payload = await enricher.EnrichPayloadAsync(userId, alertType, data);
            if (enricher.IsSuppressed(payload)) return;

            await _sseManager.SendEventAsync(userId, "alert", payload);
        }

        private void LogOverlayAction(string userId, string eventType, string? alertType = null)
        {
            if (IsOverlayNotificationLoggingDisabled()) return;

            var isHeartbeat = string.Equals(eventType, "stream", StringComparison.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(alertType))
            {
                if (isHeartbeat)
                {
                    _logger.LogDebug("SSE overlay: user_id={UserId}, event={EventType}",
                        LogValue.Safe(userId), LogValue.Safe(eventType));
                }
                else
                {
                    _logger.LogInformation("SSE overlay: user_id={UserId}, event={EventType}",
                        LogValue.Safe(userId), LogValue.Safe(eventType));
                }
            }
            else
            {
                _logger.LogInformation("SSE overlay: user_id={UserId}, event={EventType}, alert_type={AlertType}",
                    LogValue.Safe(userId), LogValue.Safe(eventType), LogValue.Safe(alertType));
            }
        }

        private static bool IsOverlayNotificationLoggingDisabled()
        {
            var raw = Environment.GetEnvironmentVariable("OMNIFORGE_DISABLE_OVERLAY_NOTIFICATION_LOGS");
            return string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase);
        }
    }
}
