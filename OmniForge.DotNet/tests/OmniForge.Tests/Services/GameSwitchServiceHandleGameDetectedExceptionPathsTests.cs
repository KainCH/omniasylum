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
    public class GameSwitchServiceHandleGameDetectedExceptionPathsTests
    {
        [Fact]
        public async Task HandleGameDetectedAsync_WhenDependenciesThrow_ShouldStillSaveCountersAndNotify()
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
            var gameId = "game1";

            gameContextRepository.Setup(r => r.GetAsync(userId)).ReturnsAsync((GameContext?)null);

            userRepository.Setup(r => r.GetUserAsync(userId)).ThrowsAsync(new Exception("db read fail"));
            userRepository.Setup(r => r.GetChatCommandsConfigAsync(userId)).ThrowsAsync(new Exception("chat read fail"));
            counterRepository.Setup(r => r.GetCustomCountersConfigAsync(userId)).ThrowsAsync(new Exception("custom counters read fail"));

            // Library reads succeed so CCL apply path runs.
            gameLibraryRepository
                .Setup(r => r.GetAsync(userId, gameId))
                .ReturnsAsync(new GameLibraryItem
                {
                    UserId = userId,
                    GameId = gameId,
                    GameName = "Test Game",
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                    LastSeenAt = DateTimeOffset.UtcNow.AddDays(-1),
                    EnabledContentClassificationLabels = new List<string> { "Gambling" },
                    BoxArtUrl = string.Empty
                });
            gameLibraryRepository.Setup(r => r.UpsertAsync(It.IsAny<GameLibraryItem>())).Returns(Task.CompletedTask);

            // Force some error-handling branches during loads.
            gameCountersRepository.Setup(r => r.GetAsync(userId, gameId)).ThrowsAsync(new Exception("counters load fail"));
            gameChatCommandsRepository.Setup(r => r.GetAsync(userId, gameId)).ThrowsAsync(new Exception("game chat load fail"));
            gameCustomCountersConfigRepository.Setup(r => r.GetAsync(userId, gameId)).ThrowsAsync(new Exception("game custom load fail"));
            gameCoreCountersConfigRepository.Setup(r => r.GetAsync(userId, gameId)).ThrowsAsync(new Exception("core selection load fail"));

            Counter? savedCounters = null;
            counterRepository
                .Setup(r => r.SaveCountersAsync(It.IsAny<Counter>()))
                .Callback<Counter>(c => savedCounters = c)
                .Returns(Task.CompletedTask);

            userRepository.Setup(r => r.SaveChatCommandsConfigAsync(userId, It.IsAny<ChatCommandConfiguration>())).Returns(Task.CompletedTask);
            counterRepository.Setup(r => r.SaveCustomCountersConfigAsync(userId, It.IsAny<CustomCounterConfiguration>())).Returns(Task.CompletedTask);

            overlayNotifier.Setup(n => n.NotifyCustomAlertAsync(userId, It.IsAny<string>(), It.IsAny<object>())).Returns(Task.CompletedTask);
            overlayNotifier.Setup(n => n.NotifyCounterUpdateAsync(userId, It.IsAny<Counter>())).Returns(Task.CompletedTask);

            gameContextRepository.Setup(r => r.SaveAsync(It.IsAny<GameContext>())).Returns(Task.CompletedTask);

            // CCL apply throws, should be caught.
            twitchApiService
                .Setup(t => t.UpdateChannelInformationAsync(userId, gameId, It.IsAny<IReadOnlyCollection<string>>()))
                .ThrowsAsync(new Exception("twitch update failed"));

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

            await service.HandleGameDetectedAsync(userId, gameId, "Test Game");

            Assert.NotNull(savedCounters);
            Assert.Equal(userId, savedCounters!.TwitchUserId);

            overlayNotifier.Verify(n => n.NotifyCounterUpdateAsync(userId, It.IsAny<Counter>()), Times.Once);
            overlayNotifier.Verify(n => n.NotifyCustomAlertAsync(userId, "chatCommandsUpdated", It.IsAny<object>()), Times.Once);
            overlayNotifier.Verify(n => n.NotifyCustomAlertAsync(userId, "customCountersUpdated", It.IsAny<object>()), Times.Once);
        }
    }
}
