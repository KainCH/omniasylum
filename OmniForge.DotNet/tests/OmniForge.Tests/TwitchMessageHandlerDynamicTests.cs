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
    public class ChatCommandProcessorDynamicTests
    {
        private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
        private readonly Mock<IServiceScope> _mockScope;
        private readonly Mock<IServiceProvider> _mockServiceProvider;
        private readonly Mock<IOverlayNotifier> _mockOverlayNotifier;
        private readonly Mock<ILogger<ChatCommandProcessor>> _mockLogger;
        private readonly Mock<ICounterRepository> _mockCounterRepository;
        private readonly Mock<IUserRepository> _mockUserRepository;
        private readonly ChatCommandProcessor _handler;

        public ChatCommandProcessorDynamicTests()
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
                    OverlaySettings = new OverlaySettings(),
                    DiscordSettings = new DiscordSettings()
                });

            // Default counters setup
            _mockCounterRepository.Setup(x => x.GetCountersAsync(It.IsAny<string>()))
                .ReturnsAsync(new Counter());

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
        public async Task ProcessAsync_ShouldExecuteCustomCommand()
        {
            // Arrange
            var userId = "user1";
            var command = "!hello";
            var response = "Hello World!";
            var config = new ChatCommandConfiguration
            {
                Commands = new Dictionary<string, ChatCommandDefinition>
                {
                    { command, new ChatCommandDefinition { Response = response, Permission = "everyone", Cooldown = 0 } }
                }
            };

            _mockUserRepository.Setup(x => x.GetChatCommandsConfigAsync(userId))
                .ReturnsAsync(config);

            var message = CreateMessage(command);
            string? sentMessage = null;

            // Act
            await _handler.ProcessAsync(ToContext(userId, message), (uid, msg) =>
            {
                sentMessage = msg;
                return Task.CompletedTask;
            });

            // Assert
            Assert.Equal(response, sentMessage);
        }

        [Fact]
        public async Task ProcessAsync_ShouldRespectPermission_Moderator()
        {
            // Arrange
            var userId = "user1";
            var command = "!modonly";
            var response = "Mod Only!";
            var config = new ChatCommandConfiguration
            {
                Commands = new Dictionary<string, ChatCommandDefinition>
                {
                    { command, new ChatCommandDefinition { Response = response, Permission = "moderator", Cooldown = 0 } }
                }
            };

            _mockUserRepository.Setup(x => x.GetChatCommandsConfigAsync(userId))
                .ReturnsAsync(config);

            var messageViewer = CreateMessage(command, isMod: false);
            var messageMod = CreateMessage(command, isMod: true);
            string? sentMessage = null;

            // Act - Viewer
            await _handler.ProcessAsync(ToContext(userId, messageViewer), (uid, msg) =>
            {
                sentMessage = msg;
                return Task.CompletedTask;
            });

            // Assert - Viewer
            Assert.Null(sentMessage);

            // Act - Mod
            await _handler.ProcessAsync(ToContext(userId, messageMod), (uid, msg) =>
            {
                sentMessage = msg;
                return Task.CompletedTask;
            });

            // Assert - Mod
            Assert.Equal(response, sentMessage);
        }

        [Fact]
        public async Task ProcessAsync_ShouldRespectCooldown()
        {
            // Arrange
            var userId = "user1";
            var command = "!cooldown";
            var response = "Cooldown Test";
            var config = new ChatCommandConfiguration
            {
                Commands = new Dictionary<string, ChatCommandDefinition>
                {
                    { command, new ChatCommandDefinition { Response = response, Permission = "everyone", Cooldown = 5 } }
                }
            };

            _mockUserRepository.Setup(x => x.GetChatCommandsConfigAsync(userId))
                .ReturnsAsync(config);

            var message = CreateMessage(command);
            int callCount = 0;

            // Act - First Call
            await _handler.ProcessAsync(ToContext(userId, message), (uid, msg) =>
            {
                callCount++;
                return Task.CompletedTask;
            });

            // Act - Second Call (Immediate)
            await _handler.ProcessAsync(ToContext(userId, message), (uid, msg) =>
            {
                callCount++;
                return Task.CompletedTask;
            });

            // Assert
            Assert.Equal(1, callCount); // Should only be called once due to cooldown
        }
    }
}
