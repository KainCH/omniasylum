using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Controllers;
using Xunit;

namespace OmniForge.Tests
{
    public class UserControllerTests
    {
        private readonly Mock<IUserRepository> _mockUserRepository;
        private readonly Mock<IOverlayNotifier> _mockOverlayNotifier;
        private readonly Mock<IDiscordService> _mockDiscordService;
        private readonly UserController _controller;

        public UserControllerTests()
        {
            _mockUserRepository = new Mock<IUserRepository>();
            _mockOverlayNotifier = new Mock<IOverlayNotifier>();
            _mockDiscordService = new Mock<IDiscordService>();

            _controller = new UserController(
                _mockUserRepository.Object,
                _mockOverlayNotifier.Object,
                _mockDiscordService.Object);

            // Setup User Context
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
        public async Task GetSettings_ShouldReturnOk_WhenUserExists()
        {
            var user = new User { TwitchUserId = "12345", StreamStatus = "live" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345"))
                .ReturnsAsync(user);

            var result = await _controller.GetSettings();

            var okResult = Assert.IsType<OkObjectResult>(result);
            // We can't easily cast anonymous types in tests, but we can check it's not null
            Assert.NotNull(okResult.Value);
        }

        [Fact]
        public async Task UpdateOverlaySettings_ShouldReturnOk_WhenValid()
        {
            var user = new User { TwitchUserId = "12345" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345"))
                .ReturnsAsync(user);

            var settings = new OverlaySettings 
            { 
                Theme = new OverlayTheme { BackgroundColor = "black" } 
            };

            var result = await _controller.UpdateOverlaySettings(settings);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.OverlaySettings.Theme.BackgroundColor == "black")), Times.Once);
            _mockOverlayNotifier.Verify(x => x.NotifySettingsUpdateAsync("12345", settings), Times.Once);
        }

        [Fact]
        public async Task UpdateDiscordWebhook_ShouldReturnBadRequest_WhenInvalidUrl()
        {
            var user = new User { TwitchUserId = "12345" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345"))
                .ReturnsAsync(user);

            var request = new UpdateWebhookRequest { WebhookUrl = "http://invalid.com" };

            var result = await _controller.UpdateDiscordWebhook(request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task UpdateDiscordWebhook_ShouldReturnOk_WhenValidUrl()
        {
            var user = new User { TwitchUserId = "12345" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345"))
                .ReturnsAsync(user);

            var request = new UpdateWebhookRequest { WebhookUrl = "https://discord.com/api/webhooks/123/abc" };

            var result = await _controller.UpdateDiscordWebhook(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.DiscordWebhookUrl == request.WebhookUrl)), Times.Once);
        }
    }
}
