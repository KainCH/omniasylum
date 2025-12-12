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
    public class ChatCommandProcessorTests
    {
        private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
        private readonly Mock<IServiceScope> _mockScope;
        private readonly Mock<IServiceProvider> _mockServiceProvider;
        private readonly Mock<IOverlayNotifier> _mockOverlayNotifier;
        private readonly Mock<ILogger<ChatCommandProcessor>> _mockLogger;
        private readonly Mock<ICounterRepository> _mockCounterRepository;
        private readonly Mock<IUserRepository> _mockUserRepository;
        private readonly ChatCommandProcessor _handler;

        public ChatCommandProcessorTests()
        {
            _mockScopeFactory = new Mock<IServiceScopeFactory>();
            _mockScope = new Mock<IServiceScope>();
            _mockServiceProvider = new Mock<IServiceProvider>();
            _mockOverlayNotifier = new Mock<IOverlayNotifier>();
            _mockLogger = new Mock<ILogger<ChatCommandProcessor>>();
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

            _handler = new ChatCommandProcessor(
                _mockScopeFactory.Object,
                _mockOverlayNotifier.Object,
                _mockLogger.Object);
        }

        private ChatMessage CreateMessage(string message, bool isMod = false, bool isBroadcaster = false, bool isSubscriber = false)
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
                isSubscriber, // isSubscriber
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


        private static ChatCommandContext ToContext(string userId, ChatMessage message)
        {
            return new ChatCommandContext
            {
                UserId = userId,
                Message = message.Message,
                IsModerator = message.IsModerator,
                IsBroadcaster = message.IsBroadcaster,
                IsSubscriber = message.IsSubscriber
            };
        }

        [Fact]
        public async Task ProcessAsync_ShouldIgnoreNonCommands()
        {
            // Arrange
            var message = CreateMessage("hello");
            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.ProcessAsync(ToContext("user1", message), sendMessageMock.Object);

            // Assert
            _mockScopeFactory.Verify(x => x.CreateScope(), Times.Never);
        }

        [Fact]
        public async Task ProcessAsync_ShouldReturnDeaths()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!deaths");
            var counters = new Counter { Deaths = 10 };
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);
            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.ProcessAsync(ToContext(userId, message), sendMessageMock.Object);

            // Assert
            sendMessageMock.Verify(x => x(userId, "Death Count: 10"), Times.Once);
        }

        [Fact]
        public async Task ProcessAsync_ShouldReturnSwears()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!swears");
            var counters = new Counter { Swears = 5 };
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);
            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.ProcessAsync(ToContext(userId, message), sendMessageMock.Object);

            // Assert
            sendMessageMock.Verify(x => x(userId, "Swear Count: 5"), Times.Once);
        }

        [Fact]
        public async Task ProcessAsync_ShouldReturnStats()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!stats");
            var counters = new Counter { Deaths = 10, Swears = 5, Screams = 2 };
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);
            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.ProcessAsync(ToContext(userId, message), sendMessageMock.Object);

            // Assert
            sendMessageMock.Verify(x => x(userId, "Deaths: 10 | Swears: 5 | Screams: 2"), Times.Once);
        }

        [Fact]
        public async Task ProcessAsync_ShouldDecrementDeaths_WhenModAndPositive()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!death-", isMod: true);
            var counters = new Counter { Deaths = 10 };
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);
            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.ProcessAsync(ToContext(userId, message), sendMessageMock.Object);

            // Assert
            Assert.Equal(9, counters.Deaths);
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(counters), Times.Once);
            sendMessageMock.Verify(x => x(userId, "Death Count: 9"), Times.Once);
        }

        [Fact]
        public async Task ProcessAsync_ShouldNotDecrementDeaths_WhenZero()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!death-", isMod: true);
            var counters = new Counter { Deaths = 0 };
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);
            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.ProcessAsync(ToContext(userId, message), sendMessageMock.Object);

            // Assert
            Assert.Equal(0, counters.Deaths);
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(It.IsAny<Counter>()), Times.Never);
        }

        [Fact]
        public async Task ProcessAsync_ShouldIncrementSwears_WhenMod()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!swear+", isMod: true);
            var counters = new Counter { Swears = 5 };
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);
            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.ProcessAsync(ToContext(userId, message), sendMessageMock.Object);

            // Assert
            Assert.Equal(6, counters.Swears);
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(counters), Times.Once);
            sendMessageMock.Verify(x => x(userId, "Swear Count: 6"), Times.Once);
        }

        [Fact]
        public async Task ProcessAsync_ShouldDecrementSwears_WhenModAndPositive()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!swear-", isMod: true);
            var counters = new Counter { Swears = 5 };
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);
            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.ProcessAsync(ToContext(userId, message), sendMessageMock.Object);

            // Assert
            Assert.Equal(4, counters.Swears);
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(counters), Times.Once);
            sendMessageMock.Verify(x => x(userId, "Swear Count: 4"), Times.Once);
        }

        [Fact]
        public async Task ProcessAsync_ShouldNotDecrementSwears_WhenZero()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!swear-", isMod: true);
            var counters = new Counter { Swears = 0 };
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);
            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.ProcessAsync(ToContext(userId, message), sendMessageMock.Object);

            // Assert
            Assert.Equal(0, counters.Swears);
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(It.IsAny<Counter>()), Times.Never);
        }

        [Fact]
        public async Task ProcessAsync_ShouldIncrementDeaths_WhenMod()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!death+", isMod: true);
            var counters = new Counter { Deaths = 10 };
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);
            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.ProcessAsync(ToContext(userId, message), sendMessageMock.Object);

            // Assert
            Assert.Equal(11, counters.Deaths);
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(counters), Times.Once);
            _mockOverlayNotifier.Verify(x => x.NotifyCounterUpdateAsync(userId, counters), Times.Once);
            sendMessageMock.Verify(x => x(userId, "Death Count: 11"), Times.Once);
        }

        [Fact]
        public async Task ProcessAsync_ShouldNotIncrementDeaths_WhenNotMod()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!death+");
            var counters = new Counter { Deaths = 10 };
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);
            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.ProcessAsync(ToContext(userId, message), sendMessageMock.Object);

            // Assert
            Assert.Equal(10, counters.Deaths);
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(It.IsAny<Counter>()), Times.Never);
        }

        [Fact]
        public async Task ProcessAsync_ShouldResetCounters_WhenMod()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!resetcounters", isMod: true);
            var counters = new Counter { Deaths = 10, Swears = 5, Screams = 2 };
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);
            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.ProcessAsync(ToContext(userId, message), sendMessageMock.Object);

            // Assert
            Assert.Equal(0, counters.Deaths);
            Assert.Equal(0, counters.Swears);
            Assert.Equal(0, counters.Screams);
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(counters), Times.Once);
            sendMessageMock.Verify(x => x(userId, "Counters have been reset."), Times.Once);
        }

        [Fact]
        public async Task ProcessAsync_ShouldNotResetCounters_WhenNotMod()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!resetcounters");
            var counters = new Counter { Deaths = 10 };
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);
            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.ProcessAsync(ToContext(userId, message), sendMessageMock.Object);

            // Assert
            Assert.Equal(10, counters.Deaths);
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(It.IsAny<Counter>()), Times.Never);
        }

        [Fact]
        public async Task ProcessAsync_ShouldReturnScreams_WhenEnabled()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!screams");
            var counters = new Counter { Screams = 5 };
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);
            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.ProcessAsync(ToContext(userId, message), sendMessageMock.Object);

            // Assert
            sendMessageMock.Verify(x => x(userId, "Scream Count: 5"), Times.Once);
        }

        [Fact]
        public async Task ProcessAsync_ShouldNotReturnScreams_WhenDisabled()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!screams");
            var counters = new Counter { Screams = 5 };
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);
            _mockUserRepository.Setup(x => x.GetUserAsync(userId)).ReturnsAsync(new User
            {
                TwitchUserId = userId,
                OverlaySettings = new OverlaySettings { Counters = new OverlayCounters { Screams = false } }
            });
            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.ProcessAsync(ToContext(userId, message), sendMessageMock.Object);

            // Assert
            sendMessageMock.Verify(x => x(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ProcessAsync_ShouldIncrementScreams_WhenModAndEnabled()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!scream+", isMod: true);
            var counters = new Counter { Screams = 5 };
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);
            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.ProcessAsync(ToContext(userId, message), sendMessageMock.Object);

            // Assert
            Assert.Equal(6, counters.Screams);
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(counters), Times.Once);
            sendMessageMock.Verify(x => x(userId, "Scream Count: 6"), Times.Once);
        }

        [Fact]
        public async Task ProcessAsync_ShouldDecrementScreams_WhenModAndEnabledAndPositive()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!scream-", isMod: true);
            var counters = new Counter { Screams = 5 };
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);
            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.ProcessAsync(ToContext(userId, message), sendMessageMock.Object);

            // Assert
            Assert.Equal(4, counters.Screams);
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(counters), Times.Once);
            sendMessageMock.Verify(x => x(userId, "Scream Count: 4"), Times.Once);
        }

        [Fact]
        public async Task ProcessAsync_ShouldIncrementScreams_WithShortAlias()
        {
            var userId = "user1";
            var message = CreateMessage("!sc+", isMod: true);
            var counters = new Counter { Screams = 2 };
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);
            var sendMessageMock = new Mock<Func<string, string, Task>>();

            await _handler.ProcessAsync(ToContext(userId, message), sendMessageMock.Object);

            Assert.Equal(3, counters.Screams);
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(counters), Times.Once);
            sendMessageMock.Verify(x => x(userId, "Scream Count: 3"), Times.Once);
        }

        [Fact]
        public async Task ProcessAsync_ShouldIncrementSwears_WithShortAlias()
        {
            var userId = "user1";
            var message = CreateMessage("!sw+", isMod: true);
            var counters = new Counter { Swears = 7 };
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);
            var sendMessageMock = new Mock<Func<string, string, Task>>();

            await _handler.ProcessAsync(ToContext(userId, message), sendMessageMock.Object);

            Assert.Equal(8, counters.Swears);
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(counters), Times.Once);
            sendMessageMock.Verify(x => x(userId, "Swear Count: 8"), Times.Once);
        }

        [Fact]
        public async Task ProcessAsync_ShouldExecuteCustomCommand_WhenPermissionGranted()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!custom");
            var counters = new Counter();
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);

            var chatCommands = new ChatCommandConfiguration
            {
                Commands = new Dictionary<string, ChatCommandDefinition>
                {
                    { "!custom", new ChatCommandDefinition { Response = "Custom Response", Permission = "Everyone", Cooldown = 0 } }
                }
            };
            _mockUserRepository.Setup(x => x.GetChatCommandsConfigAsync(userId)).ReturnsAsync(chatCommands);

            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.ProcessAsync(ToContext(userId, message), sendMessageMock.Object);

            // Assert
            sendMessageMock.Verify(x => x(userId, "Custom Response"), Times.Once);
        }

        [Fact]
        public async Task ProcessAsync_ShouldNotExecuteCustomCommand_WhenPermissionDenied()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!custom"); // Not mod
            var counters = new Counter();
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);

            var chatCommands = new ChatCommandConfiguration
            {
                Commands = new Dictionary<string, ChatCommandDefinition>
                {
                    { "!custom", new ChatCommandDefinition { Response = "Custom Response", Permission = "Moderator", Cooldown = 0 } }
                }
            };
            _mockUserRepository.Setup(x => x.GetChatCommandsConfigAsync(userId)).ReturnsAsync(chatCommands);

            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.ProcessAsync(ToContext(userId, message), sendMessageMock.Object);

            // Assert
            sendMessageMock.Verify(x => x(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ProcessAsync_ShouldRespectCooldown()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!custom");
            var counters = new Counter();
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);

            var chatCommands = new ChatCommandConfiguration
            {
                Commands = new Dictionary<string, ChatCommandDefinition>
                {
                    { "!custom", new ChatCommandDefinition { Response = "Custom Response", Permission = "Everyone", Cooldown = 10 } }
                }
            };
            _mockUserRepository.Setup(x => x.GetChatCommandsConfigAsync(userId)).ReturnsAsync(chatCommands);

            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.ProcessAsync(ToContext(userId, message), sendMessageMock.Object); // First call
            await _handler.ProcessAsync(ToContext(userId, message), sendMessageMock.Object); // Second call (should be on cooldown)

            // Assert
            sendMessageMock.Verify(x => x(userId, "Custom Response"), Times.Once); // Only once
        }

        [Fact]
        public async Task ProcessAsync_ShouldExecuteCustomCommand_WhenSubscriberPermissionAndIsSubscriber()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!subcmd");
            // We need to set IsSubscriber on the message, but CreateMessage helper sets it to false by default.
            // Let's modify CreateMessage or create a new one manually.
            // CreateMessage signature: (string message, bool isMod = false, bool isBroadcaster = false)
            // It doesn't expose isSubscriber. I'll create a new helper or just instantiate ChatMessage directly.

            var chatMessage = new ChatMessage(
                "bot", "123", "user", "User", "", Color.Black, null, "!subcmd", UserType.Viewer, "channel", "id",
                true, // isSubscriber
                0, "room", false, false, false, false, false, false, false, Noisy.False, "", "", null, null, 0, 0);

            var counters = new Counter();
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);

            var chatCommands = new ChatCommandConfiguration
            {
                Commands = new Dictionary<string, ChatCommandDefinition>
                {
                    { "!subcmd", new ChatCommandDefinition { Response = "Sub Response", Permission = "Subscriber", Cooldown = 0 } }
                }
            };
            _mockUserRepository.Setup(x => x.GetChatCommandsConfigAsync(userId)).ReturnsAsync(chatCommands);

            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.ProcessAsync(ToContext(userId, chatMessage), sendMessageMock.Object);

            // Assert
            sendMessageMock.Verify(x => x(userId, "Sub Response"), Times.Once);
        }

        [Fact]
        public async Task ProcessAsync_ShouldNotExecuteCustomCommand_WhenSubscriberPermissionAndNotSubscriber()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!subcmd"); // isSubscriber = false
            var counters = new Counter();
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);

            var chatCommands = new ChatCommandConfiguration
            {
                Commands = new Dictionary<string, ChatCommandDefinition>
                {
                    { "!subcmd", new ChatCommandDefinition { Response = "Sub Response", Permission = "Subscriber", Cooldown = 0 } }
                }
            };
            _mockUserRepository.Setup(x => x.GetChatCommandsConfigAsync(userId)).ReturnsAsync(chatCommands);

            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.ProcessAsync(ToContext(userId, message), sendMessageMock.Object);

            // Assert
            sendMessageMock.Verify(x => x(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ProcessAsync_ShouldExecuteCustomCommand_WhenBroadcasterPermissionAndIsBroadcaster()
        {
            // Arrange
            var userId = "user1";
            var message = CreateMessage("!broadcastercmd", isBroadcaster: true);
            var counters = new Counter();
            _mockCounterRepository.Setup(x => x.GetCountersAsync(userId)).ReturnsAsync(counters);

            var chatCommands = new ChatCommandConfiguration
            {
                Commands = new Dictionary<string, ChatCommandDefinition>
                {
                    { "!broadcastercmd", new ChatCommandDefinition { Response = "Broadcaster Response", Permission = "Broadcaster", Cooldown = 0 } }
                }
            };
            _mockUserRepository.Setup(x => x.GetChatCommandsConfigAsync(userId)).ReturnsAsync(chatCommands);

            var sendMessageMock = new Mock<Func<string, string, Task>>();

            // Act
            await _handler.ProcessAsync(ToContext(userId, message), sendMessageMock.Object);

            // Assert
            sendMessageMock.Verify(x => x(userId, "Broadcaster Response"), Times.Once);
        }
    }
}
