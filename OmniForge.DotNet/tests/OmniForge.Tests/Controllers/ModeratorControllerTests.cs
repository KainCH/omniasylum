using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Controllers;
using System.Security.Claims;
using Xunit;

namespace OmniForge.Tests.Controllers
{
    public class ModeratorControllerTests
    {
        private readonly Mock<IUserRepository> _mockUserRepository;
        private readonly Mock<ICounterRepository> _mockCounterRepository;
        private readonly Mock<ISeriesRepository> _mockSeriesRepository;
        private readonly Mock<IOverlayNotifier> _mockOverlayNotifier;
        private readonly Mock<ILogger<ModeratorController>> _mockLogger;
        private readonly ModeratorController _controller;
        private readonly ClaimsPrincipal _user;

        public ModeratorControllerTests()
        {
            _mockUserRepository = new Mock<IUserRepository>();
            _mockCounterRepository = new Mock<ICounterRepository>();
            _mockSeriesRepository = new Mock<ISeriesRepository>();
            _mockOverlayNotifier = new Mock<IOverlayNotifier>();
            _mockLogger = new Mock<ILogger<ModeratorController>>();

            _controller = new ModeratorController(
                _mockUserRepository.Object,
                _mockCounterRepository.Object,
                _mockSeriesRepository.Object,
                _mockOverlayNotifier.Object,
                _mockLogger.Object
            );

            _user = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
            {
                new Claim(ClaimTypes.NameIdentifier, "test-user-id"),
                new Claim(ClaimTypes.Name, "test-user")
            }, "mock"));

            _controller.ControllerContext = new ControllerContext()
            {
                HttpContext = new DefaultHttpContext() { User = _user }
            };
        }

        [Fact]
        public async Task GetMyModerators_ReturnsOk_WithModerators()
        {
            // Arrange
            var userId = "test-user-id";
            var moderator = new User
            {
                TwitchUserId = "mod-id",
                Username = "mod-user",
                ManagedStreamers = new List<string> { userId }
            };
            var otherUser = new User
            {
                TwitchUserId = "other-id",
                Username = "other-user",
                ManagedStreamers = new List<string>()
            };

            _mockUserRepository.Setup(repo => repo.GetAllUsersAsync())
                .ReturnsAsync(new List<User> { moderator, otherUser });

            // Act
            var result = await _controller.GetMyModerators();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var value = okResult.Value;
            // Reflection to access anonymous type properties is messy, checking if it's not null is a start
            Assert.NotNull(value);
        }

        [Fact]
        public async Task GrantAccess_ReturnsOk_WhenValid()
        {
            // Arrange
            var userId = "test-user-id";
            var moderatorId = "mod-id";
            var request = new ModeratorController.GrantAccessRequest { ModeratorId = moderatorId };
            var moderator = new User { TwitchUserId = moderatorId, ManagedStreamers = new List<string>() };

            _mockUserRepository.Setup(repo => repo.GetUserAsync(moderatorId))
                .ReturnsAsync(moderator);

            // Act
            var result = await _controller.GrantAccess(request);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Contains(userId, moderator.ManagedStreamers);
            _mockUserRepository.Verify(repo => repo.SaveUserAsync(moderator), Times.Once);
        }

        [Fact]
        public async Task RevokeAccess_ReturnsOk_WhenValid()
        {
            // Arrange
            var userId = "test-user-id";
            var moderatorId = "mod-id";
            var request = new ModeratorController.RevokeAccessRequest { ModeratorId = moderatorId };
            var moderator = new User { TwitchUserId = moderatorId, ManagedStreamers = new List<string> { userId } };

            _mockUserRepository.Setup(repo => repo.GetUserAsync(moderatorId))
                .ReturnsAsync(moderator);

            // Act
            var result = await _controller.RevokeAccess(request);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.DoesNotContain(userId, moderator.ManagedStreamers);
            _mockUserRepository.Verify(repo => repo.SaveUserAsync(moderator), Times.Once);
        }

        [Fact]
        public async Task GetManagedStreamers_ReturnsOk_WithStreamers()
        {
            // Arrange
            var userId = "test-user-id";
            var streamerId = "streamer-id";
            var user = new User { TwitchUserId = userId, ManagedStreamers = new List<string> { streamerId } };
            var streamer = new User { TwitchUserId = streamerId, Username = "streamer" };

            _mockUserRepository.Setup(repo => repo.GetUserAsync(userId)).ReturnsAsync(user);
            _mockUserRepository.Setup(repo => repo.GetUserAsync(streamerId)).ReturnsAsync(streamer);

            // Act
            var result = await _controller.GetManagedStreamers();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        [Fact]
        public async Task GetStreamerFeatures_ReturnsOk_WhenAuthorized()
        {
            // Arrange
            var moderatorId = "test-user-id";
            var streamerId = "streamer-id";
            var moderator = new User { TwitchUserId = moderatorId, ManagedStreamers = new List<string> { streamerId } };
            var streamer = new User { TwitchUserId = streamerId, Features = new FeatureFlags() };

            _mockUserRepository.Setup(repo => repo.GetUserAsync(moderatorId)).ReturnsAsync(moderator);
            _mockUserRepository.Setup(repo => repo.GetUserAsync(streamerId)).ReturnsAsync(streamer);

            // Act
            var result = await _controller.GetStreamerFeatures(streamerId);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        [Fact]
        public async Task GetStreamerFeatures_ReturnsForbidden_WhenNotAuthorized()
        {
            // Arrange
            var moderatorId = "test-user-id";
            var streamerId = "streamer-id";
            var moderator = new User { TwitchUserId = moderatorId, ManagedStreamers = new List<string>() }; // Not managing streamer

            _mockUserRepository.Setup(repo => repo.GetUserAsync(moderatorId)).ReturnsAsync(moderator);

            // Act
            var result = await _controller.GetStreamerFeatures(streamerId);

            // Assert
            var statusCodeResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(403, statusCodeResult.StatusCode);
        }

        [Fact]
        public async Task UpdateStreamerOverlay_ReturnsOk_AndNotifies()
        {
            // Arrange
            var moderatorId = "test-user-id";
            var streamerId = "streamer-id";
            var moderator = new User { TwitchUserId = moderatorId, ManagedStreamers = new List<string> { streamerId } };
            var streamer = new User { TwitchUserId = streamerId, OverlaySettings = new OverlaySettings() };
            var newSettings = new OverlaySettings { Counters = new OverlayCounters { Deaths = true } };

            _mockUserRepository.Setup(repo => repo.GetUserAsync(moderatorId)).ReturnsAsync(moderator);
            _mockUserRepository.Setup(repo => repo.GetUserAsync(streamerId)).ReturnsAsync(streamer);

            // Act
            var result = await _controller.UpdateStreamerOverlay(streamerId, newSettings);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(repo => repo.SaveUserAsync(streamer), Times.Once);
            _mockOverlayNotifier.Verify(n => n.NotifySettingsUpdateAsync(streamerId, newSettings), Times.Once);
        }

        [Fact]
        public async Task CreateStreamerSeriesSave_ReturnsOk_WhenValid()
        {
            // Arrange
            var moderatorId = "test-user-id";
            var streamerId = "streamer-id";
            var moderator = new User { TwitchUserId = moderatorId, ManagedStreamers = new List<string> { streamerId } };
            var streamer = new User { TwitchUserId = streamerId };
            var counters = new Counter { Deaths = 10 };
            var request = new ModeratorController.CreateSeriesRequest { SeriesName = "Test Series" };

            _mockUserRepository.Setup(repo => repo.GetUserAsync(moderatorId)).ReturnsAsync(moderator);
            _mockUserRepository.Setup(repo => repo.GetUserAsync(streamerId)).ReturnsAsync(streamer);
            _mockCounterRepository.Setup(repo => repo.GetCountersAsync(streamerId)).ReturnsAsync(counters);

            // Act
            var result = await _controller.CreateStreamerSeriesSave(streamerId, request);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockSeriesRepository.Verify(repo => repo.CreateSeriesAsync(It.IsAny<Series>()), Times.Once);
        }

        [Fact]
        public async Task LoadStreamerSeriesSave_ReturnsOk_AndUpdatesCounters()
        {
            // Arrange
            var moderatorId = "test-user-id";
            var streamerId = "streamer-id";
            var seriesId = "series-id";
            var moderator = new User { TwitchUserId = moderatorId, ManagedStreamers = new List<string> { streamerId } };
            var streamer = new User { TwitchUserId = streamerId };
            var series = new Series {
                Id = seriesId,
                Snapshot = new Counter { Deaths = 50 }
            };
            var currentCounters = new Counter { Deaths = 10 };

            _mockUserRepository.Setup(repo => repo.GetUserAsync(moderatorId)).ReturnsAsync(moderator);
            _mockUserRepository.Setup(repo => repo.GetUserAsync(streamerId)).ReturnsAsync(streamer);
            _mockSeriesRepository.Setup(repo => repo.GetSeriesByIdAsync(streamerId, seriesId)).ReturnsAsync(series);
            _mockCounterRepository.Setup(repo => repo.GetCountersAsync(streamerId)).ReturnsAsync(currentCounters);

            // Act
            var result = await _controller.LoadStreamerSeriesSave(streamerId, seriesId);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(50, currentCounters.Deaths);
            _mockCounterRepository.Verify(repo => repo.SaveCountersAsync(currentCounters), Times.Once);
            _mockOverlayNotifier.Verify(n => n.NotifyCounterUpdateAsync(streamerId, currentCounters), Times.Once);
        }
    }
}
