using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Interfaces;

namespace OmniForge.Web.Services
{
    public class AlertPayloadEnricher : IAlertPayloadEnricher
    {
        private readonly IAlertRepository _alertRepository;
        private readonly ILogger<AlertPayloadEnricher> _logger;

        public AlertPayloadEnricher(IAlertRepository alertRepository, ILogger<AlertPayloadEnricher> logger)
        {
            _alertRepository = alertRepository;
            _logger = logger;
        }

        public async Task<object> EnrichPayloadAsync(string userId, string alertType, object baseData)
        {
            try
            {
                var alerts = await _alertRepository.GetAlertsAsync(userId);
                var anyMatching = alerts.Any(a => string.Equals(a.Type, alertType, StringComparison.OrdinalIgnoreCase));
                if (!anyMatching)
                {
                    return baseData;
                }

                var alert = alerts.FirstOrDefault(a => string.Equals(a.Type, alertType, StringComparison.OrdinalIgnoreCase) && a.IsEnabled);
                if (alert == null)
                {
                    return new Dictionary<string, object> { ["suppress"] = true };
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
                _logger.LogError(ex, "Error enriching alert {AlertType} for user {UserId}; falling back to passthrough", alertType, userId);
                return baseData;
            }
        }

        public bool IsSuppressed(object payload)
        {
            return payload is Dictionary<string, object> dict
                && dict.TryGetValue("suppress", out var val)
                && val is true;
        }

        public static string ApplyTemplate(string template, IReadOnlyDictionary<string, object> payload)
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
    }
}
