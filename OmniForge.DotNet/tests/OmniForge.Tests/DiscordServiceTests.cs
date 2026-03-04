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
using OmniForge.Core.Utilities;
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
            _service = new DiscordService(_httpClient, _mockLogger.Object, botSettings, _mockDiscordBotClient.Object, new LogValueSanitizer());
        }

        [Fact]
        public async Task SendNotificationAsync_ShouldUseChannelOverride_WhenBotModeConfigured()
        {
            // Arrange
            var user = new User
            {
                Username = "testuser",
                DisplayName = "Test User",
                DiscordChannelId = "111111111111111111",
                DiscordSettings = new DiscordSettings
                {
                    EnabledNotifications = new DiscordEnabledNotifications { StreamEnd = true },
                    MessageTemplates = new System.Collections.Generic.Dictionary<string, DiscordMessageTemplate>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["stream_end"] = new DiscordMessageTemplate { ChannelIdOverride = "222222222222222222" }
                    }
                }
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
            await _service.SendNotificationAsync(user, "stream_end", new { duration = "2h" });

            // Assert
            _mockDiscordBotClient.Verify(
                x => x.SendMessageAsync(
                    "222222222222222222",
                    "test-bot-token",
                    It.IsAny<string?>(),
                    It.IsAny<Discord.Embed>(),
                    It.IsAny<Discord.MessageComponent>(),
                    It.IsAny<Discord.AllowedMentions>()),
                Times.Once);

            // No webhook should be used
            _mockHttpMessageHandler.Protected().Verify(
                "SendAsync",
                Times.Never(),
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            );
        }

        [Fact]
        public async Task SendNotificationAsync_ShouldApplyTemplates_ForTitleDescriptionAndContent()
        {
            // Arrange
            Discord.Embed? capturedEmbed = null;
            string? capturedContent = null;

            var user = new User
            {
                Username = "testuser",
                DisplayName = "Test User",
                DiscordChannelId = "123456789012345678",
                DiscordSettings = new DiscordSettings
                {
                    EnabledNotifications = new DiscordEnabledNotifications { StreamEnd = true },
                    MessageTemplates = new System.Collections.Generic.Dictionary<string, DiscordMessageTemplate>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["stream_end"] = new DiscordMessageTemplate
                        {
                            ContentTemplate = "Custom content for {{displayName}} ({{eventType}})",
                            TitleTemplate = "Custom title: {{displayName}}",
                            DescriptionTemplate = "Duration was {{duration}}"
                        }
                    }
                }
            };

            _mockDiscordBotClient
                .Setup(x => x.SendMessageAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<Discord.Embed>(),
                    It.IsAny<Discord.MessageComponent>(),
                    It.IsAny<Discord.AllowedMentions>()))
                .Callback<string, string, string?, Discord.Embed, Discord.MessageComponent, Discord.AllowedMentions>((_, _, content, embed, _, _) =>
                {
                    capturedContent = content;
                    capturedEmbed = embed;
                })
                .Returns(Task.CompletedTask);

            // Act
            await _service.SendNotificationAsync(user, "stream_end", new { duration = "2h 30m" });

            // Assert
            Assert.Equal("Custom content for Test User (stream_end)", capturedContent);
            Assert.NotNull(capturedEmbed);
            Assert.Equal("Custom title: Test User", capturedEmbed!.Title);
            Assert.Equal("Duration was 2h 30m", capturedEmbed.Description);
        }

        [Fact]
        public async Task SendNotificationAsync_StreamStart_ContentTemplate_CanIncludeMentionsToken()
        {
            // Arrange
            string? capturedContent = null;
            Discord.AllowedMentions? capturedMentions = null;

            var user = new User
            {
                Username = "testuser",
                DisplayName = "Test User",
                DiscordChannelId = "123456789012345678",
                DiscordSettings = new DiscordSettings
                {
                    EnabledNotifications = new DiscordEnabledNotifications { StreamStart = true },
                    MentionEveryoneOnStreamStart = true,
                    MessageTemplates = new System.Collections.Generic.Dictionary<string, DiscordMessageTemplate>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["stream_start"] = new DiscordMessageTemplate
                        {
                            ContentTemplate = "{{streamStartMentions}} {{displayName}} is live! {{twitchUrl}}"
                        }
                    }
                }
            };

            _mockDiscordBotClient
                .Setup(x => x.SendMessageAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<Discord.Embed>(),
                    It.IsAny<Discord.MessageComponent>(),
                    It.IsAny<Discord.AllowedMentions>()))
                .Callback<string, string, string?, Discord.Embed, Discord.MessageComponent, Discord.AllowedMentions>((_, _, content, _, _, mentions) =>
                {
                    capturedContent = content;
                    capturedMentions = mentions;
                })
                .Returns(Task.CompletedTask);

            // Act
            await _service.SendNotificationAsync(user, "stream_start", new { title = "Test Stream", game = "Just Chatting" });

            // Assert
            Assert.NotNull(capturedContent);
            Assert.Contains("@everyone", capturedContent);
            Assert.Contains("Test User is live!", capturedContent);
            Assert.Contains("https://twitch.tv/testuser", capturedContent);

            Assert.NotNull(capturedMentions);
            Assert.True((capturedMentions!.AllowedTypes & Discord.AllowedMentionTypes.Everyone) == Discord.AllowedMentionTypes.Everyone);
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
        public async Task SendTestNotificationAsync_ShouldNotSend_WhenChannelIdMissing()
        {
            // Arrange
            var user = new User
            {
                Username = "testuser",
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
                DiscordChannelId = "123456789012345678",
                DiscordSettings = new DiscordSettings
                {
                    EnabledNotifications = new DiscordEnabledNotifications
                    {
                        DeathMilestone = true
                    }
                }
            };

            var eventData = new { count = 100, previousMilestone = 90 };

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
            await _service.SendNotificationAsync(user, "death_milestone", eventData);

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
        }

        [Fact]
        public async Task SendNotificationAsync_ShouldNotSend_WhenDisabled()
        {
            // Arrange
            var user = new User
            {
                Username = "testuser",

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
                DiscordChannelId = "123456789012345678",
                DiscordSettings = new DiscordSettings
                {
                    EnabledNotifications = new DiscordEnabledNotifications { SwearMilestone = true }
                }
            };

            var eventData = new { count = 50, previousMilestone = 40 };

            _mockDiscordBotClient
                .Setup(x => x.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Discord.Embed>(), It.IsAny<Discord.MessageComponent>(), It.IsAny<Discord.AllowedMentions>()))
                .Returns(Task.CompletedTask);

            // Act
            await _service.SendNotificationAsync(user, "swear_milestone", eventData);

            // Assert
            _mockDiscordBotClient.Verify(x => x.SendMessageAsync(user.DiscordChannelId, "test-bot-token", It.IsAny<string?>(), It.IsAny<Discord.Embed>(), It.IsAny<Discord.MessageComponent>(), It.IsAny<Discord.AllowedMentions>()), Times.Once);
        }

        [Fact]
        public async Task SendNotificationAsync_ShouldSendScreamMilestone_WhenEnabled()
        {
            // Arrange
            var user = new User
            {
                Username = "testuser",
                DisplayName = "Test User",
                DiscordChannelId = "123456789012345678",
                DiscordSettings = new DiscordSettings
                {
                    EnabledNotifications = new DiscordEnabledNotifications { ScreamMilestone = true }
                }
            };

            var eventData = new { count = 20, previousMilestone = 10 };

            _mockDiscordBotClient
                .Setup(x => x.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Discord.Embed>(), It.IsAny<Discord.MessageComponent>(), It.IsAny<Discord.AllowedMentions>()))
                .Returns(Task.CompletedTask);

            // Act
            await _service.SendNotificationAsync(user, "scream_milestone", eventData);

            // Assert
            _mockDiscordBotClient.Verify(x => x.SendMessageAsync(user.DiscordChannelId, "test-bot-token", It.IsAny<string?>(), It.IsAny<Discord.Embed>(), It.IsAny<Discord.MessageComponent>(), It.IsAny<Discord.AllowedMentions>()), Times.Once);
        }

        [Fact]
        public async Task SendNotificationAsync_ShouldSendStreamStart_WhenEnabled()
        {
            // Arrange
            var user = new User
            {
                Username = "testuser",
                DisplayName = "Test User",
                DiscordChannelId = "123456789012345678",
                DiscordSettings = new DiscordSettings
                {
                    EnabledNotifications = new DiscordEnabledNotifications { StreamStart = true }
                }
            };

            var eventData = new { title = "Test Stream", game = "Just Chatting", thumbnailUrl = "http://example.com/thumb.jpg" };

            _mockDiscordBotClient
                .Setup(x => x.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Discord.Embed>(), It.IsAny<Discord.MessageComponent>(), It.IsAny<Discord.AllowedMentions>()))
                .Returns(Task.CompletedTask);

            // Act
            await _service.SendNotificationAsync(user, "stream_start", eventData);

            // Assert
            _mockDiscordBotClient.Verify(x => x.SendMessageAsync(user.DiscordChannelId, "test-bot-token", It.IsAny<string?>(), It.IsAny<Discord.Embed>(), It.IsAny<Discord.MessageComponent>(), It.IsAny<Discord.AllowedMentions>()), Times.Once);
        }

        [Fact]
        public async Task SendNotificationAsync_ShouldSendStreamEnd_WhenEnabled()
        {
            // Arrange
            var user = new User
            {
                Username = "testuser",
                DisplayName = "Test User",
                DiscordChannelId = "123456789012345678",
                DiscordSettings = new DiscordSettings
                {
                    EnabledNotifications = new DiscordEnabledNotifications { StreamEnd = true }
                }
            };

            var eventData = new { duration = "2h 30m" };

            _mockDiscordBotClient
                .Setup(x => x.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Discord.Embed>(), It.IsAny<Discord.MessageComponent>(), It.IsAny<Discord.AllowedMentions>()))
                .Returns(Task.CompletedTask);

            // Act
            await _service.SendNotificationAsync(user, "stream_end", eventData);

            // Assert
            _mockDiscordBotClient.Verify(x => x.SendMessageAsync(user.DiscordChannelId, "test-bot-token", It.IsAny<string?>(), It.IsAny<Discord.Embed>(), It.IsAny<Discord.MessageComponent>(), It.IsAny<Discord.AllowedMentions>()), Times.Once);
        }

        [Fact]
        public async Task SendNotificationAsync_ShouldHandleDictionaryData()
        {
            // Arrange
            var user = new User
            {
                Username = "testuser",
                DisplayName = "Test User",
                DiscordChannelId = "123456789012345678",
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

            _mockDiscordBotClient
                .Setup(x => x.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Discord.Embed>(), It.IsAny<Discord.MessageComponent>(), It.IsAny<Discord.AllowedMentions>()))
                .Returns(Task.CompletedTask);

            // Act
            await _service.SendNotificationAsync(user, "death_milestone", eventData);

            // Assert
            _mockDiscordBotClient.Verify(x => x.SendMessageAsync(user.DiscordChannelId, "test-bot-token", It.IsAny<string?>(), It.IsAny<Discord.Embed>(), It.IsAny<Discord.MessageComponent>(), It.IsAny<Discord.AllowedMentions>()), Times.Once);
        }

        [Fact]
        public async Task SendNotificationAsync_ShouldThrowException_WhenBotClientThrows()
        {
            // Arrange
            var user = new User
            {
                Username = "testuser",
                DisplayName = "Test User",
                DiscordChannelId = "123456789012345678",
                DiscordSettings = new DiscordSettings
                {
                    EnabledNotifications = new DiscordEnabledNotifications { DeathMilestone = true }
                }
            };

            var eventData = new { count = 100 };

            _mockDiscordBotClient
                .Setup(x => x.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Discord.Embed>(), It.IsAny<Discord.MessageComponent>(), It.IsAny<Discord.AllowedMentions>()))
                .ThrowsAsync(new HttpRequestException("Network error"));

            // Act & Assert
            await Assert.ThrowsAsync<HttpRequestException>(() =>
                _service.SendNotificationAsync(user, "death_milestone", eventData));
        }

        [Fact]
        public async Task SendNotificationAsync_ShouldNotSend_WhenNoDestinationConfigured()
        {
            // Arrange
            var user = new User
            {
                Username = "testuser",
                DiscordChannelId = string.Empty
            };

            var eventData = new { count = 100 };

            // Act
            await _service.SendNotificationAsync(user, "death_milestone", eventData);

            // Assert - should not send when no channel ID is configured
            _mockDiscordBotClient.Verify(
                x => x.SendMessageAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<Discord.Embed>(),
                    It.IsAny<Discord.MessageComponent>(),
                    It.IsAny<Discord.AllowedMentions>()),
                Times.Never);
        }

        [Fact]
        public async Task SendNotificationAsync_ShouldHandleUnknownEventType()
        {
            // Arrange
            var user = new User
            {
                Username = "testuser",
                DisplayName = "Test User",

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
        public async Task SendTestNotificationAsync_ShouldThrowException_WhenBotClientThrows()
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
                .ThrowsAsync(new HttpRequestException("Network error"));

            // Act & Assert - service throws on exception
            await Assert.ThrowsAsync<HttpRequestException>(() =>
                _service.SendTestNotificationAsync(user));
        }

        #region SendGameChangeAnnouncementAsync Tests

        [Fact]
        public async Task SendGameChangeAnnouncementAsync_ShouldSendToAnnouncementChannel()
        {
            var user = new User
            {
                Username = "testuser",
                DisplayName = "Test User",
                DiscordChannelId = "123456789012345678",
                DiscordSettings = new DiscordSettings
                {
                    EnabledNotifications = new DiscordEnabledNotifications { GameChange = true },
                    MentionEveryoneOnStreamStart = true
                }
            };

            _mockDiscordBotClient
                .Setup(x => x.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Discord.Embed>(), It.IsAny<Discord.MessageComponent>(), It.IsAny<Discord.AllowedMentions>()))
                .Returns(Task.CompletedTask);

            await _service.SendGameChangeAnnouncementAsync(user, "Elden Ring", "https://example.com/box.jpg");

            _mockDiscordBotClient.Verify(x => x.SendMessageAsync(
                "123456789012345678", "test-bot-token",
                It.Is<string?>(c => c != null && c.Contains("@everyone")),
                It.IsAny<Discord.Embed>(),
                It.IsAny<Discord.MessageComponent>(),
                It.IsAny<Discord.AllowedMentions>()), Times.Once);
        }

        [Fact]
        public async Task SendGameChangeAnnouncementAsync_ShouldNotSend_WhenChannelEmpty()
        {
            var user = new User { Username = "testuser", DiscordChannelId = "" };

            await _service.SendGameChangeAnnouncementAsync(user, "Elden Ring", null);

            _mockDiscordBotClient.Verify(x => x.SendMessageAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<Discord.Embed>(), It.IsAny<Discord.MessageComponent>(),
                It.IsAny<Discord.AllowedMentions>()), Times.Never);
        }

        [Fact]
        public async Task SendGameChangeAnnouncementAsync_ShouldNotSend_WhenGameChangeDisabled()
        {
            var user = new User
            {
                Username = "testuser",
                DiscordChannelId = "123456789012345678",
                DiscordSettings = new DiscordSettings
                {
                    EnabledNotifications = new DiscordEnabledNotifications { GameChange = false }
                }
            };

            await _service.SendGameChangeAnnouncementAsync(user, "Elden Ring", null);

            _mockDiscordBotClient.Verify(x => x.SendMessageAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<Discord.Embed>(), It.IsAny<Discord.MessageComponent>(),
                It.IsAny<Discord.AllowedMentions>()), Times.Never);
        }

        #endregion

        #region SendModChannelNotificationAsync Tests

        [Fact]
        public async Task SendModChannelNotificationAsync_ShouldSendToModChannel()
        {
            var user = new User
            {
                Username = "testuser",
                DisplayName = "Test User",
                DiscordModChannelId = "987654321098765432"
            };

            _mockDiscordBotClient
                .Setup(x => x.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Discord.Embed>(), It.IsAny<Discord.MessageComponent>(), It.IsAny<Discord.AllowedMentions>()))
                .Returns(Task.CompletedTask);

            var descriptions = new[] { "\ud83d\udc80 **Deaths** \u2014 `!deaths` `!d+` `!d-`", "\ud83e\udd2c **Swears** \u2014 `!swears` `!sw+` `!sw-`" };

            await _service.SendModChannelNotificationAsync(user, "Elden Ring", descriptions);

            _mockDiscordBotClient.Verify(x => x.SendMessageAsync(
                "987654321098765432", "test-bot-token",
                It.IsAny<string?>(), It.IsAny<Discord.Embed>(),
                It.IsAny<Discord.MessageComponent>(), It.IsAny<Discord.AllowedMentions>()), Times.Once);
        }

        [Fact]
        public async Task SendModChannelNotificationAsync_ShouldNotSend_WhenModChannelEmpty()
        {
            var user = new User { Username = "testuser", DiscordModChannelId = "" };

            await _service.SendModChannelNotificationAsync(user, "Elden Ring", new[] { "counter1" });

            _mockDiscordBotClient.Verify(x => x.SendMessageAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<Discord.Embed>(), It.IsAny<Discord.MessageComponent>(),
                It.IsAny<Discord.AllowedMentions>()), Times.Never);
        }

        [Fact]
        public async Task SendModChannelNotificationAsync_ShouldSend_WhenNoCountersEnabled()
        {
            var user = new User
            {
                Username = "testuser",
                DisplayName = "Test User",
                DiscordModChannelId = "987654321098765432"
            };

            _mockDiscordBotClient
                .Setup(x => x.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Discord.Embed>(), It.IsAny<Discord.MessageComponent>(), It.IsAny<Discord.AllowedMentions>()))
                .Returns(Task.CompletedTask);

            await _service.SendModChannelNotificationAsync(user, "Just Chatting", Array.Empty<string>());

            _mockDiscordBotClient.Verify(x => x.SendMessageAsync(
                "987654321098765432", "test-bot-token",
                It.IsAny<string?>(), It.IsAny<Discord.Embed>(),
                It.IsAny<Discord.MessageComponent>(), It.IsAny<Discord.AllowedMentions>()), Times.Once);
        }

        #endregion

    }
}
