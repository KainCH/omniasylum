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
            _mockDiscordService.Setup(x => x.ValidateWebhookAsync(It.IsAny<string>()))
                .ReturnsAsync(true);

            var request = new UpdateWebhookRequest { WebhookUrl = "https://discord.com/api/webhooks/123/abc" };

            var result = await _controller.UpdateDiscordWebhook(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.DiscordWebhookUrl == request.WebhookUrl)), Times.Once);
        }

        [Fact]
        public async Task GetDiscordWebhook_ShouldReturnOk()
        {
            var user = new User { TwitchUserId = "12345", DiscordWebhookUrl = "https://discord.com/api/webhooks/123" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var result = await _controller.GetDiscordWebhook();

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        [Fact]
        public async Task TestDiscordWebhook_ShouldReturnOk_WhenValid()
        {
            var user = new User { TwitchUserId = "12345", DiscordWebhookUrl = "https://discord.com/api/webhooks/123" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);
            _mockDiscordService.Setup(x => x.SendTestNotificationAsync(user)).Returns(Task.CompletedTask);

            var result = await _controller.TestDiscordWebhook();

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockDiscordService.Verify(x => x.SendTestNotificationAsync(user), Times.Once);
        }

        [Fact]
        public async Task TestDiscordWebhook_ShouldReturnBadRequest_WhenNoUrl()
        {
            var user = new User { TwitchUserId = "12345", DiscordWebhookUrl = "" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var result = await _controller.TestDiscordWebhook();

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task GetDiscordSettings_ShouldReturnOk()
        {
            var user = new User
            {
                TwitchUserId = "12345",
                DiscordWebhookUrl = "https://discord.com/api/webhooks/123",
                DiscordSettings = new DiscordSettings
                {
                    EnableChannelNotifications = true,
                    TemplateStyle = "asylum_themed"
                }
            };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var result = await _controller.GetDiscordSettings();

            var okResult = Assert.IsType<OkObjectResult>(result);

            // Use reflection to verify properties of the anonymous object
            var value = okResult.Value;
            Assert.NotNull(value);
            var type = value.GetType();

            Assert.Equal(user.DiscordWebhookUrl, type.GetProperty("webhookUrl")!.GetValue(value));
            Assert.True((bool)type.GetProperty("enabled")!.GetValue(value)!);
            Assert.Equal("asylum_themed", type.GetProperty("templateStyle")!.GetValue(value));
            Assert.True((bool)type.GetProperty("enableChannelNotifications")!.GetValue(value)!);
        }

        [Fact]
        public async Task UpdateDiscordSettings_ShouldReturnOk()
        {
            var user = new User { TwitchUserId = "12345" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var request = new UpdateDiscordSettingsRequest { EnableChannelNotifications = true };
            var result = await _controller.UpdateDiscordSettings(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.DiscordSettings.EnableChannelNotifications == true)), Times.Once);
        }

        [Fact]
        public async Task GetDiscordInvite_ShouldReturnOk()
        {
            var user = new User { TwitchUserId = "12345", DiscordInviteLink = "https://discord.gg/123" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var result = await _controller.GetDiscordInvite();

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        [Fact]
        public async Task UpdateDiscordInvite_ShouldReturnOk()
        {
            var user = new User { TwitchUserId = "12345" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var request = new UpdateInviteRequest { DiscordInviteLink = "https://discord.gg/new" };
            var result = await _controller.UpdateDiscordInvite(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.DiscordInviteLink == "https://discord.gg/new")), Times.Once);
        }

        [Fact]
        public async Task UpdateTemplateStyle_ShouldReturnOk_WhenValid()
        {
            var user = new User { TwitchUserId = "12345" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var request = new TemplateStyleRequest { TemplateStyle = "detailed" };
            var result = await _controller.UpdateTemplateStyle(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.Features.TemplateStyle == "detailed")), Times.Once);
        }

        [Fact]
        public async Task UpdateTemplateStyle_ShouldReturnBadRequest_WhenInvalid()
        {
            var user = new User { TwitchUserId = "12345" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var request = new TemplateStyleRequest { TemplateStyle = "invalid" };
            var result = await _controller.UpdateTemplateStyle(request);

            Assert.IsType<BadRequestObjectResult>(result);
        }
    }
}
