using System;
using System.Text.Json;
using OmniForge.Infrastructure.Models.EventSub;
using Xunit;

namespace OmniForge.Tests.Models
{
    public class EventSubModelsTests
    {
        [Fact]
        public void EventSubSession_ShouldDeserialize_FromJson()
        {
            var json = """
            {
              "metadata": {
                "message_id": "m1",
                "message_type": "session_welcome",
                "message_timestamp": "2026-01-24T00:00:00Z"
              },
              "payload": {
                "session": {
                  "id": "sess1",
                  "status": "connected",
                  "keepalive_timeout_seconds": 10,
                  "reconnect_url": "https://example/reconnect",
                  "connected_at": "2026-01-24T00:00:00Z"
                }
              }
            }
            """;

            var message = JsonSerializer.Deserialize<EventSubMessage>(json);

            Assert.NotNull(message);
            Assert.Equal("m1", message!.Metadata.MessageId);
            Assert.Equal("session_welcome", message.Metadata.MessageType);
            Assert.NotNull(message.Payload.Session);
            Assert.Equal("sess1", message.Payload.Session!.Id);
            Assert.Equal("connected", message.Payload.Session.Status);
            Assert.Equal(10, message.Payload.Session.KeepaliveTimeoutSeconds);
            Assert.Equal("https://example/reconnect", message.Payload.Session.ReconnectUrl);
            Assert.Equal(DateTime.Parse("2026-01-24T00:00:00Z").ToUniversalTime(), message.Payload.Session.ConnectedAt.ToUniversalTime());
        }
    }
}
