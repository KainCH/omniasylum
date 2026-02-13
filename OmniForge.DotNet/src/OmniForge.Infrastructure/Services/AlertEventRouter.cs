using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Constants;
using OmniForge.Core.Interfaces;

namespace OmniForge.Infrastructure.Services
{
    public class AlertEventRouter : IAlertEventRouter
    {
        private readonly IAlertRepository _alertRepository;
        private readonly IOverlayNotifier _overlayNotifier;
        private readonly ILogger<AlertEventRouter> _logger;

        // Cached set of core alert types to avoid repeated allocations on every RouteAsync call
        private static readonly HashSet<string> CoreAlertTypes = new(
            AlertTemplates.GetDefaultTemplates()
                .Select(t => t.Type)
                .Where(t => !string.IsNullOrWhiteSpace(t)),
            StringComparer.OrdinalIgnoreCase);

        public AlertEventRouter(
            IAlertRepository alertRepository,
            IOverlayNotifier overlayNotifier,
            ILogger<AlertEventRouter> logger)
        {
            _alertRepository = alertRepository;
            _overlayNotifier = overlayNotifier;
            _logger = logger;
        }

        public async Task RouteAsync(string userId, string eventKey, string defaultAlertType, object data)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return;
            }

            var mappedAlertType = defaultAlertType;
            var mappingFound = false;

            try
            {
                var mappings = await _alertRepository.GetEventMappingsAsync(userId);
                if (mappings.TryGetValue(eventKey, out var configuredType))
                {
                    mappingFound = true;
                    if (!string.IsNullOrWhiteSpace(configuredType))
                    {
                        mappedAlertType = configuredType;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Failed to load event mappings for {UserId}. Falling back to default alert type.", userId);
            }

            if (mappingFound && string.Equals(mappedAlertType, "none", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("⏭️ Alert suppressed by mapping. user={UserId}, event={EventKey}", userId, eventKey);
                return;
            }

            if (mappingFound && !string.IsNullOrWhiteSpace(mappedAlertType))
            {
                if (CoreAlertTypes.Contains(mappedAlertType))
                {
                    await SendStandardAlertAsync(userId, mappedAlertType, data);
                    return;
                }

                await _overlayNotifier.NotifyCustomAlertAsync(userId, mappedAlertType, data);
                return;
            }

            await SendStandardAlertAsync(userId, defaultAlertType, data);
        }

        private async Task SendStandardAlertAsync(string userId, string defaultAlertType, object data)
        {
            var payload = data is JsonElement jsonElement
                ? jsonElement
                : JsonSerializer.SerializeToElement(data);

            switch (defaultAlertType)
            {
                case "follow":
                    await _overlayNotifier.NotifyFollowerAsync(userId, GetString(payload, "displayName", "user", "name", fallback: "Someone"));
                    break;
                case "subscription":
                    await _overlayNotifier.NotifySubscriberAsync(
                        userId,
                        GetString(payload, "displayName", "user", "name", fallback: "Someone"),
                        GetString(payload, "tier", fallback: "Tier 1"),
                        GetBool(payload, "isGift"));
                    break;
                case "resub":
                    await _overlayNotifier.NotifyResubAsync(
                        userId,
                        GetString(payload, "displayName", "user", "name", fallback: "Someone"),
                        GetInt(payload, "months", fallback: 1),
                        GetString(payload, "tier", fallback: "Tier 1"),
                        GetString(payload, "message", fallback: string.Empty));
                    break;
                case "giftsub":
                    await _overlayNotifier.NotifyGiftSubAsync(
                        userId,
                        GetString(payload, "gifterName", "user", "name", fallback: "Someone"),
                        GetString(payload, "recipientName", fallback: "Someone"),
                        GetString(payload, "tier", fallback: "Tier 1"),
                        GetInt(payload, "totalGifts", fallback: 1));
                    break;
                case "bits":
                    await _overlayNotifier.NotifyBitsAsync(
                        userId,
                        GetString(payload, "displayName", "user", "name", fallback: "Someone"),
                        GetInt(payload, "amount", fallback: 0),
                        GetString(payload, "message", fallback: string.Empty),
                        GetInt(payload, "totalBits", fallback: 0));
                    break;
                case "raid":
                    await _overlayNotifier.NotifyRaidAsync(
                        userId,
                        GetString(payload, "raiderName", "user", "name", fallback: "Someone"),
                        GetInt(payload, "viewers", fallback: 0));
                    break;
                default:
                    await _overlayNotifier.NotifyCustomAlertAsync(userId, defaultAlertType, data);
                    break;
            }
        }

        private static string GetString(JsonElement payload, string primary, string? secondary = null, string? tertiary = null, string fallback = "")
        {
            if (TryGetString(payload, primary, out var value)) return value;
            if (!string.IsNullOrWhiteSpace(secondary) && TryGetString(payload, secondary, out value)) return value;
            if (!string.IsNullOrWhiteSpace(tertiary) && TryGetString(payload, tertiary, out value)) return value;
            return fallback;
        }

        private static bool TryGetString(JsonElement payload, string property, out string value)
        {
            value = string.Empty;
            if (!payload.TryGetProperty(property, out var prop)) return false;
            if (prop.ValueKind == JsonValueKind.String)
            {
                value = prop.GetString() ?? string.Empty;
                return true;
            }
            value = prop.ToString();
            return !string.IsNullOrWhiteSpace(value);
        }

        private static int GetInt(JsonElement payload, string property, int fallback = 0)
        {
            if (!payload.TryGetProperty(property, out var prop)) return fallback;
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var intValue)) return intValue;
            if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out intValue)) return intValue;
            return fallback;
        }

        private static bool GetBool(JsonElement payload, string property)
        {
            if (!payload.TryGetProperty(property, out var prop)) return false;
            if (prop.ValueKind == JsonValueKind.True) return true;
            if (prop.ValueKind == JsonValueKind.False) return false;
            if (prop.ValueKind == JsonValueKind.String && bool.TryParse(prop.GetString(), out var boolValue)) return boolValue;
            return false;
        }
    }
}
