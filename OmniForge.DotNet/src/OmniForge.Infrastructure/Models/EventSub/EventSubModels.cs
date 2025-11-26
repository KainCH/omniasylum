using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OmniForge.Infrastructure.Models.EventSub
{
    public class EventSubMessage
    {
        [JsonPropertyName("metadata")]
        public EventSubMetadata Metadata { get; set; } = new();
        [JsonPropertyName("payload")]
        public EventSubPayload Payload { get; set; } = new();
    }

    public class EventSubMetadata
    {
        [JsonPropertyName("message_id")]
        public string MessageId { get; set; } = string.Empty;
        [JsonPropertyName("message_type")]
        public string MessageType { get; set; } = string.Empty;
        [JsonPropertyName("message_timestamp")]
        public DateTime MessageTimestamp { get; set; }
    }

    public class EventSubPayload
    {
        [JsonPropertyName("session")]
        public EventSubSession? Session { get; set; }
        [JsonPropertyName("subscription")]
        public EventSubSubscription? Subscription { get; set; }
        [JsonPropertyName("event")]
        public JsonElement Event { get; set; }
    }

    public class EventSubSession
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;
        [JsonPropertyName("keepalive_timeout_seconds")]
        public int? KeepaliveTimeoutSeconds { get; set; }
        [JsonPropertyName("reconnect_url")]
        public string? ReconnectUrl { get; set; }
        [JsonPropertyName("connected_at")]
        public DateTime ConnectedAt { get; set; }
    }

    public class EventSubSubscription
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;
        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;
        [JsonPropertyName("condition")]
        public Dictionary<string, string> Condition { get; set; } = new();
        [JsonPropertyName("transport")]
        public EventSubTransport Transport { get; set; } = new();
        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }
    }

    public class EventSubTransport
    {
        [JsonPropertyName("method")]
        public string Method { get; set; } = string.Empty;
        [JsonPropertyName("session_id")]
        public string SessionId { get; set; } = string.Empty;
    }
}
