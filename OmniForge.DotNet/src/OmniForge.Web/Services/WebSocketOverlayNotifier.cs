using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;

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
            await _webSocketManager.SendToUserAsync(userId, "counterUpdate", counter);
        }

        public async Task NotifyMilestoneReachedAsync(string userId, string counterType, int milestone, int newValue, int previousMilestone)
        {
            await _webSocketManager.SendToUserAsync(userId, "milestoneReached", new { counterType, milestone, newValue, previousMilestone });
        }

        public async Task NotifySettingsUpdateAsync(string userId, OverlaySettings settings)
        {
            await _webSocketManager.SendToUserAsync(userId, "settingsUpdate", settings);
        }

        public async Task NotifyStreamStatusUpdateAsync(string userId, string status)
        {
            await _webSocketManager.SendToUserAsync(userId, "streamStatusUpdate", new { streamStatus = status });
        }

        public async Task NotifyStreamStartedAsync(string userId, Counter counter)
        {
            await _webSocketManager.SendToUserAsync(userId, "streamStarted", counter);
        }

        public async Task NotifyStreamEndedAsync(string userId, Counter counter)
        {
            await _webSocketManager.SendToUserAsync(userId, "streamEnded", counter);
        }

        public async Task NotifyFollowerAsync(string userId, string displayName)
        {
            await _webSocketManager.SendToUserAsync(userId, "follow", new { name = displayName, displayName, textPrompt = $"New Follower: {displayName}" });
        }

        public async Task NotifySubscriberAsync(string userId, string displayName, string tier, bool isGift)
        {
            await _webSocketManager.SendToUserAsync(userId, "subscription", new { name = displayName, displayName, tier, isGift, textPrompt = $"New Subscriber: {displayName}" });
        }

        public async Task NotifyResubAsync(string userId, string displayName, int months, string tier, string message)
        {
            await _webSocketManager.SendToUserAsync(userId, "resub", new { name = displayName, displayName, months, tier, message, textPrompt = $"{displayName} Resubscribed x{months}" });
        }

        public async Task NotifyGiftSubAsync(string userId, string gifterName, string recipientName, string tier, int totalGifts)
        {
            await _webSocketManager.SendToUserAsync(userId, "giftsub", new { name = gifterName, gifterName, recipientName, tier, totalGifts, textPrompt = $"{gifterName} Gifted {totalGifts} Subs" });
        }

        public async Task NotifyBitsAsync(string userId, string displayName, int amount, string message, int totalBits)
        {
            await _webSocketManager.SendToUserAsync(userId, "bits", new { name = displayName, displayName, amount, message, totalBits, textPrompt = $"{displayName} Cheered {amount} Bits" });
        }

        public async Task NotifyRaidAsync(string userId, string raiderName, int viewers)
        {
            await _webSocketManager.SendToUserAsync(userId, "raid", new { name = raiderName, raiderName, viewers, textPrompt = $"Raid: {raiderName} ({viewers})" });
        }

        public async Task NotifyCustomAlertAsync(string userId, string alertType, object data)
        {
            // Static overlay (wwwroot/overlay.html) consumes "customAlert" messages and expects a payload
            // similar to the Blazor overlay: template fields merged with event data, with placeholders resolved.
            // For non-template custom events (e.g. chatCommandsUpdated), fall back to passthrough.
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var alertRepository = scope.ServiceProvider.GetService<IAlertRepository>();

                if (alertRepository == null)
                {
                    await _webSocketManager.SendToUserAsync(userId, "customAlert", new { alertType, data });
                    return;
                }

                var alerts = await alertRepository.GetAlertsAsync(userId);
                var anyMatching = alerts.Any(a => string.Equals(a.Type, alertType, StringComparison.OrdinalIgnoreCase));
                if (!anyMatching)
                {
                    await _webSocketManager.SendToUserAsync(userId, "customAlert", new { alertType, data });
                    return;
                }

                var alert = alerts.FirstOrDefault(a => string.Equals(a.Type, alertType, StringComparison.OrdinalIgnoreCase) && a.IsEnabled);
                if (alert == null)
                {
                    // Matching alert exists but is disabled; suppress.
                    return;
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
                catch { }

                // Merge event data into payload (do not overwrite base alert fields).
                try
                {
                    var element = JsonSerializer.SerializeToElement(data);
                    if (element.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in element.EnumerateObject())
                        {
                            if (!payload.ContainsKey(prop.Name))
                            {
                                payload[prop.Name] = prop.Value;
                            }
                        }
                    }
                }
                catch { }

                if (payload.TryGetValue("textPrompt", out var promptObj) && promptObj is string template && !string.IsNullOrWhiteSpace(template))
                {
                    payload["textPrompt"] = ApplyTemplate(template, payload);
                }

                await _webSocketManager.SendToUserAsync(userId, "customAlert", new { alertType, data = payload });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error hydrating custom alert {AlertType} for user {UserId}; falling back to passthrough", alertType, userId);
                await _webSocketManager.SendToUserAsync(userId, "customAlert", new { alertType, data });
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
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
            return string.Empty;
        }

        public async Task NotifyTemplateChangedAsync(string userId, string templateStyle, Template template)
        {
            await _webSocketManager.SendToUserAsync(userId, "templateChanged", new { templateStyle, template });
        }
    }
}
