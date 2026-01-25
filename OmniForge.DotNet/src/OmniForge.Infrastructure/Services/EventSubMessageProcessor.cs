using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniForge.Infrastructure.Models.EventSub;
using OmniForge.Core.Utilities;

namespace OmniForge.Infrastructure.Services
{
    /// <summary>
    /// Result of processing an EventSub message.
    /// </summary>
    public class EventSubProcessResult
    {
        public EventSubMessageType MessageType { get; set; }
        public string? SessionId { get; set; }
        public string? ReconnectUrl { get; set; }
        public EventSubMessage? Message { get; set; }
        public bool RequiresDisconnect { get; set; }
    }

    public enum EventSubMessageType
    {
        Unknown,
        SessionWelcome,
        SessionKeepalive,
        Notification,
        Reconnect,
        Revocation
    }

    /// <summary>
    /// Interface for processing EventSub WebSocket messages.
    /// </summary>
    public interface IEventSubMessageProcessor
    {
        EventSubProcessResult Process(string json);
    }

    /// <summary>
    /// Processes EventSub WebSocket messages and extracts relevant information.
    /// This class is separated from the WebSocket handling to enable unit testing.
    /// </summary>
    public class EventSubMessageProcessor : IEventSubMessageProcessor
    {
        private readonly ILogger<EventSubMessageProcessor> _logger;

        public EventSubMessageProcessor(ILogger<EventSubMessageProcessor> logger)
        {
            _logger = logger;
        }

        public EventSubProcessResult Process(string json)
        {
            var result = new EventSubProcessResult();

            try
            {
                var message = JsonSerializer.Deserialize<EventSubMessage>(json);
                if (message == null)
                {
                    result.MessageType = EventSubMessageType.Unknown;
                    return result;
                }

                result.Message = message;

                switch (message.Metadata.MessageType)
                {
                    case "session_welcome":
                        result.MessageType = EventSubMessageType.SessionWelcome;
                        result.SessionId = message.Payload.Session?.Id;
                        _logger.LogInformation("Session Welcome! ID: {SessionId}, Keepalive: {Keepalive}s",
                            result.SessionId, message.Payload.Session?.KeepaliveTimeoutSeconds);
                        break;

                    case "session_keepalive":
                        result.MessageType = EventSubMessageType.SessionKeepalive;
                        break;

                    case "notification":
                        result.MessageType = EventSubMessageType.Notification;

                        var subscriptionType = message.Payload.Subscription?.Type;
                        var subscriptionId = message.Payload.Subscription?.Id;
                        var broadcasterId = TryGetBroadcasterId(message.Payload.Event);

                        // Chat events can be extremely noisy; keep those at Debug.
                        var isChatEvent = !string.IsNullOrWhiteSpace(subscriptionType)
                            && subscriptionType.StartsWith("channel.chat", StringComparison.OrdinalIgnoreCase);

                        if (isChatEvent)
                        {
                            _logger.LogDebug(
                                "💬 EventSub notification received: message_id={MessageId}, type={Type}, subscription_id={SubscriptionId}, broadcaster_user_id={BroadcasterId}",
                                LogSanitizer.Sanitize(message.Metadata.MessageId),
                                LogSanitizer.Sanitize(subscriptionType ?? string.Empty),
                                LogSanitizer.Sanitize(subscriptionId ?? string.Empty),
                                LogSanitizer.Sanitize(broadcasterId ?? string.Empty));
                        }
                        else
                        {
                            _logger.LogInformation(
                                "📨 EventSub notification received: message_id={MessageId}, type={Type}, subscription_id={SubscriptionId}, broadcaster_user_id={BroadcasterId}",
                                LogSanitizer.Sanitize(message.Metadata.MessageId),
                                LogSanitizer.Sanitize(subscriptionType ?? string.Empty),
                                LogSanitizer.Sanitize(subscriptionId ?? string.Empty),
                                LogSanitizer.Sanitize(broadcasterId ?? string.Empty));
                        }
                        break;

                    case "reconnect":
                        result.MessageType = EventSubMessageType.Reconnect;
                        result.ReconnectUrl = message.Payload.Session?.ReconnectUrl;
                        result.RequiresDisconnect = true;
                        _logger.LogWarning("Reconnect requested by server. URL: {ReconnectUrl}", result.ReconnectUrl);
                        break;

                    case "revocation":
                        result.MessageType = EventSubMessageType.Revocation;
                        _logger.LogWarning("Subscription revoked: {SubscriptionId} - {Status}",
                            message.Payload.Subscription?.Id, message.Payload.Subscription?.Status);
                        break;

                    default:
                        result.MessageType = EventSubMessageType.Unknown;
                        _logger.LogWarning("Unknown message type: {MessageType}", message.Metadata.MessageType);
                        break;
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Error deserializing EventSub message.");
                result.MessageType = EventSubMessageType.Unknown;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing EventSub message.");
                result.MessageType = EventSubMessageType.Unknown;
            }

            return result;
        }

        private static string? TryGetBroadcasterId(JsonElement eventData)
        {
            try
            {
                // Notifications may be unwrapped (event object) or wrapped ({ subscription, event }).
                if (eventData.ValueKind == JsonValueKind.Object
                    && eventData.TryGetProperty("event", out var inner)
                    && inner.ValueKind == JsonValueKind.Object)
                {
                    eventData = inner;
                }

                if (eventData.ValueKind == JsonValueKind.Object
                    && eventData.TryGetProperty("broadcaster_user_id", out var idProp)
                    && idProp.ValueKind == JsonValueKind.String)
                {
                    return idProp.GetString();
                }
            }
            catch
            {
                // best-effort
            }

            return null;
        }
    }
}
