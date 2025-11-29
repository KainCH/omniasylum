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
        private readonly Mock<ILogger<DebugController>> _mockLogger;
        private readonly DebugController _controller;

        public DebugControllerTests()
        {
            _mockUserRepository = new Mock<IUserRepository>();
            _mockDiscordService = new Mock<IDiscordService>();
            _mockSeriesRepository = new Mock<ISeriesRepository>();
            _mockLogger = new Mock<ILogger<DebugController>>();

            _controller = new DebugController(
                _mockUserRepository.Object,
                _mockDiscordService.Object,
                _mockSeriesRepository.Object,
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
            _mockSeriesRepository.Verify(x => x.CreateSeriesAsync(It.Is<Series>(s =>
                s.UserId == "12345" &&
                s.Name == "Test Series" &&
                s.Snapshot.Deaths == 10 &&
                s.Snapshot.Swears == 5
            )), Times.Once);
        }
    }
}
