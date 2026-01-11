using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Services;
using Xunit;

namespace OmniForge.Tests
{
    public class GameCounterSetupServiceTests
    {
        [Fact]
        public async Task AddLibraryCounterToGameAsync_WhenActiveGame_ShouldSeedConfigsAndNotify()
        {
            var counterLibraryRepository = new Mock<ICounterLibraryRepository>();
            var gameCustomCountersConfigRepository = new Mock<IGameCustomCountersConfigRepository>();
            var gameChatCommandsRepository = new Mock<IGameChatCommandsRepository>();
            var gameCountersRepository = new Mock<IGameCountersRepository>();
            var gameContextRepository = new Mock<IGameContextRepository>();
            var userRepository = new Mock<IUserRepository>();
            var counterRepository = new Mock<ICounterRepository>();
            var overlayNotifier = new Mock<IOverlayNotifier>();
            var logger = new Mock<ILogger<GameCounterSetupService>>();

            var libraryItem = new CounterLibraryItem
            {
                CounterId = "kills",
                Name = "Kills",
                Icon = "skull",
                IncrementBy = 2,
                DecrementBy = 3,
                Milestones = new[] { 5, 10 },
                LongCommand = "!Kills",
                AliasCommand = "k"
            };

            counterLibraryRepository
                .Setup(r => r.GetAsync("kills"))
                .ReturnsAsync(libraryItem);

            gameCustomCountersConfigRepository
                .Setup(r => r.GetAsync("user1", "game1"))
                .ReturnsAsync((CustomCounterConfiguration?)null);

            CustomCounterConfiguration? savedCustomConfig = null;
            gameCustomCountersConfigRepository
                .Setup(r => r.SaveAsync("user1", "game1", It.IsAny<CustomCounterConfiguration>()))
                .Callback<string, string, CustomCounterConfiguration>((_, __, cfg) => savedCustomConfig = cfg)
                .Returns(Task.CompletedTask);

            gameChatCommandsRepository
                .Setup(r => r.GetAsync("user1", "game1"))
                .ReturnsAsync((ChatCommandConfiguration?)null);

            ChatCommandConfiguration? savedChatConfig = null;
            gameChatCommandsRepository
                .Setup(r => r.SaveAsync("user1", "game1", It.IsAny<ChatCommandConfiguration>()))
                .Callback<string, string, ChatCommandConfiguration>((_, __, cfg) => savedChatConfig = cfg)
                .Returns(Task.CompletedTask);

            gameCountersRepository
                .Setup(r => r.GetAsync("user1", "game1"))
                .ReturnsAsync((Counter?)null);

            Counter? savedCounters = null;
            gameCountersRepository
                .Setup(r => r.SaveAsync("user1", "game1", It.IsAny<Counter>()))
                .Callback<string, string, Counter>((_, __, c) => savedCounters = c)
                .Returns(Task.CompletedTask);

            gameContextRepository
                .Setup(r => r.GetAsync("user1"))
                .ReturnsAsync(new GameContext { UserId = "user1", ActiveGameId = "game1" });

            counterRepository
                .Setup(r => r.SaveCustomCountersConfigAsync("user1", It.IsAny<CustomCounterConfiguration>()))
                .Returns(Task.CompletedTask);

            userRepository
                .Setup(r => r.SaveChatCommandsConfigAsync("user1", It.IsAny<ChatCommandConfiguration>()))
                .Returns(Task.CompletedTask);

            overlayNotifier
                .Setup(n => n.NotifyCustomAlertAsync("user1", It.IsAny<string>(), It.IsAny<object>()))
                .Returns(Task.CompletedTask);

            var service = new GameCounterSetupService(
                counterLibraryRepository.Object,
                gameCustomCountersConfigRepository.Object,
                gameChatCommandsRepository.Object,
                gameCountersRepository.Object,
                gameContextRepository.Object,
                userRepository.Object,
                counterRepository.Object,
                overlayNotifier.Object,
                logger.Object);

            await service.AddLibraryCounterToGameAsync("user1", "game1", "kills");

            Assert.NotNull(savedCustomConfig);
            Assert.NotNull(savedCustomConfig!.Counters);
            Assert.True(savedCustomConfig.Counters.TryGetValue("kills", out var kills));
            Assert.Equal("Kills", kills.Name);
            Assert.Equal(2, kills.IncrementBy);
            Assert.Equal(3, kills.DecrementBy);

            Assert.NotNull(savedChatConfig);
            Assert.NotNull(savedChatConfig!.Commands);
            Assert.True(savedChatConfig.Commands.ContainsKey("!kills"));
            Assert.True(savedChatConfig.Commands.ContainsKey("!kills+"));
            Assert.True(savedChatConfig.Commands.ContainsKey("!kills-"));
            Assert.True(savedChatConfig.Commands.ContainsKey("!k"));
            Assert.True(savedChatConfig.Commands.ContainsKey("!k+"));
            Assert.True(savedChatConfig.Commands.ContainsKey("!k-"));

            Assert.NotNull(savedCounters);
            Assert.NotNull(savedCounters!.CustomCounters);
            Assert.True(savedCounters.CustomCounters.TryGetValue("kills", out var killCount));
            Assert.Equal(0, killCount);

            counterRepository.Verify(r => r.SaveCustomCountersConfigAsync("user1", It.IsAny<CustomCounterConfiguration>()), Times.Once);
            userRepository.Verify(r => r.SaveChatCommandsConfigAsync("user1", It.IsAny<ChatCommandConfiguration>()), Times.Once);
            overlayNotifier.Verify(n => n.NotifyCustomAlertAsync("user1", "customCountersUpdated", It.IsAny<object>()), Times.Once);
            overlayNotifier.Verify(n => n.NotifyCustomAlertAsync("user1", "chatCommandsUpdated", It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public async Task RemoveLibraryCounterFromGameAsync_WhenActiveGame_ShouldRemoveConfigsAndNotify()
        {
            var counterLibraryRepository = new Mock<ICounterLibraryRepository>();
            var gameCustomCountersConfigRepository = new Mock<IGameCustomCountersConfigRepository>();
            var gameChatCommandsRepository = new Mock<IGameChatCommandsRepository>();
            var gameCountersRepository = new Mock<IGameCountersRepository>();
            var gameContextRepository = new Mock<IGameContextRepository>();
            var userRepository = new Mock<IUserRepository>();
            var counterRepository = new Mock<ICounterRepository>();
            var overlayNotifier = new Mock<IOverlayNotifier>();
            var logger = new Mock<ILogger<GameCounterSetupService>>();

            var libraryItem = new CounterLibraryItem
            {
                CounterId = "kills",
                Name = "Kills",
                LongCommand = "Kills",
                AliasCommand = "!k"
            };

            counterLibraryRepository
                .Setup(r => r.GetAsync("kills"))
                .ReturnsAsync(libraryItem);

            gameCustomCountersConfigRepository
                .Setup(r => r.GetAsync("user1", "game1"))
                .ReturnsAsync(new CustomCounterConfiguration
                {
                    Counters = new Dictionary<string, CustomCounterDefinition>
                    {
                        ["kills"] = new CustomCounterDefinition { Name = "Kills" }
                    }
                });

            CustomCounterConfiguration? savedCustomConfig = null;
            gameCustomCountersConfigRepository
                .Setup(r => r.SaveAsync("user1", "game1", It.IsAny<CustomCounterConfiguration>()))
                .Callback<string, string, CustomCounterConfiguration>((_, __, cfg) => savedCustomConfig = cfg)
                .Returns(Task.CompletedTask);

            gameChatCommandsRepository
                .Setup(r => r.GetAsync("user1", "game1"))
                .ReturnsAsync(new ChatCommandConfiguration
                {
                    Commands = new Dictionary<string, ChatCommandDefinition>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["!kills"] = new ChatCommandDefinition { Response = "Kills" },
                        ["!kills+"] = new ChatCommandDefinition { Action = "increment", Counter = "kills" },
                        ["!kills-"] = new ChatCommandDefinition { Action = "decrement", Counter = "kills" },
                        ["!k"] = new ChatCommandDefinition { Response = "Kills" },
                        ["!k+"] = new ChatCommandDefinition { Action = "increment", Counter = "kills" },
                        ["!k-"] = new ChatCommandDefinition { Action = "decrement", Counter = "kills" },
                        // Also remove commands that target the counter even if the key name differs.
                        ["!alias+"] = new ChatCommandDefinition { Action = "increment", Counter = "kills" },
                        ["!other"] = new ChatCommandDefinition { Response = "leave alone" }
                    }
                });

            ChatCommandConfiguration? savedChatConfig = null;
            gameChatCommandsRepository
                .Setup(r => r.SaveAsync("user1", "game1", It.IsAny<ChatCommandConfiguration>()))
                .Callback<string, string, ChatCommandConfiguration>((_, __, cfg) => savedChatConfig = cfg)
                .Returns(Task.CompletedTask);

            gameCountersRepository
                .Setup(r => r.GetAsync("user1", "game1"))
                .ReturnsAsync(new Counter
                {
                    TwitchUserId = "user1",
                    CustomCounters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["kills"] = 12
                    }
                });

            Counter? savedCounters = null;
            gameCountersRepository
                .Setup(r => r.SaveAsync("user1", "game1", It.IsAny<Counter>()))
                .Callback<string, string, Counter>((_, __, c) => savedCounters = c)
                .Returns(Task.CompletedTask);

            gameContextRepository
                .Setup(r => r.GetAsync("user1"))
                .ReturnsAsync(new GameContext { UserId = "user1", ActiveGameId = "game1" });

            counterRepository
                .Setup(r => r.SaveCustomCountersConfigAsync("user1", It.IsAny<CustomCounterConfiguration>()))
                .Returns(Task.CompletedTask);

            userRepository
                .Setup(r => r.SaveChatCommandsConfigAsync("user1", It.IsAny<ChatCommandConfiguration>()))
                .Returns(Task.CompletedTask);

            overlayNotifier
                .Setup(n => n.NotifyCustomAlertAsync("user1", It.IsAny<string>(), It.IsAny<object>()))
                .Returns(Task.CompletedTask);

            var service = new GameCounterSetupService(
                counterLibraryRepository.Object,
                gameCustomCountersConfigRepository.Object,
                gameChatCommandsRepository.Object,
                gameCountersRepository.Object,
                gameContextRepository.Object,
                userRepository.Object,
                counterRepository.Object,
                overlayNotifier.Object,
                logger.Object);

            await service.RemoveLibraryCounterFromGameAsync("user1", "game1", "kills");

            Assert.NotNull(savedCustomConfig);
            Assert.NotNull(savedCustomConfig!.Counters);
            Assert.False(savedCustomConfig.Counters.ContainsKey("kills"));

            Assert.NotNull(savedChatConfig);
            Assert.NotNull(savedChatConfig!.Commands);
            Assert.False(savedChatConfig.Commands.ContainsKey("!kills"));
            Assert.False(savedChatConfig.Commands.ContainsKey("!kills+"));
            Assert.False(savedChatConfig.Commands.ContainsKey("!kills-"));
            Assert.False(savedChatConfig.Commands.ContainsKey("!k"));
            Assert.False(savedChatConfig.Commands.ContainsKey("!k+"));
            Assert.False(savedChatConfig.Commands.ContainsKey("!k-"));
            Assert.False(savedChatConfig.Commands.ContainsKey("!alias+"));
            Assert.True(savedChatConfig.Commands.ContainsKey("!other"));

            Assert.NotNull(savedCounters);
            Assert.NotNull(savedCounters!.CustomCounters);
            Assert.False(savedCounters.CustomCounters.ContainsKey("kills"));

            counterRepository.Verify(r => r.SaveCustomCountersConfigAsync("user1", It.IsAny<CustomCounterConfiguration>()), Times.Once);
            userRepository.Verify(r => r.SaveChatCommandsConfigAsync("user1", It.IsAny<ChatCommandConfiguration>()), Times.Once);
            overlayNotifier.Verify(n => n.NotifyCustomAlertAsync("user1", "customCountersUpdated", It.IsAny<object>()), Times.Once);
            overlayNotifier.Verify(n => n.NotifyCustomAlertAsync("user1", "chatCommandsUpdated", It.IsAny<object>()), Times.Once);
        }
    }
}
