using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
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
            var payload = await EnrichPayloadAsync(userId, "follow", data);
            await _webSocketManager.SendToUserAsync(userId, "follow", payload);
        }

        public async Task NotifySubscriberAsync(string userId, string displayName, string tier, bool isGift)
        {
            LogOverlayAction(userId, "subscription");

            var data = new { name = displayName, displayName, tier, isGift, textPrompt = $"New Subscriber: {displayName}" };
            var payload = await EnrichPayloadAsync(userId, "subscription", data);
            await _webSocketManager.SendToUserAsync(userId, "subscription", payload);
        }

        public async Task NotifyResubAsync(string userId, string displayName, int months, string tier, string message)
        {
            LogOverlayAction(userId, "resub");

            var data = new { name = displayName, displayName, months, tier, message, textPrompt = $"{displayName} Resubscribed x{months}" };
            var payload = await EnrichPayloadAsync(userId, "resub", data);
            await _webSocketManager.SendToUserAsync(userId, "resub", payload);
        }

        public async Task NotifyGiftSubAsync(string userId, string gifterName, string recipientName, string tier, int totalGifts)
        {
            LogOverlayAction(userId, "giftsub");

            var data = new { name = gifterName, gifterName, recipientName, tier, totalGifts, textPrompt = $"{gifterName} Gifted {totalGifts} Subs" };
            var payload = await EnrichPayloadAsync(userId, "giftsub", data);
            await _webSocketManager.SendToUserAsync(userId, "giftsub", payload);
        }

        public async Task NotifyBitsAsync(string userId, string displayName, int amount, string message, int totalBits)
        {
            LogOverlayAction(userId, "bits");

            var data = new { name = displayName, displayName, amount, message, totalBits, textPrompt = $"{displayName} Cheered {amount} Bits" };
            var payload = await EnrichPayloadAsync(userId, "bits", data);
            await _webSocketManager.SendToUserAsync(userId, "bits", payload);
        }

        public async Task NotifyRaidAsync(string userId, string raiderName, int viewers)
        {
            LogOverlayAction(userId, "raid");

            var data = new { name = raiderName, raiderName, viewers, textPrompt = $"Raid: {raiderName} ({viewers})" };
            var payload = await EnrichPayloadAsync(userId, "raid", data);
            await _webSocketManager.SendToUserAsync(userId, "raid", payload);
        }

        public async Task NotifyCustomAlertAsync(string userId, string alertType, object data)
        {
            LogOverlayAction(userId, "customAlert", alertType);

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
                        "📦 Overlay payload: user_id={UserId}, alert_type={AlertType}, data={Data}",
                        LogSanitizer.Sanitize(userId),
                        LogSanitizer.Sanitize(alertType),
                        json);
                }
                catch
                {
                    // Ignore serialization issues for logging.
                }
            }

            var payload = await EnrichPayloadAsync(userId, alertType, data);
            await _webSocketManager.SendToUserAsync(userId, "customAlert", new { alertType, data = payload });
        }

        private void LogOverlayAction(string userId, string action, string? alertType = null)
        {
            if (IsOverlayNotificationLoggingDisabled())
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(alertType))
            {
                _logger.LogInformation(
                    "📣 Overlay send: user_id={UserId}, action={Action}",
                    LogSanitizer.Sanitize(userId),
                    LogSanitizer.Sanitize(action));
                return;
            }

            _logger.LogInformation(
                "📣 Overlay send: user_id={UserId}, action={Action}, alert_type={AlertType}",
                LogSanitizer.Sanitize(userId),
                LogSanitizer.Sanitize(action),
                LogSanitizer.Sanitize(alertType));
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

        private async Task<object> EnrichPayloadAsync(string userId, string alertType, object baseData)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var alertRepository = scope.ServiceProvider.GetService<IAlertRepository>();

                if (alertRepository == null) return baseData;

                var alerts = await alertRepository.GetAlertsAsync(userId);
                var anyMatching = alerts.Any(a => string.Equals(a.Type, alertType, StringComparison.OrdinalIgnoreCase));
                if (!anyMatching)
                {
                    return baseData;
                }

                var alert = alerts.FirstOrDefault(a => string.Equals(a.Type, alertType, StringComparison.OrdinalIgnoreCase) && a.IsEnabled);
                if (alert == null)
                {
                    // Matching alert exists but is disabled; return baseData but maybe we should suppress?
                    // For now, returning baseData mimics old behavior (passthrough if not found/disabled)
                    // But wait, if it's disabled in DB, we probably shouldn't show it at all?
                    // The old logic for CustomAlert was: if disabled, return.
                    // But for standard events (follow), we always want to show *something* (default behavior) unless explicitly disabled?
                    // If the user created a "follow" alert and disabled it, they probably want NO alert.
                    // But if they never created one, they want default.
                    // "anyMatching" check handles "never created one".
                    // If "anyMatching" is true, but "alert" is null (disabled), we should probably return null or a flag to suppress.
                    // However, changing return type to Task<object?> might break things.
                    // Let's stick to returning baseData for now to be safe, or maybe add a property "suppress": true?
                    return baseData;
                }

                var payload = new Dictionary<string, object>
                {
                    ["id"] = alert.Id,
                    ["type"] = alert.Type,
                    ["name"] = alert.Name,
                    ["visualCue"] = alert.VisualCue,
                    ["sound"] = alert.Sound,
                    ["soundDescription"] = alert.SoundDescription,
                    ["textPrompt"] = alert.TextPrompt,
                    ["duration"] = alert.Duration,
                    ["backgroundColor"] = alert.BackgroundColor,
                    ["textColor"] = alert.TextColor,
                    ["borderColor"] = alert.BorderColor
                };

                try
                {
                    if (!string.IsNullOrEmpty(alert.Effects))
                    {
                        var effectsObj = JsonSerializer.Deserialize<object>(alert.Effects);
                        if (effectsObj != null)
                        {
                            payload["effects"] = effectsObj;
                        }
                    }
                }
                catch (JsonException)
                {
                    // Effects field contains invalid JSON - skip silently as effects are optional
                }

                // Merge event data into payload (do not overwrite base alert fields).
                try
                {
                    var element = JsonSerializer.SerializeToElement(baseData);
                    if (element.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in element.EnumerateObject().Where(p => !payload.ContainsKey(p.Name)))
                        {
                            payload[prop.Name] = prop.Value;
                        }
                    }
                }
                catch (JsonException)
                {
                    // Event data serialization failed - continue with base payload
                }

                if (payload.TryGetValue("textPrompt", out var promptObj) && promptObj is string template && !string.IsNullOrWhiteSpace(template))
                {
                    payload["textPrompt"] = ApplyTemplate(template, payload);
                }

                return payload;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error enriching alert {AlertType} for user {UserId}; falling back to passthrough", alertType, userId);
                return baseData;
            }
        }

        private static string ApplyTemplate(string template, IReadOnlyDictionary<string, object> payload)
        {
            string GetString(string key)
            {
                if (!payload.TryGetValue(key, out var value) || value == null) return string.Empty;
                if (value is JsonElement je)
                {
                    if (je.ValueKind == JsonValueKind.String) return je.GetString() ?? string.Empty;
                    return je.ToString();
                }
                return value.ToString() ?? string.Empty;
            }

            string GetNumberString(string key)
            {
                if (!payload.TryGetValue(key, out var value) || value == null) return string.Empty;
                if (value is JsonElement je)
                {
                    if (je.ValueKind == JsonValueKind.Number) return je.ToString();
                    if (je.ValueKind == JsonValueKind.String) return je.GetString() ?? string.Empty;
                    return je.ToString();
                }
                return value.ToString() ?? string.Empty;
            }

            var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["User"] = FirstNonEmpty(GetString("user"), GetString("displayName"), GetString("name"), GetString("gifterName"), GetString("raiderName")),
                ["Tier"] = GetString("tier"),
                ["Months"] = GetNumberString("months"),
                ["Amount"] = FirstNonEmpty(GetNumberString("amount"), GetNumberString("bits")),
                ["Viewers"] = FirstNonEmpty(GetNumberString("viewers"), GetNumberString("viewerCount")),
                ["Recipient"] = FirstNonEmpty(GetString("recipientName"), GetString("recipient")),
                ["Message"] = GetString("message"),
                ["Level"] = GetNumberString("level"),
                ["Percent"] = GetNumberString("percent")
            };

            return Regex.Replace(template, "\\[(?<token>[A-Za-z]+)\\]", match =>
            {
                var token = match.Groups["token"].Value;
                if (tokens.TryGetValue(token, out var replacement) && !string.IsNullOrEmpty(replacement))
                {
                    return replacement;
                }
                return match.Value;
            });
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
        }

        public async Task NotifyTemplateChangedAsync(string userId, string templateStyle, Template template)
        {
            await _webSocketManager.SendToUserAsync(userId, "templateChanged", new { templateStyle, template });
        }
    }
}
