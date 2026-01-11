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
    public class GameSwitchServiceCoreSelectionTests
    {
        [Fact]
        public async Task ApplyActiveCoreCountersSelectionAsync_WhenActiveGameAndSelectionExists_ShouldUpdateOverlayAndChatOverrides()
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
                .ReturnsAsync(new GameContext { UserId = "user1", ActiveGameId = "game1" });

            var selection = new GameCoreCountersConfig(
                UserId: "user1",
                GameId: "game1",
                DeathsEnabled: false,
                SwearsEnabled: true,
                ScreamsEnabled: false,
                BitsEnabled: true,
                UpdatedAt: DateTimeOffset.UtcNow);

            gameCoreCountersConfigRepository
                .Setup(r => r.GetAsync("user1", "game1"))
                .ReturnsAsync(selection);

            var user = new User
            {
                TwitchUserId = "user1",
                Username = "user1",
                OverlaySettings = new OverlaySettings { Counters = new OverlayCounters() }
            };

            userRepository
                .Setup(r => r.GetUserAsync("user1"))
                .ReturnsAsync(user);

            userRepository
                .Setup(r => r.SaveUserAsync(It.IsAny<User>()))
                .Returns(Task.CompletedTask);

            overlayNotifier
                .Setup(n => n.NotifySettingsUpdateAsync("user1", It.IsAny<OverlaySettings>()))
                .Returns(Task.CompletedTask);

            var activeChat = new ChatCommandConfiguration
            {
                Commands = new Dictionary<string, ChatCommandDefinition>(StringComparer.OrdinalIgnoreCase)
                {
                    // Pre-existing overrides that should be removed when enabled.
                    ["!swears"] = new ChatCommandDefinition { Enabled = false }
                }
            };

            userRepository
                .Setup(r => r.GetChatCommandsConfigAsync("user1"))
                .ReturnsAsync(activeChat);

            ChatCommandConfiguration? savedChat = null;
            userRepository
                .Setup(r => r.SaveChatCommandsConfigAsync("user1", It.IsAny<ChatCommandConfiguration>()))
                .Callback<string, ChatCommandConfiguration>((_, cfg) => savedChat = cfg)
                .Returns(Task.CompletedTask);

            overlayNotifier
                .Setup(n => n.NotifyCustomAlertAsync("user1", "chatCommandsUpdated", It.IsAny<object>()))
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

            await service.ApplyActiveCoreCountersSelectionAsync("user1", "game1");

            // Overlay counters updated
            userRepository.Verify(r => r.SaveUserAsync(It.Is<User>(u =>
                u.OverlaySettings != null
                && u.OverlaySettings.Counters != null
                && !u.OverlaySettings.Counters.Deaths
                && u.OverlaySettings.Counters.Swears
                && !u.OverlaySettings.Counters.Screams
                && u.OverlaySettings.Counters.Bits)), Times.Once);

            Assert.NotNull(savedChat);
            Assert.NotNull(savedChat!.Commands);

            // Enabled swears should remove override
            Assert.False(savedChat.Commands.ContainsKey("!swears"));

            // Disabled deaths/screams should have explicit overrides
            Assert.True(savedChat.Commands.TryGetValue("!deaths", out var deaths));
            Assert.False(deaths.Enabled);
            Assert.True(savedChat.Commands.TryGetValue("!screams", out var screams));
            Assert.False(screams.Enabled);

            overlayNotifier.Verify(n => n.NotifyCustomAlertAsync("user1", "chatCommandsUpdated", It.IsAny<object>()), Times.Once);
        }
    }
}
