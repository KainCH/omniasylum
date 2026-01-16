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
    public class GameSwitchServiceHandleGameDetectedPreviousGameTests
    {
        [Fact]
        public async Task HandleGameDetectedAsync_WhenSwitchingFromExistingGame_ShouldPersistPreviousGameStateAndSeedNewGameConfigs()
        {
            var gameContextRepository = new Mock<IGameContextRepository>();
            var gameCountersRepository = new Mock<IGameCountersRepository>();
            var gameLibraryRepository = new Mock<IGameLibraryRepository>();
            var gameChatCommandsRepository = new Mock<IGameChatCommandsRepository>();
            var gameCustomCountersConfigRepository = new Mock<IGameCustomCountersConfigRepository>();
            var gameCoreCountersConfigRepository = new Mock<IGameCoreCountersConfigRepository>();
            var counterRepository = new Mock<ICounterRepository>();
            var userRepository = new Mock<IUserRepository>();
            var twitchApiService = new Mock<ITwitchApiService>();
            var overlayNotifier = new Mock<IOverlayNotifier>();
            var logger = new Mock<ILogger<GameSwitchService>>();

            var userId = "user1";
            var oldGameId = "game-old";
            var newGameId = "game-new";

            gameContextRepository
                .Setup(r => r.GetAsync(userId))
                .ReturnsAsync(new GameContext { UserId = userId, ActiveGameId = oldGameId, ActiveGameName = "Old Game" });

            var user = new User
            {
                TwitchUserId = userId,
                Username = userId,
                DisplayName = "User1",
                OverlaySettings = new OverlaySettings
                {
                    Counters = new OverlayCounters
                    {
                        Deaths = true,
                        Swears = false,
                        Screams = true,
                        Bits = false
                    }
                }
            };

            userRepository.Setup(r => r.GetUserAsync(userId)).ReturnsAsync(user);
            userRepository.Setup(r => r.SaveUserAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

            var activeChatCommands = new ChatCommandConfiguration
            {
                Commands = new Dictionary<string, ChatCommandDefinition>(StringComparer.OrdinalIgnoreCase)
                {
                    ["!swears"] = new ChatCommandDefinition { Enabled = false }
                }
            };

            userRepository
                .Setup(r => r.GetChatCommandsConfigAsync(userId))
                .ReturnsAsync(activeChatCommands);

            userRepository
                .Setup(r => r.SaveChatCommandsConfigAsync(userId, It.IsAny<ChatCommandConfiguration>()))
                .Returns(Task.CompletedTask);

            var activeCustomCounters = new CustomCounterConfiguration();
            counterRepository
                .Setup(r => r.GetCustomCountersConfigAsync(userId))
                .ReturnsAsync(activeCustomCounters);

            counterRepository
                .Setup(r => r.SaveCustomCountersConfigAsync(userId, It.IsAny<CustomCounterConfiguration>()))
                .Returns(Task.CompletedTask);

            var existingActiveCounters = new Counter { TwitchUserId = userId, Deaths = 3, Swears = 2, Screams = 1, Bits = 0 };
            counterRepository
                .Setup(r => r.GetCountersAsync(userId))
                .ReturnsAsync(existingActiveCounters);

            counterRepository
                .Setup(r => r.SaveCountersAsync(It.IsAny<Counter>()))
                .Returns(Task.CompletedTask);

            // Previous-game persistence
            gameCountersRepository
                .Setup(r => r.SaveAsync(userId, oldGameId, It.IsAny<Counter>()))
                .Returns(Task.CompletedTask);

            gameChatCommandsRepository
                .Setup(r => r.SaveAsync(userId, oldGameId, It.IsAny<ChatCommandConfiguration>()))
                .Returns(Task.CompletedTask);

            gameCustomCountersConfigRepository
                .Setup(r => r.SaveAsync(userId, oldGameId, It.IsAny<CustomCounterConfiguration>()))
                .Returns(Task.CompletedTask);

            gameCoreCountersConfigRepository
                .Setup(r => r.SaveAsync(userId, oldGameId, It.IsAny<GameCoreCountersConfig>()))
                .Returns(Task.CompletedTask);

            // Library upsert
            var createdAt = DateTimeOffset.UtcNow.AddDays(-7);
            gameLibraryRepository
                .Setup(r => r.GetAsync(userId, newGameId))
                .ReturnsAsync(new GameLibraryItem
                {
                    UserId = userId,
                    GameId = newGameId,
                    GameName = "New Game",
                    CreatedAt = createdAt,
                    LastSeenAt = createdAt,
                    EnabledContentClassificationLabels = new List<string> { "Gambling", "Violence" },
                    BoxArtUrl = ""
                });

            GameLibraryItem? upserted = null;
            gameLibraryRepository
                .Setup(r => r.UpsertAsync(It.IsAny<GameLibraryItem>()))
                .Callback<GameLibraryItem>(item => upserted = item)
                .Returns(Task.CompletedTask);

            // New-game counters/config seeding
            gameCountersRepository.Setup(r => r.GetAsync(userId, newGameId)).ReturnsAsync((Counter?)null);

            gameChatCommandsRepository.Setup(r => r.GetAsync(userId, newGameId)).ReturnsAsync((ChatCommandConfiguration?)null);
            gameChatCommandsRepository.Setup(r => r.SaveAsync(userId, newGameId, It.IsAny<ChatCommandConfiguration>())).Returns(Task.CompletedTask);

            gameCustomCountersConfigRepository.Setup(r => r.GetAsync(userId, newGameId)).ReturnsAsync((CustomCounterConfiguration?)null);
            CustomCounterConfiguration? seededCustomCountersConfig = null;
            gameCustomCountersConfigRepository
                .Setup(r => r.SaveAsync(userId, newGameId, It.IsAny<CustomCounterConfiguration>()))
                .Callback<string, string, CustomCounterConfiguration>((_, __, cfg) => seededCustomCountersConfig = cfg)
                .Returns(Task.CompletedTask);

            gameCoreCountersConfigRepository.Setup(r => r.GetAsync(userId, newGameId)).ReturnsAsync((GameCoreCountersConfig?)null);
            GameCoreCountersConfig? seededCoreSelection = null;
            gameCoreCountersConfigRepository
                .Setup(r => r.SaveAsync(userId, newGameId, It.IsAny<GameCoreCountersConfig>()))
                .Callback<string, string, GameCoreCountersConfig>((_, __, cfg) => seededCoreSelection = cfg)
                .Returns(Task.CompletedTask);

            gameContextRepository.Setup(r => r.SaveAsync(It.IsAny<GameContext>())).Returns(Task.CompletedTask);

            overlayNotifier.Setup(n => n.NotifySettingsUpdateAsync(userId, It.IsAny<OverlaySettings>())).Returns(Task.CompletedTask);
            overlayNotifier.Setup(n => n.NotifyCustomAlertAsync(userId, It.IsAny<string>(), It.IsAny<object>())).Returns(Task.CompletedTask);
            overlayNotifier.Setup(n => n.NotifyCounterUpdateAsync(userId, It.IsAny<Counter>())).Returns(Task.CompletedTask);

            List<string>? appliedCcls = null;
            twitchApiService
                .Setup(t => t.UpdateChannelInformationAsync(userId, newGameId, It.IsAny<IReadOnlyCollection<string>>()))
                .Callback<string, string, IReadOnlyCollection<string>>((_, __, ccls) => appliedCcls = new List<string>(ccls))
                .Returns(Task.CompletedTask);

            var service = new GameSwitchService(
                gameContextRepository.Object,
                gameCountersRepository.Object,
                gameLibraryRepository.Object,
                gameChatCommandsRepository.Object,
                gameCustomCountersConfigRepository.Object,
                gameCoreCountersConfigRepository.Object,
                counterRepository.Object,
                userRepository.Object,
                twitchApiService.Object,
                overlayNotifier.Object,
                logger.Object);

            await service.HandleGameDetectedAsync(userId, newGameId, "New Game", boxArtUrl: "http://boxart");

            // Previous game persisted
            gameCountersRepository.Verify(r => r.SaveAsync(userId, oldGameId, It.IsAny<Counter>()), Times.Once);
            gameChatCommandsRepository.Verify(r => r.SaveAsync(userId, oldGameId, It.IsAny<ChatCommandConfiguration>()), Times.Once);
            gameCustomCountersConfigRepository.Verify(r => r.SaveAsync(userId, oldGameId, It.IsAny<CustomCounterConfiguration>()), Times.Once);
            gameCoreCountersConfigRepository.Verify(r => r.SaveAsync(userId, oldGameId, It.IsAny<GameCoreCountersConfig>()), Times.Once);

            Assert.NotNull(upserted);
            Assert.Equal(createdAt, upserted!.CreatedAt);
            Assert.Equal(newGameId, upserted.GameId);
            Assert.Equal("http://boxart", upserted.BoxArtUrl);

            Assert.NotNull(seededCoreSelection);
            Assert.True(seededCoreSelection!.DeathsEnabled);
            Assert.False(seededCoreSelection.SwearsEnabled);
            Assert.True(seededCoreSelection.ScreamsEnabled);
            Assert.False(seededCoreSelection.BitsEnabled);

            Assert.NotNull(seededCustomCountersConfig);
            Assert.NotNull(seededCustomCountersConfig!.Counters);
            Assert.Empty(seededCustomCountersConfig.Counters);

            Assert.NotNull(appliedCcls);
            Assert.Contains("Gambling", appliedCcls!);
            Assert.Contains("Violence", appliedCcls!);

            overlayNotifier.Verify(n => n.NotifyCustomAlertAsync(userId, "chatCommandsUpdated", It.IsAny<object>()), Times.Once);
            overlayNotifier.Verify(n => n.NotifyCustomAlertAsync(userId, "customCountersUpdated", It.IsAny<object>()), Times.Once);
            overlayNotifier.Verify(n => n.NotifyCounterUpdateAsync(userId, It.IsAny<Counter>()), Times.Once);
        }
    }
}
