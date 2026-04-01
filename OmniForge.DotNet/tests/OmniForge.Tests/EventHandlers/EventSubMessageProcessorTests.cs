using System;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using OmniForge.Infrastructure.Services;
using OmniForge.Core.Utilities;
using Xunit;

namespace OmniForge.Tests.EventHandlers
{
    public class EventSubMessageProcessorTests
    {
        private readonly Mock<ILogger<EventSubMessageProcessor>> _mockLogger;
        private readonly EventSubMessageProcessor _processor;

        public EventSubMessageProcessorTests()
        {
            _mockLogger = new Mock<ILogger<EventSubMessageProcessor>>();
            _processor = new EventSubMessageProcessor(_mockLogger.Object, new LogValueSanitizer());
        }

        [Fact]
        public void Process_SessionWelcome_ShouldReturnSessionIdAndType()
        {
            // Arrange
            var json = @"{
                ""metadata"": {
                    ""message_id"": ""test-id"",
                    ""message_type"": ""session_welcome"",
                    ""message_timestamp"": ""2024-01-01T00:00:00Z""
                },
                ""payload"": {
                    ""session"": {
                        ""id"": ""session-123"",
                        ""keepalive_timeout_seconds"": 10
                    }
                }
            }";

            // Act
            var result = _processor.Process(json);

            // Assert
            Assert.Equal(EventSubMessageType.SessionWelcome, result.MessageType);
            Assert.Equal("session-123", result.SessionId);
            Assert.NotNull(result.Message);
        }

        [Fact]
        public void Process_SessionKeepalive_ShouldReturnKeepaliveType()
        {
            // Arrange
            var json = @"{
                ""metadata"": {
                    ""message_id"": ""test-id"",
                    ""message_type"": ""session_keepalive"",
                    ""message_timestamp"": ""2024-01-01T00:00:00Z""
                },
                ""payload"": {}
            }";

            // Act
            var result = _processor.Process(json);

            // Assert
            Assert.Equal(EventSubMessageType.SessionKeepalive, result.MessageType);
        }

        [Fact]
        public void Process_Notification_ShouldReturnNotificationTypeAndMessage()
        {
            // Arrange
            var json = @"{
                ""metadata"": {
                    ""message_id"": ""notification-123"",
                    ""message_type"": ""notification"",
                    ""message_timestamp"": ""2024-01-01T00:00:00Z""
                },
                ""payload"": {
                    ""subscription"": {
                        ""id"": ""sub-123"",
                        ""type"": ""stream.online""
                    },
                    ""event"": {
                        ""broadcaster_user_id"": ""123""
                    }
                }
            }";

            // Act
            var result = _processor.Process(json);

            // Assert
            Assert.Equal(EventSubMessageType.Notification, result.MessageType);
            Assert.NotNull(result.Message);
        }

        [Fact]
        public void Process_ChatNotification_ShouldLogDebug()
        {
            // Arrange
            var json = @"{
                ""metadata"": {
                    ""message_id"": ""notification-123"",
                    ""message_type"": ""notification"",
                    ""message_timestamp"": ""2024-01-01T00:00:00Z""
                },
                ""payload"": {
                    ""subscription"": {
                        ""id"": ""sub-123"",
                        ""type"": ""channel.chat.message""
                    },
                    ""event"": {
                        ""broadcaster_user_id"": ""123""
                    }
                }
            }";

            // Act
            var result = _processor.Process(json);

            // Assert
            Assert.Equal(EventSubMessageType.Notification, result.MessageType);
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Debug,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce);
        }

        [Fact]
        public void Process_Notification_WithWrappedEvent_ShouldIncludeBroadcasterIdInLog()
        {
            // Arrange
            var json = @"{
                ""metadata"": {
                    ""message_id"": ""notification-123"",
                    ""message_type"": ""notification"",
                    ""message_timestamp"": ""2024-01-01T00:00:00Z""
                },
                ""payload"": {
                    ""subscription"": {
                        ""id"": ""sub-123"",
                        ""type"": ""stream.online""
                    },
                    ""event"": {
                        ""event"": {
                            ""broadcaster_user_id"": ""123""
                        }
                    }
                }
            }";

            // Act
            var result = _processor.Process(json);

            // Assert
            Assert.Equal(EventSubMessageType.Notification, result.MessageType);
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("broadcaster_user_id=123")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce);
        }

        [Fact]
        public void Process_Reconnect_ShouldReturnReconnectTypeAndUrl()
        {
            // Arrange
            var json = @"{
                ""metadata"": {
                    ""message_id"": ""test-id"",
                    ""message_type"": ""reconnect"",
                    ""message_timestamp"": ""2024-01-01T00:00:00Z""
                },
                ""payload"": {
                    ""session"": {
                        ""id"": ""session-123"",
                        ""reconnect_url"": ""wss://eventsub.wss.twitch.tv/ws?reconnect=true""
                    }
                }
            }";

            // Act
            var result = _processor.Process(json);

            // Assert
            Assert.Equal(EventSubMessageType.Reconnect, result.MessageType);
            Assert.Equal("wss://eventsub.wss.twitch.tv/ws?reconnect=true", result.ReconnectUrl);
            Assert.True(result.RequiresDisconnect);
        }

        [Fact]
        public void Process_Revocation_ShouldReturnRevocationType()
        {
            // Arrange
            var json = @"{
                ""metadata"": {
                    ""message_id"": ""test-id"",
                    ""message_type"": ""revocation"",
                    ""message_timestamp"": ""2024-01-01T00:00:00Z""
                },
                ""payload"": {
                    ""subscription"": {
                        ""id"": ""sub-123"",
                        ""status"": ""authorization_revoked""
                    }
                }
            }";

            // Act
            var result = _processor.Process(json);

            // Assert
            Assert.Equal(EventSubMessageType.Revocation, result.MessageType);
        }

        [Fact]
        public void Process_UnknownMessageType_ShouldReturnUnknown()
        {
            // Arrange
            var json = @"{
                ""metadata"": {
                    ""message_id"": ""test-id"",
                    ""message_type"": ""some_future_type"",
                    ""message_timestamp"": ""2024-01-01T00:00:00Z""
                },
                ""payload"": {}
            }";

            // Act
            var result = _processor.Process(json);

            // Assert
            Assert.Equal(EventSubMessageType.Unknown, result.MessageType);
        }

        [Fact]
        public void Process_InvalidJson_ShouldReturnUnknown()
        {
            // Arrange
            var json = "{ this is not valid json }";

            // Act
            var result = _processor.Process(json);

            // Assert
            Assert.Equal(EventSubMessageType.Unknown, result.MessageType);
        }

        [Fact]
        public void Process_EmptyJson_ShouldReturnUnknown()
        {
            // Arrange
            var json = "{}";

            // Act
            var result = _processor.Process(json);

            // Assert
            Assert.Equal(EventSubMessageType.Unknown, result.MessageType);
        }

        [Fact]
        public void Process_NullMetadata_ShouldReturnUnknown()
        {
            // Arrange
            var json = @"{
                ""payload"": {}
            }";

            // Act
            var result = _processor.Process(json);

            // Assert
            Assert.Equal(EventSubMessageType.Unknown, result.MessageType);
        }

        [Fact]
        public void Process_SessionWelcomeWithoutSession_ShouldReturnNullSessionId()
        {
            // Arrange
            var json = @"{
                ""metadata"": {
                    ""message_id"": ""test-id"",
                    ""message_type"": ""session_welcome"",
                    ""message_timestamp"": ""2024-01-01T00:00:00Z""
                },
                ""payload"": {}
            }";

            // Act
            var result = _processor.Process(json);

            // Assert
            Assert.Equal(EventSubMessageType.SessionWelcome, result.MessageType);
            Assert.Null(result.SessionId);
        }

        [Fact]
        public void Process_ReconnectWithoutUrl_ShouldStillRequireDisconnect()
        {
            // Arrange
            var json = @"{
                ""metadata"": {
                    ""message_id"": ""test-id"",
                    ""message_type"": ""reconnect"",
                    ""message_timestamp"": ""2024-01-01T00:00:00Z""
                },
                ""payload"": {}
            }";

            // Act
            var result = _processor.Process(json);

            // Assert
            Assert.Equal(EventSubMessageType.Reconnect, result.MessageType);
            Assert.Null(result.ReconnectUrl);
            Assert.True(result.RequiresDisconnect);
        }

        [Fact]
        public void Process_NullJson_ShouldReturnUnknown()
        {
            // "null" deserializes to null EventSubMessage — exercises the null-check branch
            var result = _processor.Process("null");
            Assert.Equal(EventSubMessageType.Unknown, result.MessageType);
        }

        [Fact]
        public void Process_Notification_NoBroadcasterUserId_TryGetBroadcasterIdReturnsNull()
        {
            // Event payload exists but has no broadcaster_user_id — TryGetBroadcasterId returns null
            var json = @"{
                ""metadata"": {
                    ""message_id"": ""notification-456"",
                    ""message_type"": ""notification"",
                    ""message_timestamp"": ""2024-01-01T00:00:00Z""
                },
                ""payload"": {
                    ""subscription"": {
                        ""id"": ""sub-456"",
                        ""type"": ""stream.online""
                    },
                    ""event"": {
                        ""some_other_field"": ""value""
                    }
                }
            }";

            var result = _processor.Process(json);

            Assert.Equal(EventSubMessageType.Notification, result.MessageType);
        }
    }
}
