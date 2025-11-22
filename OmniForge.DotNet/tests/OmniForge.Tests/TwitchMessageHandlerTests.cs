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
using TwitchLib.Client.Models.Builders;
using Xunit;

namespace OmniForge.Tests
{
    public class TwitchMessageHandlerTests
    {
        private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
        private readonly Mock<IServiceScope> _mockScope;
        private readonly Mock<IServiceProvider> _mockServiceProvider;
        private readonly Mock<IOverlayNotifier> _mockOverlayNotifier;
        private readonly Mock<ILogger<TwitchMessageHandler>> _mockLogger;
        private readonly Mock<ICounterRepository> _mockCounterRepository;
        private readonly Mock<IUserRepository> _mockUserRepository;
        private readonly TwitchMessageHandler _handler;

        public TwitchMessageHandlerTests()
        {
            _mockScopeFactory = new Mock<IServiceScopeFactory>();
            _mockScope = new Mock<IServiceScope>();
            _mockServiceProvider = new Mock<IServiceProvider>();
            _mockOverlayNotifier = new Mock<IOverlayNotifier>();
            _mockLogger = new Mock<ILogger<TwitchMessageHandler>>();
            _mockCounterRepository = new Mock<ICounterRepository>();
            _mockUserRepository = new Mock<IUserRepository>();

            _mockScopeFactory.Setup(x => x.CreateScope()).Returns(_mockScope.Object);
            _mockScope.Setup(x => x.ServiceProvider).Returns(_mockServiceProvider.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(ICounterRepository)))
                .Returns(_mockCounterRepository.Object);
            _mockServiceProvider.Setup(x => x.GetService(typeof(IUserRepository)))
                .Returns(_mockUserRepository.Object);

            // Default user setup
            _mockUserRepository.Setup(x => x.GetUserAsync(It.IsAny<string>()))
                .ReturnsAsync(new User
                {
                    TwitchUserId = "user1",
                    OverlaySettings = new OverlaySettings
                    {
                        Counters = new OverlayCounters { Screams = true }
                    },
                    DiscordSettings = new DiscordSettings() // Ensure this is not null for milestone checks
                });

            _handler = new TwitchMessageHandler(
                _mockScopeFactory.Object,
                _mockOverlayNotifier.Object,
                _mockLogger.Object);
        }

        private ChatMessage CreateMessage(string message, bool isMod = false, bool isBroadcaster = false)
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
                isBroadcaster, // isBroadcaster
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


        [Fact]
        public async Task HandleMessageAsync_ShouldIgnoreNonCommands()
        {
            // Arrange
            var message = CreateMessage("hello");
            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.HandleMessageAsync("user1", message, sendMessageMock.Object);

            // Assert
            _mockScopeFactory.Verify(x => x.CreateScope(), Times.Never);
        }

        [Fact]
        public async Task HandleMessageAsync_ShouldReturnDeaths()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!deaths");
            var counters = new Counter { Deaths = 10 };
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);
            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.HandleMessageAsync(userId, message, sendMessageMock.Object);

            // Assert
            sendMessageMock.Verify(x => x(userId, "Death Count: 10"), Times.Once);
        }

        [Fact]
        public async Task HandleMessageAsync_ShouldReturnSwears()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!swears");
            var counters = new Counter { Swears = 5 };
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);
            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.HandleMessageAsync(userId, message, sendMessageMock.Object);

            // Assert
            sendMessageMock.Verify(x => x(userId, "Swear Count: 5"), Times.Once);
        }

        [Fact]
        public async Task HandleMessageAsync_ShouldReturnStats()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!stats");
            var counters = new Counter { Deaths = 10, Swears = 5, Screams = 2 };
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);
            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.HandleMessageAsync(userId, message, sendMessageMock.Object);

            // Assert
            sendMessageMock.Verify(x => x(userId, "Deaths: 10 | Swears: 5 | Screams: 2"), Times.Once);
        }

        [Fact]
        public async Task HandleMessageAsync_ShouldDecrementDeaths_WhenModAndPositive()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!death-", isMod: true);
            var counters = new Counter { Deaths = 10 };
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);
            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.HandleMessageAsync(userId, message, sendMessageMock.Object);

            // Assert
            Assert.Equal(9, counters.Deaths);
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(counters), Times.Once);
            sendMessageMock.Verify(x => x(userId, "Death Count: 9"), Times.Once);
        }

        [Fact]
        public async Task HandleMessageAsync_ShouldNotDecrementDeaths_WhenZero()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!death-", isMod: true);
            var counters = new Counter { Deaths = 0 };
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);
            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.HandleMessageAsync(userId, message, sendMessageMock.Object);

            // Assert
            Assert.Equal(0, counters.Deaths);
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(It.IsAny<Counter>()), Times.Never);
        }

        [Fact]
        public async Task HandleMessageAsync_ShouldIncrementSwears_WhenMod()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!swear+", isMod: true);
            var counters = new Counter { Swears = 5 };
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);
            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.HandleMessageAsync(userId, message, sendMessageMock.Object);

            // Assert
            Assert.Equal(6, counters.Swears);
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(counters), Times.Once);
            sendMessageMock.Verify(x => x(userId, "Swear Count: 6"), Times.Once);
        }

        [Fact]
        public async Task HandleMessageAsync_ShouldDecrementSwears_WhenModAndPositive()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!swear-", isMod: true);
            var counters = new Counter { Swears = 5 };
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);
            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.HandleMessageAsync(userId, message, sendMessageMock.Object);

            // Assert
            Assert.Equal(4, counters.Swears);
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(counters), Times.Once);
            sendMessageMock.Verify(x => x(userId, "Swear Count: 4"), Times.Once);
        }

        [Fact]
        public async Task HandleMessageAsync_ShouldNotDecrementSwears_WhenZero()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!swear-", isMod: true);
            var counters = new Counter { Swears = 0 };
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);
            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.HandleMessageAsync(userId, message, sendMessageMock.Object);

            // Assert
            Assert.Equal(0, counters.Swears);
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(It.IsAny<Counter>()), Times.Never);
        }

        [Fact]
        public async Task HandleMessageAsync_ShouldIncrementDeaths_WhenMod()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!death+", isMod: true);
            var counters = new Counter { Deaths = 10 };
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);
            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.HandleMessageAsync(userId, message, sendMessageMock.Object);

            // Assert
            Assert.Equal(11, counters.Deaths);
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(counters), Times.Once);
            _mockOverlayNotifier.Verify(x => x.NotifyCounterUpdateAsync(userId, counters), Times.Once);
            sendMessageMock.Verify(x => x(userId, "Death Count: 11"), Times.Once);
        }

        [Fact]
        public async Task HandleMessageAsync_ShouldNotIncrementDeaths_WhenNotMod()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!death+");
            var counters = new Counter { Deaths = 10 };
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);
            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.HandleMessageAsync(userId, message, sendMessageMock.Object);

            // Assert
            Assert.Equal(10, counters.Deaths);
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(It.IsAny<Counter>()), Times.Never);
        }

        [Fact]
        public async Task HandleMessageAsync_ShouldResetCounters_WhenMod()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!resetcounters", isMod: true);
            var counters = new Counter { Deaths = 10, Swears = 5, Screams = 2 };
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);
            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.HandleMessageAsync(userId, message, sendMessageMock.Object);

            // Assert
            Assert.Equal(0, counters.Deaths);
            Assert.Equal(0, counters.Swears);
            Assert.Equal(0, counters.Screams);
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(counters), Times.Once);
            sendMessageMock.Verify(x => x(userId, "Counters have been reset."), Times.Once);
        }
    }
}
