using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Controllers;
using Xunit;

namespace OmniForge.Tests.Controllers
{
    public class CounterControllerTests
    {
        private readonly Mock<ICounterRepository> _mockCounterRepository = new();
        private readonly Mock<IUserRepository> _mockUserRepository = new();
        private readonly Mock<IGameContextRepository> _mockGameContextRepository = new();
        private readonly Mock<IGameCoreCountersConfigRepository> _mockGameCoreCountersConfigRepository = new();
        private readonly Mock<INotificationService> _mockNotificationService = new();
        private readonly Mock<IOverlayNotifier> _mockOverlayNotifier = new();
        private readonly Mock<ILogger<CounterController>> _mockLogger = new();

        private CounterController CreateController(string? userId)
        {
            var controller = new CounterController(
                _mockCounterRepository.Object,
                _mockUserRepository.Object,
                _mockGameContextRepository.Object,
                _mockGameCoreCountersConfigRepository.Object,
                _mockNotificationService.Object,
                _mockOverlayNotifier.Object,
                _mockLogger.Object);

            var httpContext = new DefaultHttpContext();
            if (!string.IsNullOrEmpty(userId))
            {
                httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim("userId", userId)
                }, "test"));
            }

            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            return controller;
        }

        [Fact]
        public async Task GetOverlaySettings_WhenFeatureDisabled_ShouldReturn403()
        {
            var controller = CreateController("user1");

            _mockUserRepository
                .Setup(x => x.GetUserAsync("user1"))
                .ReturnsAsync(new User { TwitchUserId = "user1", Features = new FeatureFlags { StreamOverlay = false } });

            var result = await controller.GetOverlaySettings();

            var obj = Assert.IsType<ObjectResult>(result);
            Assert.Equal(403, obj.StatusCode);
        }

        [Fact]
        public async Task UpdateOverlaySettings_WhenInvalidPosition_ShouldReturnBadRequest()
        {
            var controller = CreateController("user1");

            _mockUserRepository
                .Setup(x => x.GetUserAsync("user1"))
                .ReturnsAsync(new User { TwitchUserId = "user1", Features = new FeatureFlags { StreamOverlay = true } });

            var result = await controller.UpdateOverlaySettings(new OverlaySettings { Position = "middle" });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task UpdateOverlaySettings_WhenActiveGameAndCountersProvided_ShouldPersistCoreSelection()
        {
            var controller = CreateController("user1");

            var user = new User
            {
                TwitchUserId = "user1",
                Features = new FeatureFlags { StreamOverlay = true },
                OverlaySettings = new OverlaySettings()
            };

            _mockUserRepository.Setup(x => x.GetUserAsync("user1")).ReturnsAsync(user);
            _mockUserRepository.Setup(x => x.SaveUserAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

            _mockGameContextRepository
                .Setup(x => x.GetAsync("user1"))
                .ReturnsAsync(new GameContext { UserId = "user1", ActiveGameId = "game1", ActiveGameName = "Test Game" });

            var request = new OverlaySettings
            {
                Position = "top-right",
                Counters = new OverlayCounters { Deaths = true, Swears = false, Screams = true, Bits = true }
            };

            var result = await controller.UpdateOverlaySettings(request);

            Assert.IsType<OkObjectResult>(result);
            _mockGameCoreCountersConfigRepository.Verify(x => x.SaveAsync(
                "user1",
                "game1",
                It.Is<GameCoreCountersConfig>(c => c.UserId == "user1" && c.GameId == "game1" && c.DeathsEnabled && !c.SwearsEnabled && c.ScreamsEnabled && c.BitsEnabled)),
                Times.Once);
        }

        [Fact]
        public async Task GetPublicCounters_WhenPreviewTrueAndStreamStartedNull_ShouldSetStreamStarted()
        {
            var controller = CreateController(null);
            controller.ControllerContext.HttpContext.Request.QueryString = new QueryString("?preview=true");

            _mockCounterRepository
                .Setup(x => x.GetCountersAsync("user1"))
                .ReturnsAsync(new Counter { TwitchUserId = "user1", StreamStarted = null });

            _mockUserRepository
                .Setup(x => x.GetUserAsync("user1"))
                .ReturnsAsync(new User
                {
                    TwitchUserId = "user1",
                    Features = new FeatureFlags { StreamOverlay = true },
                    OverlaySettings = new OverlaySettings { OfflinePreview = false }
                });

            _mockCounterRepository
                .Setup(x => x.GetCustomCountersConfigAsync("user1"))
                .ReturnsAsync(new CustomCounterConfiguration { Counters = new Dictionary<string, CustomCounterDefinition>() });

            var result = await controller.GetPublicCounters("user1");

            var ok = Assert.IsType<OkObjectResult>(result);
            var json = JsonSerializer.Serialize(ok.Value);
            using var doc = JsonDocument.Parse(json);

            Assert.True(doc.RootElement.TryGetProperty("streamStarted", out var streamStarted));
            Assert.Equal(JsonValueKind.String, streamStarted.ValueKind);
        }

        [Fact]
        public async Task IncrementPublicCustomCounter_WhenMilestoneCrossed_ShouldEmitMilestoneAlert()
        {
            var controller = CreateController(null);

            _mockUserRepository
                .Setup(x => x.GetUserAsync("user1"))
                .ReturnsAsync(new User { TwitchUserId = "user1", Features = new FeatureFlags { StreamOverlay = true } });

            var config = new CustomCounterConfiguration
            {
                Counters = new Dictionary<string, CustomCounterDefinition>
                {
                    ["kills"] = new CustomCounterDefinition
                    {
                        Name = "Kills",
                        IncrementBy = 1,
                        Milestones = new List<int> { 5 },
                        Icon = "skull"
                    }
                }
            };

            _mockCounterRepository
                .Setup(x => x.GetCustomCountersConfigAsync("user1"))
                .ReturnsAsync(config);

            _mockCounterRepository
                .Setup(x => x.GetCountersAsync("user1"))
                .ReturnsAsync(new Counter
                {
                    TwitchUserId = "user1",
                    CustomCounters = new Dictionary<string, int> { ["kills"] = 4 }
                });

            _mockCounterRepository
                .Setup(x => x.IncrementCounterAsync("user1", "kills", 1))
                .ReturnsAsync(new Counter
                {
                    TwitchUserId = "user1",
                    CustomCounters = new Dictionary<string, int> { ["kills"] = 5 }
                });

            var result = await controller.IncrementPublicCustomCounter("user1", "kills");

            Assert.IsType<OkObjectResult>(result);
            _mockOverlayNotifier.Verify(x => x.NotifyCounterUpdateAsync("user1", It.IsAny<Counter>()), Times.Once);
            _mockOverlayNotifier.Verify(x => x.NotifyCustomAlertAsync(
                "user1",
                "customMilestoneReached",
                It.IsAny<object>()), Times.Once);
        }
    }
}
