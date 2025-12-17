using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniForge.Infrastructure.Interfaces;

namespace OmniForge.Infrastructure.Services.EventHandlers
{
    /// <summary>
    /// Base class for EventSub event handlers providing common functionality.
    /// </summary>
    public abstract class BaseEventSubHandler : IEventSubHandler
    {
        protected readonly IServiceScopeFactory ScopeFactory;
        protected readonly ILogger Logger;

        protected BaseEventSubHandler(IServiceScopeFactory scopeFactory, ILogger logger)
        {
            ScopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// The EventSub subscription type this handler processes.
        /// </summary>
        public abstract string SubscriptionType { get; }

        /// <summary>
        /// Handles the EventSub event data.
        /// </summary>
        public abstract Task HandleAsync(JsonElement eventData);

        /// <summary>
        /// Attempts to extract the broadcaster user ID from the event data.
        /// </summary>
        protected bool TryGetBroadcasterId(JsonElement eventData, out string? broadcasterId)
        {
            broadcasterId = null;
            eventData = UnwrapEvent(eventData);
            if (eventData.TryGetProperty("broadcaster_user_id", out var idProp))
            {
                broadcasterId = idProp.GetString();
            }
            return broadcasterId != null;
        }

        /// <summary>
        /// EventSub notifications may arrive as the full envelope ({ subscription, event }).
        /// Handlers typically care about the inner "event" object.
        /// </summary>
        protected static JsonElement UnwrapEvent(JsonElement eventData)
        {
            if (eventData.ValueKind == JsonValueKind.Object &&
                eventData.TryGetProperty("event", out var evt) &&
                evt.ValueKind == JsonValueKind.Object)
            {
                return evt;
            }

            return eventData;
        }

        /// <summary>
        /// Converts subscription tier code to human-readable format.
        /// </summary>
        protected static string GetReadableTier(string tier)
        {
            return tier switch
            {
                "1000" => "Tier 1",
                "2000" => "Tier 2",
                "3000" => "Tier 3",
                "Prime" => "Prime",
                _ => tier
            };
        }

        /// <summary>
        /// Safely gets a string property from JSON, returning default if not found.
        /// </summary>
        protected static string GetStringProperty(JsonElement element, string propertyName, string defaultValue = "")
        {
            if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString() ?? defaultValue;
            }
            return defaultValue;
        }

        /// <summary>
        /// Safely gets an integer property from JSON, returning default if not found.
        /// </summary>
        protected static int GetIntProperty(JsonElement element, string propertyName, int defaultValue = 0)
        {
            if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number)
            {
                return prop.GetInt32();
            }
            return defaultValue;
        }

        /// <summary>
        /// Safely gets a boolean property from JSON, returning default if not found.
        /// </summary>
        protected static bool GetBoolProperty(JsonElement element, string propertyName, bool defaultValue = false)
        {
            if (element.TryGetProperty(propertyName, out var prop) &&
                (prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False))
            {
                return prop.GetBoolean();
            }
            return defaultValue;
        }
    }
}
