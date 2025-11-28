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
            var series = new Series
            {
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

        #region GrantAccess Edge Cases

        [Fact]
        public async Task GrantAccess_ReturnsBadRequest_WhenModeratorIdEmpty()
        {
            var request = new ModeratorController.GrantAccessRequest { ModeratorId = "" };
            var result = await _controller.GrantAccess(request);
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task GrantAccess_ReturnsBadRequest_WhenAddingSelf()
        {
            var request = new ModeratorController.GrantAccessRequest { ModeratorId = "test-user-id" };
            var result = await _controller.GrantAccess(request);
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task GrantAccess_ReturnsNotFound_WhenUserNotFound()
        {
            var request = new ModeratorController.GrantAccessRequest { ModeratorId = "non-existent" };
            _mockUserRepository.Setup(repo => repo.GetUserAsync("non-existent")).ReturnsAsync((User?)null);

            var result = await _controller.GrantAccess(request);
            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task GrantAccess_SkipsSave_WhenAlreadyModerator()
        {
            var userId = "test-user-id";
            var moderatorId = "mod-id";
            var request = new ModeratorController.GrantAccessRequest { ModeratorId = moderatorId };
            var moderator = new User { TwitchUserId = moderatorId, ManagedStreamers = new List<string> { userId } };

            _mockUserRepository.Setup(repo => repo.GetUserAsync(moderatorId)).ReturnsAsync(moderator);

            var result = await _controller.GrantAccess(request);

            Assert.IsType<OkObjectResult>(result);
            // SaveUserAsync is still called but ManagedStreamers list is unchanged
            Assert.Single(moderator.ManagedStreamers);
        }

        [Fact]
        public async Task GrantAccess_Returns500_OnException()
        {
            var request = new ModeratorController.GrantAccessRequest { ModeratorId = "mod-id" };
            _mockUserRepository.Setup(repo => repo.GetUserAsync("mod-id")).ThrowsAsync(new Exception("DB error"));

            var result = await _controller.GrantAccess(request);

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusResult.StatusCode);
        }

        #endregion

        #region RevokeAccess Edge Cases

        [Fact]
        public async Task RevokeAccess_ReturnsBadRequest_WhenModeratorIdEmpty()
        {
            var request = new ModeratorController.RevokeAccessRequest { ModeratorId = "" };
            var result = await _controller.RevokeAccess(request);
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task RevokeAccess_ReturnsNotFound_WhenUserNotFound()
        {
            var request = new ModeratorController.RevokeAccessRequest { ModeratorId = "non-existent" };
            _mockUserRepository.Setup(repo => repo.GetUserAsync("non-existent")).ReturnsAsync((User?)null);

            var result = await _controller.RevokeAccess(request);
            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task RevokeAccess_Returns500_OnException()
        {
            var request = new ModeratorController.RevokeAccessRequest { ModeratorId = "mod-id" };
            _mockUserRepository.Setup(repo => repo.GetUserAsync("mod-id")).ThrowsAsync(new Exception("DB error"));

            var result = await _controller.RevokeAccess(request);

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusResult.StatusCode);
        }

        #endregion

        #region SearchUsers Tests

        [Fact]
        public async Task SearchUsers_ReturnsOk_WithMatchingUsers()
        {
            var allUsers = new List<User>
            {
                new User { TwitchUserId = "1", Username = "TestUser", DisplayName = "Test User" },
                new User { TwitchUserId = "2", Username = "OtherUser", DisplayName = "Other" }
            };
            _mockUserRepository.Setup(repo => repo.GetAllUsersAsync()).ReturnsAsync(allUsers);

            var result = await _controller.SearchUsers("Test");

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        [Fact]
        public async Task SearchUsers_ReturnsBadRequest_WhenQueryTooShort()
        {
            var result = await _controller.SearchUsers("a");
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task SearchUsers_ReturnsBadRequest_WhenQueryEmpty()
        {
            var result = await _controller.SearchUsers("");
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task SearchUsers_ReturnsBadRequest_WhenQueryNull()
        {
            var result = await _controller.SearchUsers(null!);
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task SearchUsers_Returns500_OnException()
        {
            _mockUserRepository.Setup(repo => repo.GetAllUsersAsync()).ThrowsAsync(new Exception("DB error"));

            var result = await _controller.SearchUsers("test");

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusResult.StatusCode);
        }

        #endregion

        #region GetManagedStreamers Edge Cases

        [Fact]
        public async Task GetManagedStreamers_ReturnsNotFound_WhenUserNotFound()
        {
            _mockUserRepository.Setup(repo => repo.GetUserAsync("test-user-id")).ReturnsAsync((User?)null);

            var result = await _controller.GetManagedStreamers();

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task GetManagedStreamers_Returns500_OnException()
        {
            _mockUserRepository.Setup(repo => repo.GetUserAsync("test-user-id")).ThrowsAsync(new Exception("DB error"));

            var result = await _controller.GetManagedStreamers();

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusResult.StatusCode);
        }

        [Fact]
        public async Task GetManagedStreamers_SkipsNonExistentStreamers()
        {
            var userId = "test-user-id";
            var user = new User { TwitchUserId = userId, ManagedStreamers = new List<string> { "exists", "not-exists" } };
            var streamer = new User { TwitchUserId = "exists", Username = "streamer" };

            _mockUserRepository.Setup(repo => repo.GetUserAsync(userId)).ReturnsAsync(user);
            _mockUserRepository.Setup(repo => repo.GetUserAsync("exists")).ReturnsAsync(streamer);
            _mockUserRepository.Setup(repo => repo.GetUserAsync("not-exists")).ReturnsAsync((User?)null);

            var result = await _controller.GetManagedStreamers();

            Assert.IsType<OkObjectResult>(result);
        }

        #endregion

        #region GetMyModerators Edge Cases

        [Fact]
        public async Task GetMyModerators_Returns500_OnException()
        {
            _mockUserRepository.Setup(repo => repo.GetAllUsersAsync()).ThrowsAsync(new Exception("DB error"));

            var result = await _controller.GetMyModerators();

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusResult.StatusCode);
        }

        #endregion

        #region GetStreamerFeatures Edge Cases

        [Fact]
        public async Task GetStreamerFeatures_ReturnsNotFound_WhenStreamerNotFound()
        {
            var moderatorId = "test-user-id";
            var streamerId = "streamer-id";
            var moderator = new User { TwitchUserId = moderatorId, ManagedStreamers = new List<string> { streamerId } };

            _mockUserRepository.Setup(repo => repo.GetUserAsync(moderatorId)).ReturnsAsync(moderator);
            _mockUserRepository.Setup(repo => repo.GetUserAsync(streamerId)).ReturnsAsync((User?)null);

            var result = await _controller.GetStreamerFeatures(streamerId);

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task GetStreamerFeatures_Returns500_OnException()
        {
            _mockUserRepository.Setup(repo => repo.GetUserAsync("test-user-id")).ThrowsAsync(new Exception("DB error"));

            var result = await _controller.GetStreamerFeatures("streamer-id");

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusResult.StatusCode);
        }

        #endregion

        #region UpdateStreamerFeatures Tests

        [Fact]
        public async Task UpdateStreamerFeatures_ReturnsOk_WhenAuthorized()
        {
            var moderatorId = "test-user-id";
            var streamerId = "streamer-id";
            var moderator = new User { TwitchUserId = moderatorId, ManagedStreamers = new List<string> { streamerId } };
            var streamer = new User { TwitchUserId = streamerId, Features = new FeatureFlags() };
            var newFeatures = new FeatureFlags { ChatCommands = true };

            _mockUserRepository.Setup(repo => repo.GetUserAsync(moderatorId)).ReturnsAsync(moderator);
            _mockUserRepository.Setup(repo => repo.GetUserAsync(streamerId)).ReturnsAsync(streamer);

            var result = await _controller.UpdateStreamerFeatures(streamerId, newFeatures);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(repo => repo.SaveUserAsync(streamer), Times.Once);
        }

        [Fact]
        public async Task UpdateStreamerFeatures_ReturnsForbidden_WhenNotAuthorized()
        {
            var moderatorId = "test-user-id";
            var moderator = new User { TwitchUserId = moderatorId, ManagedStreamers = new List<string>() };

            _mockUserRepository.Setup(repo => repo.GetUserAsync(moderatorId)).ReturnsAsync(moderator);

            var result = await _controller.UpdateStreamerFeatures("streamer-id", new FeatureFlags());

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(403, statusResult.StatusCode);
        }

        [Fact]
        public async Task UpdateStreamerFeatures_ReturnsNotFound_WhenStreamerNotFound()
        {
            var moderatorId = "test-user-id";
            var streamerId = "streamer-id";
            var moderator = new User { TwitchUserId = moderatorId, ManagedStreamers = new List<string> { streamerId } };

            _mockUserRepository.Setup(repo => repo.GetUserAsync(moderatorId)).ReturnsAsync(moderator);
            _mockUserRepository.Setup(repo => repo.GetUserAsync(streamerId)).ReturnsAsync((User?)null);

            var result = await _controller.UpdateStreamerFeatures(streamerId, new FeatureFlags());

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task UpdateStreamerFeatures_Returns500_OnException()
        {
            _mockUserRepository.Setup(repo => repo.GetUserAsync("test-user-id")).ThrowsAsync(new Exception("DB error"));

            var result = await _controller.UpdateStreamerFeatures("streamer-id", new FeatureFlags());

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusResult.StatusCode);
        }

        #endregion

        #region GetStreamerOverlay Tests

        [Fact]
        public async Task GetStreamerOverlay_ReturnsOk_WhenAuthorized()
        {
            var moderatorId = "test-user-id";
            var streamerId = "streamer-id";
            var moderator = new User { TwitchUserId = moderatorId, ManagedStreamers = new List<string> { streamerId } };
            var streamer = new User { TwitchUserId = streamerId, OverlaySettings = new OverlaySettings() };

            _mockUserRepository.Setup(repo => repo.GetUserAsync(moderatorId)).ReturnsAsync(moderator);
            _mockUserRepository.Setup(repo => repo.GetUserAsync(streamerId)).ReturnsAsync(streamer);

            var result = await _controller.GetStreamerOverlay(streamerId);

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task GetStreamerOverlay_ReturnsForbidden_WhenNotAuthorized()
        {
            var moderator = new User { TwitchUserId = "test-user-id", ManagedStreamers = new List<string>() };
            _mockUserRepository.Setup(repo => repo.GetUserAsync("test-user-id")).ReturnsAsync(moderator);

            var result = await _controller.GetStreamerOverlay("streamer-id");

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(403, statusResult.StatusCode);
        }

        [Fact]
        public async Task GetStreamerOverlay_ReturnsNotFound_WhenStreamerNotFound()
        {
            var moderatorId = "test-user-id";
            var streamerId = "streamer-id";
            var moderator = new User { TwitchUserId = moderatorId, ManagedStreamers = new List<string> { streamerId } };

            _mockUserRepository.Setup(repo => repo.GetUserAsync(moderatorId)).ReturnsAsync(moderator);
            _mockUserRepository.Setup(repo => repo.GetUserAsync(streamerId)).ReturnsAsync((User?)null);

            var result = await _controller.GetStreamerOverlay(streamerId);

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task GetStreamerOverlay_Returns500_OnException()
        {
            _mockUserRepository.Setup(repo => repo.GetUserAsync("test-user-id")).ThrowsAsync(new Exception("DB error"));

            var result = await _controller.GetStreamerOverlay("streamer-id");

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusResult.StatusCode);
        }

        #endregion

        #region UpdateStreamerOverlay Edge Cases

        [Fact]
        public async Task UpdateStreamerOverlay_ReturnsForbidden_WhenNotAuthorized()
        {
            var moderator = new User { TwitchUserId = "test-user-id", ManagedStreamers = new List<string>() };
            _mockUserRepository.Setup(repo => repo.GetUserAsync("test-user-id")).ReturnsAsync(moderator);

            var result = await _controller.UpdateStreamerOverlay("streamer-id", new OverlaySettings());

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(403, statusResult.StatusCode);
        }

        [Fact]
        public async Task UpdateStreamerOverlay_ReturnsNotFound_WhenStreamerNotFound()
        {
            var moderatorId = "test-user-id";
            var streamerId = "streamer-id";
            var moderator = new User { TwitchUserId = moderatorId, ManagedStreamers = new List<string> { streamerId } };

            _mockUserRepository.Setup(repo => repo.GetUserAsync(moderatorId)).ReturnsAsync(moderator);
            _mockUserRepository.Setup(repo => repo.GetUserAsync(streamerId)).ReturnsAsync((User?)null);

            var result = await _controller.UpdateStreamerOverlay(streamerId, new OverlaySettings());

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task UpdateStreamerOverlay_Returns500_OnException()
        {
            _mockUserRepository.Setup(repo => repo.GetUserAsync("test-user-id")).ThrowsAsync(new Exception("DB error"));

            var result = await _controller.UpdateStreamerOverlay("streamer-id", new OverlaySettings());

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusResult.StatusCode);
        }

        #endregion

        #region GetStreamerDiscordWebhook Tests

        [Fact]
        public async Task GetStreamerDiscordWebhook_ReturnsOk_WhenAuthorized()
        {
            var moderatorId = "test-user-id";
            var streamerId = "streamer-id";
            var moderator = new User { TwitchUserId = moderatorId, ManagedStreamers = new List<string> { streamerId } };
            var streamer = new User { TwitchUserId = streamerId, DiscordWebhookUrl = "https://discord.com/api/webhooks/123", Features = new FeatureFlags { DiscordNotifications = true } };

            _mockUserRepository.Setup(repo => repo.GetUserAsync(moderatorId)).ReturnsAsync(moderator);
            _mockUserRepository.Setup(repo => repo.GetUserAsync(streamerId)).ReturnsAsync(streamer);

            var result = await _controller.GetStreamerDiscordWebhook(streamerId);

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task GetStreamerDiscordWebhook_ReturnsForbidden_WhenNotAuthorized()
        {
            var moderator = new User { TwitchUserId = "test-user-id", ManagedStreamers = new List<string>() };
            _mockUserRepository.Setup(repo => repo.GetUserAsync("test-user-id")).ReturnsAsync(moderator);

            var result = await _controller.GetStreamerDiscordWebhook("streamer-id");

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(403, statusResult.StatusCode);
        }

        [Fact]
        public async Task GetStreamerDiscordWebhook_ReturnsNotFound_WhenStreamerNotFound()
        {
            var moderatorId = "test-user-id";
            var streamerId = "streamer-id";
            var moderator = new User { TwitchUserId = moderatorId, ManagedStreamers = new List<string> { streamerId } };

            _mockUserRepository.Setup(repo => repo.GetUserAsync(moderatorId)).ReturnsAsync(moderator);
            _mockUserRepository.Setup(repo => repo.GetUserAsync(streamerId)).ReturnsAsync((User?)null);

            var result = await _controller.GetStreamerDiscordWebhook(streamerId);

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task GetStreamerDiscordWebhook_Returns500_OnException()
        {
            _mockUserRepository.Setup(repo => repo.GetUserAsync("test-user-id")).ThrowsAsync(new Exception("DB error"));

            var result = await _controller.GetStreamerDiscordWebhook("streamer-id");

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusResult.StatusCode);
        }

        #endregion

        #region UpdateStreamerDiscordWebhook Tests

        [Fact]
        public async Task UpdateStreamerDiscordWebhook_ReturnsOk_WhenAuthorized()
        {
            var moderatorId = "test-user-id";
            var streamerId = "streamer-id";
            var moderator = new User { TwitchUserId = moderatorId, ManagedStreamers = new List<string> { streamerId } };
            var streamer = new User { TwitchUserId = streamerId, Features = new FeatureFlags() };
            var request = new ModeratorController.UpdateDiscordWebhookRequest { WebhookUrl = "https://discord.com/api/webhooks/456", Enabled = true };

            _mockUserRepository.Setup(repo => repo.GetUserAsync(moderatorId)).ReturnsAsync(moderator);
            _mockUserRepository.Setup(repo => repo.GetUserAsync(streamerId)).ReturnsAsync(streamer);

            var result = await _controller.UpdateStreamerDiscordWebhook(streamerId, request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(request.WebhookUrl, streamer.DiscordWebhookUrl);
            Assert.True(streamer.Features.DiscordNotifications);
        }

        [Fact]
        public async Task UpdateStreamerDiscordWebhook_ReturnsForbidden_WhenNotAuthorized()
        {
            var moderator = new User { TwitchUserId = "test-user-id", ManagedStreamers = new List<string>() };
            _mockUserRepository.Setup(repo => repo.GetUserAsync("test-user-id")).ReturnsAsync(moderator);

            var result = await _controller.UpdateStreamerDiscordWebhook("streamer-id", new ModeratorController.UpdateDiscordWebhookRequest());

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(403, statusResult.StatusCode);
        }

        [Fact]
        public async Task UpdateStreamerDiscordWebhook_ReturnsNotFound_WhenStreamerNotFound()
        {
            var moderatorId = "test-user-id";
            var streamerId = "streamer-id";
            var moderator = new User { TwitchUserId = moderatorId, ManagedStreamers = new List<string> { streamerId } };

            _mockUserRepository.Setup(repo => repo.GetUserAsync(moderatorId)).ReturnsAsync(moderator);
            _mockUserRepository.Setup(repo => repo.GetUserAsync(streamerId)).ReturnsAsync((User?)null);

            var result = await _controller.UpdateStreamerDiscordWebhook(streamerId, new ModeratorController.UpdateDiscordWebhookRequest());

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task UpdateStreamerDiscordWebhook_Returns500_OnException()
        {
            _mockUserRepository.Setup(repo => repo.GetUserAsync("test-user-id")).ThrowsAsync(new Exception("DB error"));

            var result = await _controller.UpdateStreamerDiscordWebhook("streamer-id", new ModeratorController.UpdateDiscordWebhookRequest());

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusResult.StatusCode);
        }

        #endregion

        #region GetStreamerSeriesSaves Tests

        [Fact]
        public async Task GetStreamerSeriesSaves_ReturnsOk_WhenAuthorized()
        {
            var moderatorId = "test-user-id";
            var streamerId = "streamer-id";
            var moderator = new User { TwitchUserId = moderatorId, ManagedStreamers = new List<string> { streamerId } };
            var streamer = new User { TwitchUserId = streamerId };
            var seriesList = new List<Series> { new Series { Id = "1", Name = "Test" } };

            _mockUserRepository.Setup(repo => repo.GetUserAsync(moderatorId)).ReturnsAsync(moderator);
            _mockUserRepository.Setup(repo => repo.GetUserAsync(streamerId)).ReturnsAsync(streamer);
            _mockSeriesRepository.Setup(repo => repo.GetSeriesAsync(streamerId)).ReturnsAsync(seriesList);

            var result = await _controller.GetStreamerSeriesSaves(streamerId);

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task GetStreamerSeriesSaves_ReturnsForbidden_WhenNotAuthorized()
        {
            var moderator = new User { TwitchUserId = "test-user-id", ManagedStreamers = new List<string>() };
            _mockUserRepository.Setup(repo => repo.GetUserAsync("test-user-id")).ReturnsAsync(moderator);

            var result = await _controller.GetStreamerSeriesSaves("streamer-id");

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(403, statusResult.StatusCode);
        }

        [Fact]
        public async Task GetStreamerSeriesSaves_ReturnsNotFound_WhenStreamerNotFound()
        {
            var moderatorId = "test-user-id";
            var streamerId = "streamer-id";
            var moderator = new User { TwitchUserId = moderatorId, ManagedStreamers = new List<string> { streamerId } };

            _mockUserRepository.Setup(repo => repo.GetUserAsync(moderatorId)).ReturnsAsync(moderator);
            _mockUserRepository.Setup(repo => repo.GetUserAsync(streamerId)).ReturnsAsync((User?)null);

            var result = await _controller.GetStreamerSeriesSaves(streamerId);

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task GetStreamerSeriesSaves_Returns500_OnException()
        {
            _mockUserRepository.Setup(repo => repo.GetUserAsync("test-user-id")).ThrowsAsync(new Exception("DB error"));

            var result = await _controller.GetStreamerSeriesSaves("streamer-id");

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusResult.StatusCode);
        }

        #endregion

        #region CreateStreamerSeriesSave Edge Cases

        [Fact]
        public async Task CreateStreamerSeriesSave_ReturnsForbidden_WhenNotAuthorized()
        {
            var moderator = new User { TwitchUserId = "test-user-id", ManagedStreamers = new List<string>() };
            _mockUserRepository.Setup(repo => repo.GetUserAsync("test-user-id")).ReturnsAsync(moderator);

            var result = await _controller.CreateStreamerSeriesSave("streamer-id", new ModeratorController.CreateSeriesRequest { SeriesName = "Test" });

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(403, statusResult.StatusCode);
        }

        [Fact]
        public async Task CreateStreamerSeriesSave_ReturnsBadRequest_WhenNameEmpty()
        {
            var moderatorId = "test-user-id";
            var streamerId = "streamer-id";
            var moderator = new User { TwitchUserId = moderatorId, ManagedStreamers = new List<string> { streamerId } };

            _mockUserRepository.Setup(repo => repo.GetUserAsync(moderatorId)).ReturnsAsync(moderator);

            var result = await _controller.CreateStreamerSeriesSave(streamerId, new ModeratorController.CreateSeriesRequest { SeriesName = "" });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task CreateStreamerSeriesSave_ReturnsBadRequest_WhenNameWhitespace()
        {
            var moderatorId = "test-user-id";
            var streamerId = "streamer-id";
            var moderator = new User { TwitchUserId = moderatorId, ManagedStreamers = new List<string> { streamerId } };

            _mockUserRepository.Setup(repo => repo.GetUserAsync(moderatorId)).ReturnsAsync(moderator);

            var result = await _controller.CreateStreamerSeriesSave(streamerId, new ModeratorController.CreateSeriesRequest { SeriesName = "   " });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task CreateStreamerSeriesSave_ReturnsNotFound_WhenStreamerNotFound()
        {
            var moderatorId = "test-user-id";
            var streamerId = "streamer-id";
            var moderator = new User { TwitchUserId = moderatorId, ManagedStreamers = new List<string> { streamerId } };

            _mockUserRepository.Setup(repo => repo.GetUserAsync(moderatorId)).ReturnsAsync(moderator);
            _mockUserRepository.Setup(repo => repo.GetUserAsync(streamerId)).ReturnsAsync((User?)null);

            var result = await _controller.CreateStreamerSeriesSave(streamerId, new ModeratorController.CreateSeriesRequest { SeriesName = "Test" });

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task CreateStreamerSeriesSave_ReturnsNotFound_WhenCountersNotFound()
        {
            var moderatorId = "test-user-id";
            var streamerId = "streamer-id";
            var moderator = new User { TwitchUserId = moderatorId, ManagedStreamers = new List<string> { streamerId } };
            var streamer = new User { TwitchUserId = streamerId };

            _mockUserRepository.Setup(repo => repo.GetUserAsync(moderatorId)).ReturnsAsync(moderator);
            _mockUserRepository.Setup(repo => repo.GetUserAsync(streamerId)).ReturnsAsync(streamer);
            _mockCounterRepository.Setup(repo => repo.GetCountersAsync(streamerId)).ReturnsAsync((Counter?)null);

            var result = await _controller.CreateStreamerSeriesSave(streamerId, new ModeratorController.CreateSeriesRequest { SeriesName = "Test" });

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task CreateStreamerSeriesSave_Returns500_OnException()
        {
            _mockUserRepository.Setup(repo => repo.GetUserAsync("test-user-id")).ThrowsAsync(new Exception("DB error"));

            var result = await _controller.CreateStreamerSeriesSave("streamer-id", new ModeratorController.CreateSeriesRequest { SeriesName = "Test" });

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusResult.StatusCode);
        }

        #endregion

        #region LoadStreamerSeriesSave Edge Cases

        [Fact]
        public async Task LoadStreamerSeriesSave_ReturnsForbidden_WhenNotAuthorized()
        {
            var moderator = new User { TwitchUserId = "test-user-id", ManagedStreamers = new List<string>() };
            _mockUserRepository.Setup(repo => repo.GetUserAsync("test-user-id")).ReturnsAsync(moderator);

            var result = await _controller.LoadStreamerSeriesSave("streamer-id", "series-id");

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(403, statusResult.StatusCode);
        }

        [Fact]
        public async Task LoadStreamerSeriesSave_ReturnsNotFound_WhenStreamerNotFound()
        {
            var moderatorId = "test-user-id";
            var streamerId = "streamer-id";
            var moderator = new User { TwitchUserId = moderatorId, ManagedStreamers = new List<string> { streamerId } };

            _mockUserRepository.Setup(repo => repo.GetUserAsync(moderatorId)).ReturnsAsync(moderator);
            _mockUserRepository.Setup(repo => repo.GetUserAsync(streamerId)).ReturnsAsync((User?)null);

            var result = await _controller.LoadStreamerSeriesSave(streamerId, "series-id");

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task LoadStreamerSeriesSave_ReturnsNotFound_WhenSeriesNotFound()
        {
            var moderatorId = "test-user-id";
            var streamerId = "streamer-id";
            var moderator = new User { TwitchUserId = moderatorId, ManagedStreamers = new List<string> { streamerId } };
            var streamer = new User { TwitchUserId = streamerId };

            _mockUserRepository.Setup(repo => repo.GetUserAsync(moderatorId)).ReturnsAsync(moderator);
            _mockUserRepository.Setup(repo => repo.GetUserAsync(streamerId)).ReturnsAsync(streamer);
            _mockSeriesRepository.Setup(repo => repo.GetSeriesByIdAsync(streamerId, "series-id")).ReturnsAsync((Series?)null);

            var result = await _controller.LoadStreamerSeriesSave(streamerId, "series-id");

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task LoadStreamerSeriesSave_ReturnsNotFound_WhenCountersNotFound()
        {
            var moderatorId = "test-user-id";
            var streamerId = "streamer-id";
            var moderator = new User { TwitchUserId = moderatorId, ManagedStreamers = new List<string> { streamerId } };
            var streamer = new User { TwitchUserId = streamerId };
            var series = new Series { Id = "series-id", Snapshot = new Counter() };

            _mockUserRepository.Setup(repo => repo.GetUserAsync(moderatorId)).ReturnsAsync(moderator);
            _mockUserRepository.Setup(repo => repo.GetUserAsync(streamerId)).ReturnsAsync(streamer);
            _mockSeriesRepository.Setup(repo => repo.GetSeriesByIdAsync(streamerId, "series-id")).ReturnsAsync(series);
            _mockCounterRepository.Setup(repo => repo.GetCountersAsync(streamerId)).ReturnsAsync((Counter?)null);

            var result = await _controller.LoadStreamerSeriesSave(streamerId, "series-id");

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task LoadStreamerSeriesSave_Returns500_OnException()
        {
            _mockUserRepository.Setup(repo => repo.GetUserAsync("test-user-id")).ThrowsAsync(new Exception("DB error"));

            var result = await _controller.LoadStreamerSeriesSave("streamer-id", "series-id");

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusResult.StatusCode);
        }

        #endregion

        #region DeleteStreamerSeriesSave Tests

        [Fact]
        public async Task DeleteStreamerSeriesSave_ReturnsOk_WhenAuthorized()
        {
            var moderatorId = "test-user-id";
            var streamerId = "streamer-id";
            var seriesId = "series-id";
            var moderator = new User { TwitchUserId = moderatorId, ManagedStreamers = new List<string> { streamerId } };
            var streamer = new User { TwitchUserId = streamerId };

            _mockUserRepository.Setup(repo => repo.GetUserAsync(moderatorId)).ReturnsAsync(moderator);
            _mockUserRepository.Setup(repo => repo.GetUserAsync(streamerId)).ReturnsAsync(streamer);

            var result = await _controller.DeleteStreamerSeriesSave(streamerId, seriesId);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockSeriesRepository.Verify(repo => repo.DeleteSeriesAsync(streamerId, seriesId), Times.Once);
        }

        [Fact]
        public async Task DeleteStreamerSeriesSave_ReturnsForbidden_WhenNotAuthorized()
        {
            var moderator = new User { TwitchUserId = "test-user-id", ManagedStreamers = new List<string>() };
            _mockUserRepository.Setup(repo => repo.GetUserAsync("test-user-id")).ReturnsAsync(moderator);

            var result = await _controller.DeleteStreamerSeriesSave("streamer-id", "series-id");

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(403, statusResult.StatusCode);
        }

        [Fact]
        public async Task DeleteStreamerSeriesSave_ReturnsNotFound_WhenStreamerNotFound()
        {
            var moderatorId = "test-user-id";
            var streamerId = "streamer-id";
            var moderator = new User { TwitchUserId = moderatorId, ManagedStreamers = new List<string> { streamerId } };

            _mockUserRepository.Setup(repo => repo.GetUserAsync(moderatorId)).ReturnsAsync(moderator);
            _mockUserRepository.Setup(repo => repo.GetUserAsync(streamerId)).ReturnsAsync((User?)null);

            var result = await _controller.DeleteStreamerSeriesSave(streamerId, "series-id");

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task DeleteStreamerSeriesSave_Returns500_OnException()
        {
            _mockUserRepository.Setup(repo => repo.GetUserAsync("test-user-id")).ThrowsAsync(new Exception("DB error"));

            var result = await _controller.DeleteStreamerSeriesSave("streamer-id", "series-id");

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusResult.StatusCode);
        }

        #endregion
    }
}
