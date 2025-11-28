using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniForge.Infrastructure.Models.EventSub;

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
                        _logger.LogInformation("Notification received: {MessageId}", message.Metadata.MessageId);
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
    }
}
