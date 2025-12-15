using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using OmniForge.Infrastructure.Interfaces;
using OmniForge.Core.Entities;
using OmniForge.Infrastructure.Configuration;
using OmniForge.Infrastructure.Services;
using Xunit;

namespace OmniForge.Tests
{
    public class DiscordServiceTests
    {
        private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;
        private readonly Mock<ILogger<DiscordService>> _mockLogger;
        private readonly Mock<IDiscordBotClient> _mockDiscordBotClient;
        private readonly DiscordService _service;
        private readonly HttpClient _httpClient;

        public DiscordServiceTests()
        {
            _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
            _mockLogger = new Mock<ILogger<DiscordService>>();
            _mockDiscordBotClient = new Mock<IDiscordBotClient>();

            _httpClient = new HttpClient(_mockHttpMessageHandler.Object);
            var botSettings = Options.Create(new DiscordBotSettings
            {
                BotToken = "test-bot-token",
                ApiBaseUrl = "https://discord.com/api/v10"
            });
            _service = new DiscordService(_httpClient, _mockLogger.Object, botSettings, _mockDiscordBotClient.Object);
        }

        [Fact]
        public async Task SendTestNotificationAsync_ShouldSendBotMessage_WhenChannelIdConfigured()
        {
            // Arrange
            var user = new User
            {
                Username = "testuser",
                DisplayName = "Test User",
                DiscordChannelId = "123456789012345678"
            };

            _mockDiscordBotClient
                .Setup(x => x.SendMessageAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<Discord.Embed>(),
                    It.IsAny<Discord.MessageComponent>(),
                    It.IsAny<Discord.AllowedMentions>()))
                .Returns(Task.CompletedTask);

            // Act
            await _service.SendTestNotificationAsync(user);

            // Assert
            _mockDiscordBotClient.Verify(
                x => x.SendMessageAsync(
                    user.DiscordChannelId,
                    "test-bot-token",
                    It.IsAny<string?>(),
                    It.IsAny<Discord.Embed>(),
                    It.IsAny<Discord.MessageComponent>(),
                    It.IsAny<Discord.AllowedMentions>()),
                Times.Once);

            _mockHttpMessageHandler.Protected().Verify(
                "SendAsync",
                Times.Never(),
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            );
        }

        [Fact]
        public async Task SendTestNotificationAsync_ShouldSendWebhook_WhenUrlConfigured_AndNoChannelId()
        {
            // Arrange
            var user = new User
            {
                Username = "testuser",
                DisplayName = "Test User",
                DiscordChannelId = string.Empty,
                DiscordWebhookUrl = "https://discord.com/api/webhooks/123/abc"
            };

            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK
                });

            // Act
            await _service.SendTestNotificationAsync(user);

            // Assert
            _mockHttpMessageHandler.Protected().Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Post &&
                    req.RequestUri != null &&
                    req.RequestUri.ToString() == user.DiscordWebhookUrl),
                ItExpr.IsAny<CancellationToken>()
            );
        }

        [Fact]
        public async Task SendTestNotificationAsync_ShouldNotSend_WhenUrlMissing()
        {
            // Arrange
            var user = new User
            {
                Username = "testuser",
                DiscordWebhookUrl = string.Empty,
                DiscordChannelId = string.Empty
            };

            // Act
            await _service.SendTestNotificationAsync(user);

            // Assert
            _mockHttpMessageHandler.Protected().Verify(
                "SendAsync",
                Times.Never(),
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            );
        }

        [Fact]
        public async Task SendNotificationAsync_ShouldSendDeathMilestone_WhenEnabled()
        {
            // Arrange
            var user = new User
            {
                Username = "testuser",
                DisplayName = "Test User",
                DiscordWebhookUrl = "https://discord.com/api/webhooks/123/abc",
                DiscordSettings = new DiscordSettings
                {
                    EnabledNotifications = new DiscordEnabledNotifications
                    {
                        DeathMilestone = true
                    }
                }
            };

            var eventData = new { count = 100, previousMilestone = 90 };

            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK
                });

            // Act
            await _service.SendNotificationAsync(user, "death_milestone", eventData);

            // Assert
            _mockHttpMessageHandler.Protected().Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Post &&
                    req.RequestUri != null &&
                    req.RequestUri.ToString() == user.DiscordWebhookUrl),
                ItExpr.IsAny<CancellationToken>()
            );
        }

        [Fact]
        public async Task SendNotificationAsync_ShouldNotSend_WhenDisabled()
        {
            // Arrange
            var user = new User
            {
                Username = "testuser",
                DiscordWebhookUrl = "https://discord.com/api/webhooks/123/abc",
                DiscordSettings = new DiscordSettings
                {
                    EnabledNotifications = new DiscordEnabledNotifications
                    {
                        DeathMilestone = false
                    }
                }
            };

            var eventData = new { count = 100 };

            // Act
            await _service.SendNotificationAsync(user, "death_milestone", eventData);

            // Assert
            _mockHttpMessageHandler.Protected().Verify(
                "SendAsync",
                Times.Never(),
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            );
        }

        [Fact]
        public async Task SendNotificationAsync_ShouldSendSwearMilestone_WhenEnabled()
        {
            // Arrange
            var user = new User
            {
                Username = "testuser",
                DisplayName = "Test User",
                DiscordWebhookUrl = "https://discord.com/api/webhooks/123/abc",
                DiscordSettings = new DiscordSettings
                {
                    EnabledNotifications = new DiscordEnabledNotifications { SwearMilestone = true }
                }
            };

            var eventData = new { count = 50, previousMilestone = 40 };

            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.OK });

            // Act
            await _service.SendNotificationAsync(user, "swear_milestone", eventData);

            // Assert
            _mockHttpMessageHandler.Protected().Verify("SendAsync", Times.Once(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
        }

        [Fact]
        public async Task SendNotificationAsync_ShouldSendScreamMilestone_WhenEnabled()
        {
            // Arrange
            var user = new User
            {
                Username = "testuser",
                DisplayName = "Test User",
                DiscordWebhookUrl = "https://discord.com/api/webhooks/123/abc",
                DiscordSettings = new DiscordSettings
                {
                    EnabledNotifications = new DiscordEnabledNotifications { ScreamMilestone = true }
                }
            };

            var eventData = new { count = 20, previousMilestone = 10 };

            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.OK });

            // Act
            await _service.SendNotificationAsync(user, "scream_milestone", eventData);

            // Assert
            _mockHttpMessageHandler.Protected().Verify("SendAsync", Times.Once(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
        }

        [Fact]
        public async Task SendNotificationAsync_ShouldSendStreamStart_WhenEnabled()
        {
            // Arrange
            var user = new User
            {
                Username = "testuser",
                DisplayName = "Test User",
                DiscordWebhookUrl = "https://discord.com/api/webhooks/123/abc",
                DiscordSettings = new DiscordSettings
                {
                    EnabledNotifications = new DiscordEnabledNotifications { StreamStart = true }
                }
            };

            var eventData = new { title = "Test Stream", game = "Just Chatting", thumbnailUrl = "http://example.com/thumb.jpg" };

            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.OK });

            // Act
            await _service.SendNotificationAsync(user, "stream_start", eventData);

            // Assert
            _mockHttpMessageHandler.Protected().Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("with_components=true")),
                ItExpr.IsAny<CancellationToken>());
        }

        [Fact]
        public async Task SendNotificationAsync_ShouldSendStreamEnd_WhenEnabled()
        {
            // Arrange
            var user = new User
            {
                Username = "testuser",
                DisplayName = "Test User",
                DiscordWebhookUrl = "https://discord.com/api/webhooks/123/abc",
                DiscordSettings = new DiscordSettings
                {
                    EnabledNotifications = new DiscordEnabledNotifications { StreamEnd = true }
                }
            };

            var eventData = new { duration = "2h 30m" };

            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.OK });

            // Act
            await _service.SendNotificationAsync(user, "stream_end", eventData);

            // Assert
            _mockHttpMessageHandler.Protected().Verify("SendAsync", Times.Once(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
        }

        [Fact]
        public async Task SendNotificationAsync_ShouldHandleDictionaryData()
        {
            // Arrange
            var user = new User
            {
                Username = "testuser",
                DisplayName = "Test User",
                DiscordWebhookUrl = "https://discord.com/api/webhooks/123/abc",
                DiscordSettings = new DiscordSettings
                {
                    EnabledNotifications = new DiscordEnabledNotifications { DeathMilestone = true }
                }
            };

            var eventData = new System.Collections.Generic.Dictionary<string, object>
            {
                { "count", 100 },
                { "previousMilestone", 90 }
            };

            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.OK });

            // Act
            await _service.SendNotificationAsync(user, "death_milestone", eventData);

            // Assert
            _mockHttpMessageHandler.Protected().Verify("SendAsync", Times.Once(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
        }

        [Fact]
        public async Task SendNotificationAsync_ShouldLogError_WhenHttpRequestFails()
        {
            // Arrange
            var user = new User
            {
                Username = "testuser",
                DisplayName = "Test User",
                DiscordWebhookUrl = "https://discord.com/api/webhooks/123/abc",
                DiscordSettings = new DiscordSettings
                {
                    EnabledNotifications = new DiscordEnabledNotifications { DeathMilestone = true }
                }
            };

            var eventData = new { count = 100 };

            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.InternalServerError });

            // Act & Assert - service throws on failure
            await Assert.ThrowsAsync<HttpRequestException>(() =>
                _service.SendNotificationAsync(user, "death_milestone", eventData));
        }

        [Fact]
        public async Task SendNotificationAsync_ShouldThrowException_WhenHttpRequestThrows()
        {
            // Arrange
            var user = new User
            {
                Username = "testuser",
                DisplayName = "Test User",
                DiscordWebhookUrl = "https://discord.com/api/webhooks/123/abc",
                DiscordSettings = new DiscordSettings
                {
                    EnabledNotifications = new DiscordEnabledNotifications { DeathMilestone = true }
                }
            };

            var eventData = new { count = 100 };

            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new HttpRequestException("Network error"));

            // Act & Assert - service throws on exception
            await Assert.ThrowsAsync<HttpRequestException>(() =>
                _service.SendNotificationAsync(user, "death_milestone", eventData));
        }

        [Fact]
        public async Task SendNotificationAsync_ShouldNotSend_WhenWebhookUrlEmpty()
        {
            // Arrange
            var user = new User
            {
                Username = "testuser",
                DiscordWebhookUrl = ""
            };

            var eventData = new { count = 100 };

            // Act
            await _service.SendNotificationAsync(user, "death_milestone", eventData);

            // Assert - should not send when webhook URL is empty
            _mockHttpMessageHandler.Protected().Verify(
                "SendAsync",
                Times.Never(),
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            );
        }

        [Fact]
        public async Task SendNotificationAsync_ShouldHandleUnknownEventType()
        {
            // Arrange
            var user = new User
            {
                Username = "testuser",
                DisplayName = "Test User",
                DiscordWebhookUrl = "https://discord.com/api/webhooks/123/abc",
                DiscordSettings = new DiscordSettings
                {
                    EnabledNotifications = new DiscordEnabledNotifications { DeathMilestone = true }
                }
            };

            var eventData = new { someData = "test" };

            // Act - should not throw but should not send for unknown event type (returns false from IsNotificationEnabled)
            await _service.SendNotificationAsync(user, "unknown_event_type", eventData);

            // Assert - unknown event type returns false from IsNotificationEnabled, so no send
            _mockHttpMessageHandler.Protected().Verify(
                "SendAsync",
                Times.Never(),
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            );
        }

        [Fact]
        public async Task SendTestNotificationAsync_ShouldThrowException_WhenHttpRequestThrows()
        {
            // Arrange
            var user = new User
            {
                Username = "testuser",
                DisplayName = "Test User",
                DiscordWebhookUrl = "https://discord.com/api/webhooks/123/abc"
            };

            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new HttpRequestException("Network error"));

            // Act & Assert - service throws on exception
            await Assert.ThrowsAsync<HttpRequestException>(() =>
                _service.SendTestNotificationAsync(user));
        }

        [Fact]
        public async Task SendTestNotificationAsync_ShouldNotSend_WhenWebhookUrlEmpty()
        {
            // Arrange
            var user = new User
            {
                Username = "testuser",
                DisplayName = "Test User",
                DiscordWebhookUrl = ""
            };

            // Act
            await _service.SendTestNotificationAsync(user);

            // Assert - should not send when webhook URL is empty
            _mockHttpMessageHandler.Protected().Verify(
                "SendAsync",
                Times.Never(),
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            );
        }

        [Fact]
        public async Task ValidateWebhookAsync_ShouldReturnFalse_WhenUrlEmpty()
        {
            // Act
            var result = await _service.ValidateWebhookAsync("");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task ValidateWebhookAsync_ShouldReturnFalse_WhenRequestFails()
        {
            // Arrange
            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new HttpRequestException("Network error"));

            // Act
            var result = await _service.ValidateWebhookAsync("https://discord.com/api/webhooks/123/abc");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task ValidateWebhookAsync_ShouldReturnTrue_WhenRequestSucceeds()
        {
            // Arrange
            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.OK });

            // Act
            var result = await _service.ValidateWebhookAsync("https://discord.com/api/webhooks/123/abc");

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task ValidateWebhookAsync_ShouldReturnFalse_WhenNon200Response()
        {
            // Arrange
            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.NotFound });

            // Act
            var result = await _service.ValidateWebhookAsync("https://discord.com/api/webhooks/123/abc");

            // Assert
            Assert.False(result);
        }
    }
}
