using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Controllers;
using Xunit;

namespace OmniForge.Tests
{
    public class DebugControllerTests
    {
        private readonly Mock<IUserRepository> _mockUserRepository;
        private readonly Mock<IDiscordService> _mockDiscordService;
        private readonly Mock<ISeriesRepository> _mockSeriesRepository;
        private readonly Mock<IOverlayNotifier> _mockOverlayNotifier;
        private readonly Mock<ILogger<DebugController>> _mockLogger;
        private readonly DebugController _controller;

        public DebugControllerTests()
        {
            _mockUserRepository = new Mock<IUserRepository>();
            _mockDiscordService = new Mock<IDiscordService>();
            _mockSeriesRepository = new Mock<ISeriesRepository>();
            _mockOverlayNotifier = new Mock<IOverlayNotifier>();
            _mockLogger = new Mock<ILogger<DebugController>>();

            _controller = new DebugController(
                _mockUserRepository.Object,
                _mockDiscordService.Object,
                _mockSeriesRepository.Object,
                _mockOverlayNotifier.Object,
                _mockLogger.Object);

            var user = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
            {
                new Claim("userId", "12345")
            }, "mock"));

            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            };
        }

        [Fact]
        public async Task TestWebhookSave_ShouldReturnOk()
        {
            var user = new User { TwitchUserId = "12345", DiscordWebhookUrl = "old" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var result = await _controller.TestWebhookSave();

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.DiscordWebhookUrl.Contains("test-webhook-token"))), Times.Once);
        }

        [Fact]
        public async Task TestWebhookRead_ShouldReturnOk()
        {
            var user = new User { TwitchUserId = "12345", DiscordWebhookUrl = "url" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var result = await _controller.TestWebhookRead();

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        [Fact]
        public async Task TestStreamNotification_ShouldReturnBadRequest_WhenNoWebhook()
        {
            var user = new User { TwitchUserId = "12345", DiscordWebhookUrl = "" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var result = await _controller.TestStreamNotification();

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task TestStreamNotification_ShouldReturnOk_WhenValid()
        {
            var user = new User { TwitchUserId = "12345", DiscordWebhookUrl = "url", Username = "test" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var result = await _controller.TestStreamNotification();

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockDiscordService.Verify(x => x.SendNotificationAsync(user, "stream-online", It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public async Task CleanupUserData_ShouldReturnOk_WhenCleaning()
        {
            var user = new User { TwitchUserId = "12345", DiscordWebhookUrl = "https://discord.com/api/webhooks/1234567890/test-webhook-token-12345" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var result = await _controller.CleanupUserData();

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => string.IsNullOrEmpty(u.DiscordWebhookUrl))), Times.Once);
        }

        [Fact]
        public async Task RestoreSeriesSave_ShouldReturnOk()
        {
            var request = new RestoreSeriesRequest
            {
                TwitchUserId = "12345",
                SeriesName = "Test Series",
                Description = "Test Description",
                Counters = new RestoreSeriesRequest.CounterValues
                {
                    Deaths = 10,
                    Swears = 5
                }
            };

            var result = await _controller.RestoreSeriesSave(request);

            var okResult = Assert.IsType<OkObjectResult>(result);

            // Use reflection to verify anonymous type properties
            var value = okResult.Value;
            Assert.NotNull(value);

            var successProperty = value!.GetType().GetProperty("success");
            var saveProperty = value.GetType().GetProperty("save");

            Assert.NotNull(successProperty);
            Assert.True((bool)successProperty!.GetValue(value)!);

            Assert.NotNull(saveProperty);
            var saveValue = saveProperty!.GetValue(value);
            Assert.NotNull(saveValue);

            var seriesNameProperty = saveValue!.GetType().GetProperty("seriesName");
            var deathsProperty = saveValue.GetType().GetProperty("deaths");

            Assert.NotNull(seriesNameProperty);
            Assert.NotNull(deathsProperty);

            Assert.Equal("Test Series", seriesNameProperty!.GetValue(saveValue));
            Assert.Equal(10, deathsProperty!.GetValue(saveValue));

            _mockSeriesRepository.Verify(x => x.CreateSeriesAsync(It.Is<Series>(s =>
                s.UserId == "12345" &&
                s.Name == "Test Series" &&
                s.Snapshot.Deaths == 10 &&
                s.Snapshot.Swears == 5
            )), Times.Once);
        }

        [Fact]
        public async Task RestoreSeriesSave_ShouldReturn500_WhenRepositoryFails()
        {
            var request = new RestoreSeriesRequest
            {
                TwitchUserId = "12345",
                SeriesName = "Test Series"
            };

            _mockSeriesRepository.Setup(x => x.CreateSeriesAsync(It.IsAny<Series>()))
                .ThrowsAsync(new System.Exception("Database error"));

            var result = await _controller.RestoreSeriesSave(request);

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, objectResult.StatusCode);

            var value = objectResult.Value;
            Assert.NotNull(value);

            var successProperty = value!.GetType().GetProperty("success");
            var errorProperty = value.GetType().GetProperty("error");

            Assert.NotNull(successProperty);
            Assert.NotNull(errorProperty);

            Assert.False((bool)successProperty!.GetValue(value)!);
            Assert.Equal("Failed to create series save", errorProperty!.GetValue(value));
        }

        [Fact]
        public async Task RestoreSeriesSave_ShouldReturnBadRequest_WhenValidationFails()
        {
            var request = new RestoreSeriesRequest
            {
                TwitchUserId = "", // Invalid
                SeriesName = ""   // Invalid
            };

            var result = await _controller.RestoreSeriesSave(request);

            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("TwitchUserId and SeriesName are required", badRequestResult.Value);
        }

        [Fact]
        public async Task SendInteractionBanner_ShouldReturnBadRequest_WhenTwitchUserIdMissing()
        {
            var request = new InteractionBannerRequest
            {
                TwitchUserId = "",
                TextPrompt = "Test prompt"
            };

            var result = await _controller.SendInteractionBanner(request);

            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            var value = badRequestResult.Value;
            Assert.NotNull(value);
            var errorProperty = value!.GetType().GetProperty("error");
            Assert.NotNull(errorProperty);
            Assert.Equal("TwitchUserId is required", errorProperty!.GetValue(value));
        }

        [Fact]
        public async Task SendInteractionBanner_ShouldReturnBadRequest_WhenTextPromptMissing()
        {
            var request = new InteractionBannerRequest
            {
                TwitchUserId = "12345",
                TextPrompt = ""
            };

            var result = await _controller.SendInteractionBanner(request);

            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            var value = badRequestResult.Value;
            Assert.NotNull(value);

            // Verify success is false and error message is set correctly
            var successProperty = value!.GetType().GetProperty("success");
            Assert.NotNull(successProperty);
            Assert.False((bool)successProperty!.GetValue(value)!);

            var errorProperty = value!.GetType().GetProperty("error");
            Assert.NotNull(errorProperty);
            Assert.Equal("TextPrompt is required", errorProperty!.GetValue(value));
        }

        [Fact]
        public async Task SendInteractionBanner_ShouldReturnOk_WhenValidRequest()
        {
            var request = new InteractionBannerRequest
            {
                TwitchUserId = "12345",
                TextPrompt = "Press F to pay respects",
                DurationMs = 5000
            };

            var result = await _controller.SendInteractionBanner(request);

            Assert.IsType<OkObjectResult>(result);
            _mockOverlayNotifier.Verify(x => x.NotifyCustomAlertAsync("12345", "interactionBanner", It.IsAny<object>()), Times.Once);
        }

        [Fact]
        public async Task SendInteractionBanner_ShouldClampDuration_WhenTooLow()
        {
            var request = new InteractionBannerRequest
            {
                TwitchUserId = "12345",
                TextPrompt = "Test prompt",
                DurationMs = 100 // Below minimum of 500
            };

            var result = await _controller.SendInteractionBanner(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            var value = okResult.Value;
            Assert.NotNull(value);
            var durationProperty = value!.GetType().GetProperty("duration");
            Assert.NotNull(durationProperty);
            Assert.Equal(500, durationProperty!.GetValue(value)); // Should be clamped to minimum
        }

        [Fact]
        public async Task SendInteractionBanner_ShouldClampDuration_WhenTooHigh()
        {
            var request = new InteractionBannerRequest
            {
                TwitchUserId = "12345",
                TextPrompt = "Test prompt",
                DurationMs = 50000 // Above maximum of 30000
            };

            var result = await _controller.SendInteractionBanner(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            var value = okResult.Value;
            Assert.NotNull(value);
            var durationProperty = value!.GetType().GetProperty("duration");
            Assert.NotNull(durationProperty);
            Assert.Equal(30000, durationProperty!.GetValue(value)); // Should be clamped to maximum
        }

        [Fact]
        public async Task SendInteractionBanner_ShouldUseDefaultDuration_WhenNotProvided()
        {
            var request = new InteractionBannerRequest
            {
                TwitchUserId = "12345",
                TextPrompt = "Test prompt"
                // DurationMs not set
            };

            var result = await _controller.SendInteractionBanner(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            var value = okResult.Value;
            Assert.NotNull(value);
            var durationProperty = value!.GetType().GetProperty("duration");
            Assert.NotNull(durationProperty);
            Assert.Equal(5000, durationProperty!.GetValue(value)); // Default duration
        }
    }
}
