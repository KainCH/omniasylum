using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Services;
using TwitchLib.Client.Enums;
using TwitchLib.Client.Models;
using Xunit;

namespace OmniForge.Tests
{
    public class TwitchMessageHandlerDiscordTests
    {
        private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
        private readonly Mock<IServiceScope> _mockScope;
        private readonly Mock<IServiceProvider> _mockServiceProvider;
        private readonly Mock<IOverlayNotifier> _mockOverlayNotifier;
        private readonly Mock<ILogger<TwitchMessageHandler>> _mockLogger;
        private readonly Mock<ICounterRepository> _mockCounterRepository;
        private readonly Mock<IUserRepository> _mockUserRepository;
        private readonly Mock<IDiscordService> _mockDiscordService;
        private readonly TwitchMessageHandler _handler;

        public TwitchMessageHandlerDiscordTests()
        {
            _mockScopeFactory = new Mock<IServiceScopeFactory>();
            _mockScope = new Mock<IServiceScope>();
            _mockServiceProvider = new Mock<IServiceProvider>();
            _mockOverlayNotifier = new Mock<IOverlayNotifier>();
            _mockLogger = new Mock<ILogger<TwitchMessageHandler>>();
            _mockCounterRepository = new Mock<ICounterRepository>();
            _mockUserRepository = new Mock<IUserRepository>();
            _mockDiscordService = new Mock<IDiscordService>();

            _mockScopeFactory.Setup(x => x.CreateScope()).Returns(_mockScope.Object);
            _mockScope.Setup(x => x.ServiceProvider).Returns(_mockServiceProvider.Object);

            // Setup ServiceProvider to return mocks
            _mockServiceProvider.Setup(x => x.GetService(typeof(ICounterRepository)))
                .Returns(_mockCounterRepository.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(IUserRepository)))
                .Returns(_mockUserRepository.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(IDiscordService)))
                .Returns(_mockDiscordService.Object);

            // Required for GetRequiredService extension method
            _mockServiceProvider.Setup(x => x.GetService(typeof(ICounterRepository)))
                .Returns(_mockCounterRepository.Object);

            _handler = new TwitchMessageHandler(
                _mockScopeFactory.Object,
                _mockOverlayNotifier.Object,
                _mockLogger.Object);
        }

        private ChatMessage CreateMessage(string message, bool isMod = true)
        {
            return new ChatMessage(
                "bot", // botUsername
                "123", // userId
                "user", // userName
                "User", // displayName
                "", // colorHex
                Color.Black, // color
                null, // emoteSet
                message, // message
                UserType.Viewer, // userType
                "channel", // channel
                "id", // id
                false, // isSubscriber
                0, // subscribedMonthCount
                "room", // roomId
                false, // isTurbo
                isMod, // isModerator
                false, // isMe
                false, // isBroadcaster
                false, // isVip
                false, // isPartner
                false, // isStaff
                Noisy.False, // noisy
                "", // rawIrcMessage
                "", // emoteReplacedMessage
                null, // badges
                null, // cheerBadge
                0, // bits
                0 // bitsInDollars
            );
        }

        private User CreateUserWithDiscord(bool enabled = true, string webhookUrl = "https://discord.com/api/webhooks/...")
        {
            return new User
            {
                TwitchUserId = "user1",
                Features = new FeatureFlags { DiscordWebhook = enabled },
                DiscordWebhookUrl = webhookUrl,
                DiscordSettings = new DiscordSettings
                {
                    MilestoneThresholds = new DiscordMilestoneThresholds
                    {
                        Deaths = new List<int> { 10, 50, 100 },
                        Swears = new List<int> { 10, 50, 100 },
                        Screams = new List<int> { 5, 10 }
                    }
                },
                OverlaySettings = new OverlaySettings
                {
                    Counters = new OverlayCounters { Screams = true }
                }
            };
        }

        [Fact]
        public async Task HandleMessageAsync_ShouldSendDiscordNotification_WhenDeathMilestoneReached()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!death+");
            var user = CreateUserWithDiscord();

            // Current deaths is 9, so +1 will hit 10 (milestone)
            var counters = new Counter { Deaths = 9 };

            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);
            _mockUserRepository.Setup(x => x.GetUserAsync(userId)).ReturnsAsync(user);

            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.HandleMessageAsync(userId, message, sendMessageMock.Object);

            // Assert
            _mockDiscordService.Verify(x => x.SendNotificationAsync(
                It.Is<User>(u => u.TwitchUserId == userId),
                "death_milestone",
                It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public async Task HandleMessageAsync_ShouldSendDiscordNotification_WhenSwearMilestoneReached()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!swear+");
            var user = CreateUserWithDiscord();

            // Current swears is 49, so +1 will hit 50 (milestone)
            var counters = new Counter { Swears = 49 };

            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);
            _mockUserRepository.Setup(x => x.GetUserAsync(userId)).ReturnsAsync(user);

            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.HandleMessageAsync(userId, message, sendMessageMock.Object);

            // Assert
            _mockDiscordService.Verify(x => x.SendNotificationAsync(
                It.Is<User>(u => u.TwitchUserId == userId),
                "swear_milestone",
                It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public async Task HandleMessageAsync_ShouldSendDiscordNotification_WhenScreamMilestoneReached()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!scream+");
            var user = CreateUserWithDiscord();

            // Current screams is 4, so +1 will hit 5 (milestone)
            var counters = new Counter { Screams = 4 };

            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);
            _mockUserRepository.Setup(x => x.GetUserAsync(userId)).ReturnsAsync(user);

            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.HandleMessageAsync(userId, message, sendMessageMock.Object);

            // Assert
            _mockDiscordService.Verify(x => x.SendNotificationAsync(
                It.Is<User>(u => u.TwitchUserId == userId),
                "scream_milestone",
                It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public async Task HandleMessageAsync_ShouldNotSendDiscordNotification_WhenMilestoneNotReached()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!death+");
            var user = CreateUserWithDiscord();

            // Current deaths is 10, so +1 will be 11 (not a milestone)
            var counters = new Counter { Deaths = 10 };

            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);
            _mockUserRepository.Setup(x => x.GetUserAsync(userId)).ReturnsAsync(user);

            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.HandleMessageAsync(userId, message, sendMessageMock.Object);

            // Assert
            _mockDiscordService.Verify(x => x.SendNotificationAsync(
                It.IsAny<User>(),
                It.IsAny<string>(),
                It.IsAny<object>()), Times.Never);
        }

        [Fact]
        public async Task HandleMessageAsync_ShouldNotSendDiscordNotification_WhenFeatureDisabled()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!death+");
            var user = CreateUserWithDiscord(enabled: false);

            // Current deaths is 9, so +1 will hit 10 (milestone)
            var counters = new Counter { Deaths = 9 };

            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);
            _mockUserRepository.Setup(x => x.GetUserAsync(userId)).ReturnsAsync(user);

            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.HandleMessageAsync(userId, message, sendMessageMock.Object);

            // Assert
            _mockDiscordService.Verify(x => x.SendNotificationAsync(
                It.IsAny<User>(),
                It.IsAny<string>(),
                It.IsAny<object>()), Times.Never);
        }

        [Fact]
        public async Task HandleMessageAsync_ShouldNotSendDiscordNotification_WhenWebhookUrlEmpty()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!death+");
            var user = CreateUserWithDiscord(enabled: true, webhookUrl: "");

            // Current deaths is 9, so +1 will hit 10 (milestone)
            var counters = new Counter { Deaths = 9 };

            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);
            _mockUserRepository.Setup(x => x.GetUserAsync(userId)).ReturnsAsync(user);

            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.HandleMessageAsync(userId, message, sendMessageMock.Object);

            // Assert
            _mockDiscordService.Verify(x => x.SendNotificationAsync(
                It.IsAny<User>(),
                It.IsAny<string>(),
                It.IsAny<object>()), Times.Never);
        }

        [Fact]
        public async Task HandleMessageAsync_ShouldLogException_WhenDiscordServiceFails()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!death+");
            var user = CreateUserWithDiscord();
            var counters = new Counter { Deaths = 9 };

            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);
            _mockUserRepository.Setup(x => x.GetUserAsync(userId)).ReturnsAsync(user);

            _mockDiscordService.Setup(x => x.SendNotificationAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<object>()))
                .ThrowsAsync(new Exception("Discord API Error"));

            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.HandleMessageAsync(userId, message, sendMessageMock.Object);

            // Assert
            // Should not throw exception
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Error sending Discord notification")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }
    }
}
