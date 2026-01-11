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
    public class GameSwitchServiceCclFallbackTests
    {
        [Fact]
        public async Task HandleGameDetectedAsync_WhenGameCclsUnconfigured_ShouldApplyUserDefaultCcls()
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
                Features = new FeatureFlags
                {
                    StreamSettings = new StreamSettings
                    {
                        DefaultContentClassificationLabels = new List<string> { "Gambling" }
                    }
                },
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
    }
}
