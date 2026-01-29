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
    public class GameSwitchServiceHandleGameDetectedMoreTests
    {
        [Fact]
        public async Task HandleGameDetectedAsync_WhenSwitchingGames_ShouldSavePreviousGameCountersIncludingCustomCountersAndCategoryName()
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

            gameContextRepository
                .Setup(r => r.GetAsync("user1"))
                .ReturnsAsync(new GameContext { UserId = "user1", ActiveGameId = "oldGame", ActiveGameName = "Old Category" });

            userRepository.Setup(r => r.GetUserAsync("user1")).ReturnsAsync(new User
            {
                TwitchUserId = "user1",
                Username = "user1",
                DisplayName = "User1",
                OverlaySettings = new OverlaySettings { Counters = new OverlayCounters() }
            });

            userRepository.Setup(r => r.GetChatCommandsConfigAsync("user1")).ReturnsAsync(new ChatCommandConfiguration());
            userRepository.Setup(r => r.SaveChatCommandsConfigAsync("user1", It.IsAny<ChatCommandConfiguration>())).Returns(Task.CompletedTask);
            userRepository.Setup(r => r.SaveUserAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

            counterRepository
                .Setup(r => r.GetCountersAsync("user1"))
                .ReturnsAsync(new Counter
                {
                    TwitchUserId = "user1",
                    Deaths = 3,
                    CustomCounters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["kills"] = 5,
                        ["assists"] = 2
                    }
                });

            counterRepository.Setup(r => r.GetCustomCountersConfigAsync("user1")).ReturnsAsync(new CustomCounterConfiguration());
            counterRepository.Setup(r => r.SaveCustomCountersConfigAsync("user1", It.IsAny<CustomCounterConfiguration>())).Returns(Task.CompletedTask);
            counterRepository.Setup(r => r.SaveCountersAsync(It.IsAny<Counter>())).Returns(Task.CompletedTask);

            gameLibraryRepository.Setup(r => r.GetAsync("user1", "newGame")).ReturnsAsync((GameLibraryItem?)null);
            gameLibraryRepository.Setup(r => r.UpsertAsync(It.IsAny<GameLibraryItem>())).Returns(Task.CompletedTask);

            Counter? savedPrevious = null;
            gameCountersRepository
                .Setup(r => r.SaveAsync("user1", "oldGame", It.IsAny<Counter>()))
                .Callback<string, string, Counter>((_, __, c) => savedPrevious = c)
                .Returns(Task.CompletedTask);

            gameCountersRepository.Setup(r => r.GetAsync("user1", "newGame")).ReturnsAsync((Counter?)null);

            gameChatCommandsRepository.Setup(r => r.GetAsync("user1", "newGame")).ReturnsAsync((ChatCommandConfiguration?)null);
            gameChatCommandsRepository.Setup(r => r.SaveAsync("user1", "newGame", It.IsAny<ChatCommandConfiguration>())).Returns(Task.CompletedTask);

            gameCustomCountersConfigRepository.Setup(r => r.GetAsync("user1", "newGame")).ReturnsAsync((CustomCounterConfiguration?)null);
            CustomCounterConfiguration? seededCustomCountersConfig = null;
            gameCustomCountersConfigRepository
                .Setup(r => r.SaveAsync("user1", "newGame", It.IsAny<CustomCounterConfiguration>()))
                .Callback<string, string, CustomCounterConfiguration>((_, __, cfg) => seededCustomCountersConfig = cfg)
                .Returns(Task.CompletedTask);

            gameCoreCountersConfigRepository.Setup(r => r.GetAsync("user1", "newGame")).ReturnsAsync((GameCoreCountersConfig?)null);
            gameCoreCountersConfigRepository.Setup(r => r.SaveAsync("user1", "newGame", It.IsAny<GameCoreCountersConfig>())).Returns(Task.CompletedTask);

            gameContextRepository.Setup(r => r.SaveAsync(It.IsAny<GameContext>())).Returns(Task.CompletedTask);

            overlayNotifier.Setup(n => n.NotifySettingsUpdateAsync("user1", It.IsAny<OverlaySettings>())).Returns(Task.CompletedTask);
            overlayNotifier.Setup(n => n.NotifyCustomAlertAsync("user1", It.IsAny<string>(), It.IsAny<object>())).Returns(Task.CompletedTask);
            overlayNotifier.Setup(n => n.NotifyCounterUpdateAsync("user1", It.IsAny<Counter>())).Returns(Task.CompletedTask);

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

            await service.HandleGameDetectedAsync("user1", "newGame", "New Category");

            gameCountersRepository.Verify(r => r.SaveAsync("user1", "oldGame", It.IsAny<Counter>()), Times.Once);
            Assert.NotNull(savedPrevious);
            Assert.Equal("Old Category", savedPrevious!.LastCategoryName);
            Assert.Equal(5, savedPrevious.CustomCounters["kills"]);
            Assert.Equal(2, savedPrevious.CustomCounters["assists"]);

            Assert.NotNull(seededCustomCountersConfig);
            Assert.NotNull(seededCustomCountersConfig!.Counters);
            Assert.Empty(seededCustomCountersConfig.Counters);
        }

        [Fact]
        public async Task HandleGameDetectedAsync_WhenSameGameAlreadyActive_ShouldEnsureLibraryAndSeedCoreSelectionIfMissing()
        {
            var gameContextRepository = new Mock<IGameContextRepository>(MockBehavior.Strict);
            var gameCountersRepository = new Mock<IGameCountersRepository>(MockBehavior.Strict);
            var gameLibraryRepository = new Mock<IGameLibraryRepository>(MockBehavior.Strict);
            var gameChatCommandsRepository = new Mock<IGameChatCommandsRepository>(MockBehavior.Strict);
            var gameCustomCountersConfigRepository = new Mock<IGameCustomCountersConfigRepository>(MockBehavior.Strict);
            var gameCoreCountersConfigRepository = new Mock<IGameCoreCountersConfigRepository>(MockBehavior.Strict);
            var counterRepository = new Mock<ICounterRepository>(MockBehavior.Strict);
            var userRepository = new Mock<IUserRepository>(MockBehavior.Strict);
            var twitchApiService = new Mock<ITwitchApiService>(MockBehavior.Strict);
            var overlayNotifier = new Mock<IOverlayNotifier>(MockBehavior.Strict);
            var logger = new Mock<ILogger<GameSwitchService>>();

            gameContextRepository
                .Setup(r => r.GetAsync("user1"))
                .ReturnsAsync(new GameContext { UserId = "user1", ActiveGameId = "GAME1", ActiveGameName = "Test Game" });

            gameLibraryRepository
                .Setup(r => r.GetAsync("user1", "game1"))
                .ReturnsAsync((GameLibraryItem?)null);

            gameLibraryRepository
                .Setup(r => r.UpsertAsync(It.IsAny<GameLibraryItem>()))
                .Returns(Task.CompletedTask);

            GameCoreCountersConfig? seededSelection = null;
            gameCoreCountersConfigRepository
                .SetupSequence(r => r.GetAsync("user1", "game1"))
                .ReturnsAsync((GameCoreCountersConfig?)null)
                .ReturnsAsync(() => seededSelection);

            gameCoreCountersConfigRepository
                .Setup(r => r.SaveAsync("user1", "game1", It.IsAny<GameCoreCountersConfig>()))
                .Callback<string, string, GameCoreCountersConfig>((_, __, cfg) => seededSelection = cfg)
                .Returns(Task.CompletedTask);

            var user = new User
            {
                TwitchUserId = "user1",
                Username = "user1",
                DisplayName = "User1",
                OverlaySettings = new OverlaySettings
                {
                    Counters = new OverlayCounters { Deaths = true, Swears = true, Screams = true, Bits = false }
                }
            };

            userRepository.Setup(r => r.GetUserAsync("user1")).ReturnsAsync(user);
            userRepository.Setup(r => r.SaveUserAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

            userRepository.Setup(r => r.GetChatCommandsConfigAsync("user1")).ReturnsAsync(new ChatCommandConfiguration());
            userRepository.Setup(r => r.SaveChatCommandsConfigAsync("user1", It.IsAny<ChatCommandConfiguration>())).Returns(Task.CompletedTask);

            overlayNotifier.Setup(n => n.NotifySettingsUpdateAsync("user1", It.IsAny<OverlaySettings>())).Returns(Task.CompletedTask);
            overlayNotifier.Setup(n => n.NotifyCustomAlertAsync("user1", It.IsAny<string>(), It.IsAny<object>())).Returns(Task.CompletedTask);

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

            await service.HandleGameDetectedAsync("user1", "game1", "Test Game");

            gameContextRepository.Verify(r => r.GetAsync("user1"), Times.Exactly(2));
            gameLibraryRepository.Verify(r => r.UpsertAsync(It.IsAny<GameLibraryItem>()), Times.Once);
            gameCoreCountersConfigRepository.Verify(r => r.SaveAsync("user1", "game1", It.IsAny<GameCoreCountersConfig>()), Times.Once);
            overlayNotifier.Verify(n => n.NotifySettingsUpdateAsync("user1", It.IsAny<OverlaySettings>()), Times.Once);
            overlayNotifier.Verify(n => n.NotifyCustomAlertAsync("user1", "chatCommandsUpdated", It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public async Task HandleGameDetectedAsync_WhenSameGameAlreadyActiveAndAlreadyConfigured_ShouldNoOp()
        {
            var gameContextRepository = new Mock<IGameContextRepository>(MockBehavior.Strict);
            var gameCountersRepository = new Mock<IGameCountersRepository>(MockBehavior.Strict);
            var gameLibraryRepository = new Mock<IGameLibraryRepository>(MockBehavior.Strict);
            var gameChatCommandsRepository = new Mock<IGameChatCommandsRepository>(MockBehavior.Strict);
            var gameCustomCountersConfigRepository = new Mock<IGameCustomCountersConfigRepository>(MockBehavior.Strict);
            var gameCoreCountersConfigRepository = new Mock<IGameCoreCountersConfigRepository>(MockBehavior.Strict);
            var counterRepository = new Mock<ICounterRepository>(MockBehavior.Strict);
            var userRepository = new Mock<IUserRepository>(MockBehavior.Strict);
            var twitchApiService = new Mock<ITwitchApiService>(MockBehavior.Strict);
            var overlayNotifier = new Mock<IOverlayNotifier>(MockBehavior.Strict);
            var logger = new Mock<ILogger<GameSwitchService>>();

            gameContextRepository
                .Setup(r => r.GetAsync("user1"))
                .ReturnsAsync(new GameContext { UserId = "user1", ActiveGameId = "GAME1", ActiveGameName = "Test Game" });

            gameLibraryRepository
                .Setup(r => r.GetAsync("user1", "game1"))
                .ReturnsAsync(new GameLibraryItem { UserId = "global", GameId = "game1", GameName = "Test Game" });

            gameCoreCountersConfigRepository
                .Setup(r => r.GetAsync("user1", "game1"))
                .ReturnsAsync(new GameCoreCountersConfig(
                    UserId: "user1",
                    GameId: "game1",
                    DeathsEnabled: true,
                    SwearsEnabled: true,
                    ScreamsEnabled: true,
                    BitsEnabled: false,
                    UpdatedAt: DateTimeOffset.UtcNow));

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

            await service.HandleGameDetectedAsync("user1", "game1", "Test Game");

            gameContextRepository.Verify(r => r.GetAsync("user1"), Times.Once);
            gameLibraryRepository.Verify(r => r.GetAsync("user1", "game1"), Times.Once);
            gameCoreCountersConfigRepository.Verify(r => r.GetAsync("user1", "game1"), Times.Once);
        }

        [Fact]
        public async Task HandleGameDetectedAsync_WhenGameHasAdminCcls_ShouldApplyThoseCcls()
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

            gameContextRepository.Setup(r => r.GetAsync("user1")).ReturnsAsync((GameContext?)null);

            var user = new User
            {
                TwitchUserId = "user1",
                Username = "user1",
                DisplayName = "User1",
                OverlaySettings = new OverlaySettings { Counters = new OverlayCounters() }
            };

            userRepository.Setup(r => r.GetUserAsync("user1")).ReturnsAsync(user);
            userRepository.Setup(r => r.GetChatCommandsConfigAsync("user1")).ReturnsAsync(new ChatCommandConfiguration());
            userRepository.Setup(r => r.SaveChatCommandsConfigAsync("user1", It.IsAny<ChatCommandConfiguration>())).Returns(Task.CompletedTask);
            userRepository.Setup(r => r.SaveUserAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

            counterRepository.Setup(r => r.GetCustomCountersConfigAsync("user1")).ReturnsAsync(new CustomCounterConfiguration());
            counterRepository.Setup(r => r.SaveCustomCountersConfigAsync("user1", It.IsAny<CustomCounterConfiguration>())).Returns(Task.CompletedTask);
            counterRepository.Setup(r => r.SaveCountersAsync(It.IsAny<Counter>())).Returns(Task.CompletedTask);

            gameLibraryRepository
                .Setup(r => r.GetAsync("user1", "game1"))
                .ReturnsAsync(new GameLibraryItem
                {
                    UserId = "global",
                    GameId = "game1",
                    GameName = "Test Game",
                    EnabledContentClassificationLabels = new List<string> { "Gambling" },
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                    LastSeenAt = DateTimeOffset.UtcNow.AddDays(-1),
                    BoxArtUrl = string.Empty
                });
            gameLibraryRepository.Setup(r => r.UpsertAsync(It.IsAny<GameLibraryItem>())).Returns(Task.CompletedTask);

            gameCountersRepository.Setup(r => r.GetAsync("user1", "game1")).ReturnsAsync((Counter?)null);

            gameChatCommandsRepository.Setup(r => r.GetAsync("user1", "game1")).ReturnsAsync((ChatCommandConfiguration?)null);
            gameChatCommandsRepository.Setup(r => r.SaveAsync("user1", "game1", It.IsAny<ChatCommandConfiguration>())).Returns(Task.CompletedTask);

            gameCustomCountersConfigRepository.Setup(r => r.GetAsync("user1", "game1")).ReturnsAsync((CustomCounterConfiguration?)null);
            gameCustomCountersConfigRepository.Setup(r => r.SaveAsync("user1", "game1", It.IsAny<CustomCounterConfiguration>())).Returns(Task.CompletedTask);

            gameCoreCountersConfigRepository.Setup(r => r.GetAsync("user1", "game1")).ReturnsAsync((GameCoreCountersConfig?)null);
            gameCoreCountersConfigRepository.Setup(r => r.SaveAsync("user1", "game1", It.IsAny<GameCoreCountersConfig>())).Returns(Task.CompletedTask);

            gameContextRepository.Setup(r => r.SaveAsync(It.IsAny<GameContext>())).Returns(Task.CompletedTask);

            overlayNotifier.Setup(n => n.NotifySettingsUpdateAsync("user1", It.IsAny<OverlaySettings>())).Returns(Task.CompletedTask);
            overlayNotifier.Setup(n => n.NotifyCustomAlertAsync("user1", It.IsAny<string>(), It.IsAny<object>())).Returns(Task.CompletedTask);
            overlayNotifier.Setup(n => n.NotifyCounterUpdateAsync("user1", It.IsAny<Counter>())).Returns(Task.CompletedTask);

            List<string>? captured = null;
            twitchApiService
                .Setup(t => t.UpdateChannelInformationAsync("user1", "game1", It.IsAny<IReadOnlyCollection<string>>()))
                .Callback<string, string, IReadOnlyCollection<string>>((_, __, ccls) => captured = new List<string>(ccls))
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

            await service.HandleGameDetectedAsync("user1", "game1", "Test Game");

            Assert.NotNull(captured);
            Assert.Contains("Gambling", captured!);
        }

        [Fact]
        public async Task HandleGameDetectedAsync_WhenNoGameCclsAndNoUserDefault_ShouldNotApplyCcls()
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

            gameContextRepository.Setup(r => r.GetAsync("user1")).ReturnsAsync((GameContext?)null);

            var user = new User
            {
                TwitchUserId = "user1",
                Username = "user1",
                DisplayName = "User1",
                Features = new FeatureFlags(),
                OverlaySettings = new OverlaySettings { Counters = new OverlayCounters() }
            };

            userRepository.Setup(r => r.GetUserAsync("user1")).ReturnsAsync(user);
            userRepository.Setup(r => r.GetChatCommandsConfigAsync("user1")).ReturnsAsync(new ChatCommandConfiguration());
            userRepository.Setup(r => r.SaveChatCommandsConfigAsync("user1", It.IsAny<ChatCommandConfiguration>())).Returns(Task.CompletedTask);
            userRepository.Setup(r => r.SaveUserAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

            counterRepository.Setup(r => r.GetCustomCountersConfigAsync("user1")).ReturnsAsync(new CustomCounterConfiguration());
            counterRepository.Setup(r => r.SaveCustomCountersConfigAsync("user1", It.IsAny<CustomCounterConfiguration>())).Returns(Task.CompletedTask);
            counterRepository.Setup(r => r.SaveCountersAsync(It.IsAny<Counter>())).Returns(Task.CompletedTask);

            gameLibraryRepository
                .Setup(r => r.GetAsync("user1", "game1"))
                .ReturnsAsync(new GameLibraryItem
                {
                    UserId = "global",
                    GameId = "game1",
                    GameName = "Test Game",
                    EnabledContentClassificationLabels = null,
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                    LastSeenAt = DateTimeOffset.UtcNow.AddDays(-1),
                    BoxArtUrl = string.Empty
                });
            gameLibraryRepository.Setup(r => r.UpsertAsync(It.IsAny<GameLibraryItem>())).Returns(Task.CompletedTask);

            gameCountersRepository.Setup(r => r.GetAsync("user1", "game1")).ReturnsAsync((Counter?)null);

            gameChatCommandsRepository.Setup(r => r.GetAsync("user1", "game1")).ReturnsAsync((ChatCommandConfiguration?)null);
            gameChatCommandsRepository.Setup(r => r.SaveAsync("user1", "game1", It.IsAny<ChatCommandConfiguration>())).Returns(Task.CompletedTask);

            gameCustomCountersConfigRepository.Setup(r => r.GetAsync("user1", "game1")).ReturnsAsync((CustomCounterConfiguration?)null);
            gameCustomCountersConfigRepository.Setup(r => r.SaveAsync("user1", "game1", It.IsAny<CustomCounterConfiguration>())).Returns(Task.CompletedTask);

            gameCoreCountersConfigRepository.Setup(r => r.GetAsync("user1", "game1")).ReturnsAsync((GameCoreCountersConfig?)null);
            gameCoreCountersConfigRepository.Setup(r => r.SaveAsync("user1", "game1", It.IsAny<GameCoreCountersConfig>())).Returns(Task.CompletedTask);

            gameContextRepository.Setup(r => r.SaveAsync(It.IsAny<GameContext>())).Returns(Task.CompletedTask);

            overlayNotifier.Setup(n => n.NotifySettingsUpdateAsync("user1", It.IsAny<OverlaySettings>())).Returns(Task.CompletedTask);
            overlayNotifier.Setup(n => n.NotifyCustomAlertAsync("user1", It.IsAny<string>(), It.IsAny<object>())).Returns(Task.CompletedTask);
            overlayNotifier.Setup(n => n.NotifyCounterUpdateAsync("user1", It.IsAny<Counter>())).Returns(Task.CompletedTask);

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

            await service.HandleGameDetectedAsync("user1", "game1", "Test Game");

            twitchApiService.Verify(t => t.UpdateChannelInformationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>()), Times.Never);
        }
    }
}
