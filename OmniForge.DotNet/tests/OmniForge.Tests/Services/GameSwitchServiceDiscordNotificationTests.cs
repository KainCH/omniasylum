using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Services;
using Xunit;

namespace OmniForge.Tests.Services
{
    public class GameSwitchServiceDiscordNotificationTests
    {
        private static GameSwitchService CreateService(
            Mock<IGameContextRepository>? gameContextRepository = null,
            Mock<IGameCountersRepository>? gameCountersRepository = null,
            Mock<IGameLibraryRepository>? gameLibraryRepository = null,
            Mock<IGameChatCommandsRepository>? gameChatCommandsRepository = null,
            Mock<IGameCustomCountersConfigRepository>? gameCustomCountersConfigRepository = null,
            Mock<IGameCoreCountersConfigRepository>? gameCoreCountersConfigRepository = null,
            Mock<ICounterRepository>? counterRepository = null,
            Mock<ICounterLibraryRepository>? counterLibraryRepository = null,
            Mock<IUserRepository>? userRepository = null,
            Mock<ITwitchApiService>? twitchApiService = null,
            Mock<IOverlayNotifier>? overlayNotifier = null,
            Mock<IDiscordService>? discordService = null,
            Mock<ILogger<GameSwitchService>>? logger = null)
        {
            return new GameSwitchService(
                (gameContextRepository ?? new Mock<IGameContextRepository>()).Object,
                (gameCountersRepository ?? new Mock<IGameCountersRepository>()).Object,
                (gameLibraryRepository ?? new Mock<IGameLibraryRepository>()).Object,
                (gameChatCommandsRepository ?? new Mock<IGameChatCommandsRepository>()).Object,
                (gameCustomCountersConfigRepository ?? new Mock<IGameCustomCountersConfigRepository>()).Object,
                (gameCoreCountersConfigRepository ?? new Mock<IGameCoreCountersConfigRepository>()).Object,
                (counterRepository ?? new Mock<ICounterRepository>()).Object,
                (counterLibraryRepository ?? new Mock<ICounterLibraryRepository>()).Object,
                (userRepository ?? new Mock<IUserRepository>()).Object,
                (twitchApiService ?? new Mock<ITwitchApiService>()).Object,
                (overlayNotifier ?? new Mock<IOverlayNotifier>()).Object,
                (discordService ?? new Mock<IDiscordService>()).Object,
                (logger ?? new Mock<ILogger<GameSwitchService>>()).Object);
        }

        [Fact]
        public async Task HandleGameDetectedAsync_ShouldSendModChannelNotification_OnGameSwitch()
        {
            // Arrange
            var discordService = new Mock<IDiscordService>();
            var userRepository = new Mock<IUserRepository>();
            var counterRepository = new Mock<ICounterRepository>();
            var gameContextRepository = new Mock<IGameContextRepository>();
            var gameCountersRepository = new Mock<IGameCountersRepository>();
            var gameCoreCountersConfigRepository = new Mock<IGameCoreCountersConfigRepository>();

            var user = new User
            {
                TwitchUserId = "user1",
                Username = "testuser",
                DiscordModChannelId = "222222222222222222"
            };
            userRepository.Setup(r => r.GetUserAsync("user1")).ReturnsAsync(user);
            userRepository.Setup(r => r.GetChatCommandsConfigAsync("user1")).ReturnsAsync(new ChatCommandConfiguration());
            userRepository.Setup(r => r.SaveChatCommandsConfigAsync("user1", It.IsAny<ChatCommandConfiguration>())).Returns(Task.CompletedTask);
            userRepository.Setup(r => r.SaveUserAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

            counterRepository.Setup(r => r.GetCountersAsync("user1")).ReturnsAsync(new Counter { TwitchUserId = "user1" });
            counterRepository.Setup(r => r.GetCustomCountersConfigAsync("user1")).ReturnsAsync(new CustomCounterConfiguration());
            counterRepository.Setup(r => r.SaveCountersAsync(It.IsAny<Counter>())).Returns(Task.CompletedTask);
            counterRepository.Setup(r => r.SaveCustomCountersConfigAsync("user1", It.IsAny<CustomCounterConfiguration>())).Returns(Task.CompletedTask);

            // No previous game context — first detection
            gameContextRepository.Setup(r => r.GetAsync("user1")).ReturnsAsync((GameContext?)null);
            gameContextRepository.Setup(r => r.SaveAsync(It.IsAny<GameContext>())).Returns(Task.CompletedTask);

            var service = CreateService(
                gameContextRepository: gameContextRepository,
                gameCountersRepository: gameCountersRepository,
                gameCoreCountersConfigRepository: gameCoreCountersConfigRepository,
                counterRepository: counterRepository,
                userRepository: userRepository,
                discordService: discordService);

            // Act
            await service.HandleGameDetectedAsync("user1", "game1", "Elden Ring", "https://example.com/box.jpg");

            // Allow fire-and-forget to complete
            await Task.Delay(200);

            // Assert — only mod channel fires; public announcement is not GameSwitchService's concern
            discordService.Verify(d => d.SendGameChangeAnnouncementAsync(
                It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
            discordService.Verify(d => d.SendModChannelNotificationAsync(
                user, "Elden Ring", It.IsAny<IReadOnlyList<string>>()), Times.Once);
        }

        [Fact]
        public async Task HandleGameDetectedAsync_ShouldNotSendPublicAnnouncement_OnGameSwitch()
        {
            // Arrange — GameSwitchService never calls SendGameChangeAnnouncementAsync regardless of live status
            var discordService = new Mock<IDiscordService>();
            var userRepository = new Mock<IUserRepository>();
            var counterRepository = new Mock<ICounterRepository>();
            var gameContextRepository = new Mock<IGameContextRepository>();
            var gameCountersRepository = new Mock<IGameCountersRepository>();
            var gameCoreCountersConfigRepository = new Mock<IGameCoreCountersConfigRepository>();

            var user = new User
            {
                TwitchUserId = "user1",
                Username = "testuser",
                DiscordChannelId = "111111111111111111",
                DiscordModChannelId = "222222222222222222"
            };
            userRepository.Setup(r => r.GetUserAsync("user1")).ReturnsAsync(user);
            userRepository.Setup(r => r.GetChatCommandsConfigAsync("user1")).ReturnsAsync(new ChatCommandConfiguration());
            userRepository.Setup(r => r.SaveChatCommandsConfigAsync("user1", It.IsAny<ChatCommandConfiguration>())).Returns(Task.CompletedTask);
            userRepository.Setup(r => r.SaveUserAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

            counterRepository.Setup(r => r.GetCountersAsync("user1")).ReturnsAsync(new Counter { TwitchUserId = "user1" });
            counterRepository.Setup(r => r.GetCustomCountersConfigAsync("user1")).ReturnsAsync(new CustomCounterConfiguration());
            counterRepository.Setup(r => r.SaveCountersAsync(It.IsAny<Counter>())).Returns(Task.CompletedTask);
            counterRepository.Setup(r => r.SaveCustomCountersConfigAsync("user1", It.IsAny<CustomCounterConfiguration>())).Returns(Task.CompletedTask);

            gameContextRepository.Setup(r => r.GetAsync("user1")).ReturnsAsync((GameContext?)null);
            gameContextRepository.Setup(r => r.SaveAsync(It.IsAny<GameContext>())).Returns(Task.CompletedTask);

            var service = CreateService(
                gameContextRepository: gameContextRepository,
                gameCountersRepository: gameCountersRepository,
                gameCoreCountersConfigRepository: gameCoreCountersConfigRepository,
                counterRepository: counterRepository,
                userRepository: userRepository,
                discordService: discordService);

            // Act
            await service.HandleGameDetectedAsync("user1", "game1", "Elden Ring");

            // Allow fire-and-forget to complete
            await Task.Delay(200);

            // Assert — public announcement is never sent by GameSwitchService
            discordService.Verify(d => d.SendGameChangeAnnouncementAsync(
                It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
            discordService.Verify(d => d.SendModChannelNotificationAsync(
                user, "Elden Ring", It.IsAny<IReadOnlyList<string>>()), Times.Once);
        }

        [Fact]
        public async Task HandleGameDetectedAsync_ShouldNotCallDiscord_OnSameGameRedetect()
        {
            // Arrange
            var discordService = new Mock<IDiscordService>();
            var userRepository = new Mock<IUserRepository>();
            var counterRepository = new Mock<ICounterRepository>();
            var gameContextRepository = new Mock<IGameContextRepository>();
            var gameCountersRepository = new Mock<IGameCountersRepository>();
            var gameLibraryRepository = new Mock<IGameLibraryRepository>();
            var gameCoreCountersConfigRepository = new Mock<IGameCoreCountersConfigRepository>();

            // Same game already active
            gameContextRepository.Setup(r => r.GetAsync("user1")).ReturnsAsync(new GameContext
            {
                UserId = "user1",
                ActiveGameId = "game1",
                ActiveGameName = "Elden Ring"
            });

            // Library item exists
            gameLibraryRepository.Setup(r => r.GetAsync("user1", "game1")).ReturnsAsync(new GameLibraryItem
            {
                UserId = "user1",
                GameId = "game1",
                GameName = "Elden Ring"
            });

            // Core selection exists
            gameCoreCountersConfigRepository.Setup(r => r.GetAsync("user1", "game1")).ReturnsAsync(
                new GameCoreCountersConfig("user1", "game1", true, true, true, false, DateTimeOffset.UtcNow));

            counterRepository.Setup(r => r.GetCountersAsync("user1")).ReturnsAsync(new Counter { TwitchUserId = "user1" });
            gameCountersRepository.Setup(r => r.SaveAsync("user1", "game1", It.IsAny<Counter>())).Returns(Task.CompletedTask);

            var service = CreateService(
                gameContextRepository: gameContextRepository,
                gameCountersRepository: gameCountersRepository,
                gameLibraryRepository: gameLibraryRepository,
                gameCoreCountersConfigRepository: gameCoreCountersConfigRepository,
                counterRepository: counterRepository,
                userRepository: userRepository,
                discordService: discordService);

            // Act
            await service.HandleGameDetectedAsync("user1", "game1", "Elden Ring");

            // Allow any fire-and-forget to complete
            await Task.Delay(200);

            // Assert — same-game re-detect (e.g. title change) produces no Discord calls.
            // The mod-channel announcement is StreamOnlineHandler's responsibility, guarded by isNewStream.
            discordService.Verify(d => d.SendGameChangeAnnouncementAsync(
                It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
            discordService.Verify(d => d.SendModChannelNotificationAsync(
                It.IsAny<User>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()), Times.Never);
        }

        [Fact]
        public async Task HandleGameDetectedAsync_DiscordFailure_ShouldNotBlockOverlay()
        {
            // Arrange
            var discordService = new Mock<IDiscordService>();
            var userRepository = new Mock<IUserRepository>();
            var counterRepository = new Mock<ICounterRepository>();
            var gameContextRepository = new Mock<IGameContextRepository>();
            var overlayNotifier = new Mock<IOverlayNotifier>();
            var gameCoreCountersConfigRepository = new Mock<IGameCoreCountersConfigRepository>();

            var user = new User
            {
                TwitchUserId = "user1",
                Username = "testuser",
                DiscordChannelId = "111111111111111111"
            };
            userRepository.Setup(r => r.GetUserAsync("user1")).ReturnsAsync(user);
            userRepository.Setup(r => r.GetChatCommandsConfigAsync("user1")).ReturnsAsync(new ChatCommandConfiguration());
            userRepository.Setup(r => r.SaveChatCommandsConfigAsync("user1", It.IsAny<ChatCommandConfiguration>())).Returns(Task.CompletedTask);
            userRepository.Setup(r => r.SaveUserAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

            counterRepository.Setup(r => r.GetCountersAsync("user1")).ReturnsAsync(new Counter { TwitchUserId = "user1" });
            counterRepository.Setup(r => r.GetCustomCountersConfigAsync("user1")).ReturnsAsync(new CustomCounterConfiguration());
            counterRepository.Setup(r => r.SaveCountersAsync(It.IsAny<Counter>())).Returns(Task.CompletedTask);
            counterRepository.Setup(r => r.SaveCustomCountersConfigAsync("user1", It.IsAny<CustomCounterConfiguration>())).Returns(Task.CompletedTask);

            gameContextRepository.Setup(r => r.GetAsync("user1")).ReturnsAsync((GameContext?)null);
            gameContextRepository.Setup(r => r.SaveAsync(It.IsAny<GameContext>())).Returns(Task.CompletedTask);

            // Discord throws on mod channel
            discordService.Setup(d => d.SendModChannelNotificationAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()))
                .ThrowsAsync(new Exception("Discord API error"));

            var service = CreateService(
                gameContextRepository: gameContextRepository,
                gameCoreCountersConfigRepository: gameCoreCountersConfigRepository,
                counterRepository: counterRepository,
                userRepository: userRepository,
                overlayNotifier: overlayNotifier,
                discordService: discordService);

            // Act — should not throw
            await service.HandleGameDetectedAsync("user1", "game1", "Elden Ring");

            // Assert — overlay notification still happened before Discord
            overlayNotifier.Verify(o => o.NotifyCounterUpdateAsync("user1", It.IsAny<Counter>()), Times.Once);
        }
    }
}
