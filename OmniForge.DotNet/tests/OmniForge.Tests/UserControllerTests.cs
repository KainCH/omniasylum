using System;
using System.Collections.Generic;
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

        private UserController CreateControllerWithNoUser()
        {
            var controller = new UserController(
                _mockUserRepository.Object,
                _mockOverlayNotifier.Object,
                _mockDiscordService.Object);

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal() }
            };

            return controller;
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

        #region Unauthorized Tests

        [Fact]
        public async Task GetSettings_ShouldReturnUnauthorized_WhenNoUser()
        {
            var controller = CreateControllerWithNoUser();
            var result = await controller.GetSettings();
            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task UpdateOverlaySettings_ShouldReturnUnauthorized_WhenNoUser()
        {
            var controller = CreateControllerWithNoUser();
            var result = await controller.UpdateOverlaySettings(new OverlaySettings());
            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task GetDiscordWebhook_ShouldReturnUnauthorized_WhenNoUser()
        {
            var controller = CreateControllerWithNoUser();
            var result = await controller.GetDiscordWebhook();
            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task UpdateDiscordWebhook_ShouldReturnUnauthorized_WhenNoUser()
        {
            var controller = CreateControllerWithNoUser();
            var result = await controller.UpdateDiscordWebhook(new UpdateWebhookRequest());
            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task TestDiscordWebhook_ShouldReturnUnauthorized_WhenNoUser()
        {
            var controller = CreateControllerWithNoUser();
            var result = await controller.TestDiscordWebhook();
            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task GetDiscordSettings_ShouldReturnUnauthorized_WhenNoUser()
        {
            var controller = CreateControllerWithNoUser();
            var result = await controller.GetDiscordSettings();
            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task UpdateDiscordSettings_ShouldReturnUnauthorized_WhenNoUser()
        {
            var controller = CreateControllerWithNoUser();
            var result = await controller.UpdateDiscordSettings(new UpdateDiscordSettingsRequest());
            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task GetDiscordInvite_ShouldReturnUnauthorized_WhenNoUser()
        {
            var controller = CreateControllerWithNoUser();
            var result = await controller.GetDiscordInvite();
            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task UpdateDiscordInvite_ShouldReturnUnauthorized_WhenNoUser()
        {
            var controller = CreateControllerWithNoUser();
            var result = await controller.UpdateDiscordInvite(new UpdateInviteRequest());
            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task UpdateTemplateStyle_ShouldReturnUnauthorized_WhenNoUser()
        {
            var controller = CreateControllerWithNoUser();
            var result = await controller.UpdateTemplateStyle(new TemplateStyleRequest());
            Assert.IsType<UnauthorizedResult>(result);
        }

        #endregion

        #region NotFound Tests

        [Fact]
        public async Task GetSettings_ShouldReturnNotFound_WhenUserDoesNotExist()
        {
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync((User?)null);
            var result = await _controller.GetSettings();
            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task UpdateOverlaySettings_ShouldReturnNotFound_WhenUserDoesNotExist()
        {
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync((User?)null);
            var result = await _controller.UpdateOverlaySettings(new OverlaySettings());
            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task GetDiscordWebhook_ShouldReturnNotFound_WhenUserDoesNotExist()
        {
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync((User?)null);
            var result = await _controller.GetDiscordWebhook();
            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task UpdateDiscordWebhook_ShouldReturnNotFound_WhenUserDoesNotExist()
        {
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync((User?)null);
            var result = await _controller.UpdateDiscordWebhook(new UpdateWebhookRequest());
            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task TestDiscordWebhook_ShouldReturnNotFound_WhenUserDoesNotExist()
        {
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync((User?)null);
            var result = await _controller.TestDiscordWebhook();
            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task GetDiscordSettings_ShouldReturnNotFound_WhenUserDoesNotExist()
        {
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync((User?)null);
            var result = await _controller.GetDiscordSettings();
            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task UpdateDiscordSettings_ShouldReturnNotFound_WhenUserDoesNotExist()
        {
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync((User?)null);
            var result = await _controller.UpdateDiscordSettings(new UpdateDiscordSettingsRequest());
            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task GetDiscordInvite_ShouldReturnNotFound_WhenUserDoesNotExist()
        {
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync((User?)null);
            var result = await _controller.GetDiscordInvite();
            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task UpdateDiscordInvite_ShouldReturnNotFound_WhenUserDoesNotExist()
        {
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync((User?)null);
            var result = await _controller.UpdateDiscordInvite(new UpdateInviteRequest());
            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task UpdateTemplateStyle_ShouldReturnNotFound_WhenUserDoesNotExist()
        {
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync((User?)null);
            var result = await _controller.UpdateTemplateStyle(new TemplateStyleRequest { TemplateStyle = "minimal" });
            Assert.IsType<NotFoundObjectResult>(result);
        }

        #endregion

        #region UpdateDiscordWebhook Tests

        [Fact]
        public async Task UpdateDiscordWebhook_ShouldAcceptEmptyUrl()
        {
            var user = new User { TwitchUserId = "12345" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var request = new UpdateWebhookRequest { WebhookUrl = "" };
            var result = await _controller.UpdateDiscordWebhook(request);

            Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.DiscordWebhookUrl == "")), Times.Once);
        }

        [Fact]
        public async Task UpdateDiscordWebhook_ShouldUseDiscordWebhookUrl_WhenWebhookUrlIsNull()
        {
            var user = new User { TwitchUserId = "12345" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);
            _mockDiscordService.Setup(x => x.ValidateWebhookAsync(It.IsAny<string>())).ReturnsAsync(true);

            var request = new UpdateWebhookRequest { DiscordWebhookUrl = "https://discord.com/api/webhooks/123/abc" };
            var result = await _controller.UpdateDiscordWebhook(request);

            Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.DiscordWebhookUrl == "https://discord.com/api/webhooks/123/abc")), Times.Once);
        }

        [Fact]
        public async Task UpdateDiscordWebhook_ShouldReturnBadRequest_WhenWebhookValidationFails()
        {
            var user = new User { TwitchUserId = "12345" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);
            _mockDiscordService.Setup(x => x.ValidateWebhookAsync(It.IsAny<string>())).ReturnsAsync(false);

            var request = new UpdateWebhookRequest { WebhookUrl = "https://discord.com/api/webhooks/123/abc" };
            var result = await _controller.UpdateDiscordWebhook(request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        #endregion

        #region TestDiscordWebhook Tests

        [Fact]
        public async Task TestDiscordWebhook_ShouldReturnServerError_WhenExceptionThrown()
        {
            var user = new User { TwitchUserId = "12345", DiscordWebhookUrl = "https://discord.com/api/webhooks/123" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);
            _mockDiscordService.Setup(x => x.SendTestNotificationAsync(user)).ThrowsAsync(new Exception("Network error"));

            var result = await _controller.TestDiscordWebhook();

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusResult.StatusCode);
        }

        #endregion

        #region UpdateDiscordSettings Tests

        [Fact]
        public async Task UpdateDiscordSettings_ShouldUpdateTemplateStyle()
        {
            var user = new User { TwitchUserId = "12345" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var request = new UpdateDiscordSettingsRequest { TemplateStyle = "minimal" };
            var result = await _controller.UpdateDiscordSettings(request);

            Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u =>
                u.Features.TemplateStyle == "minimal" &&
                u.DiscordSettings.TemplateStyle == "minimal")), Times.Once);
        }

        [Fact]
        public async Task UpdateDiscordSettings_ShouldUpdateEnabledNotifications()
        {
            var user = new User { TwitchUserId = "12345" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var request = new UpdateDiscordSettingsRequest
            {
                EnabledNotifications = new UpdateDiscordNotificationsRequest
                {
                    DeathMilestone = true,
                    SwearMilestone = false,
                    StreamStart = true,
                    StreamEnd = true,
                    FollowerGoal = true,
                    SubscriberMilestone = true,
                    ChannelPointRedemption = true
                }
            };

            var result = await _controller.UpdateDiscordSettings(request);

            Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u =>
                u.DiscordSettings.EnabledNotifications.DeathMilestone == true &&
                u.DiscordSettings.EnabledNotifications.SwearMilestone == false)), Times.Once);
        }

        [Fact]
        public async Task UpdateDiscordSettings_ShouldUseFlatProperties_WhenStructuredObjectIsMissing()
        {
            var user = new User { TwitchUserId = "12345" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var request = new UpdateDiscordSettingsRequest
            {
                DeathMilestoneEnabled = true,
                SwearMilestoneEnabled = false
            };

            var result = await _controller.UpdateDiscordSettings(request);

            Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u =>
                u.DiscordSettings.EnabledNotifications.DeathMilestone == true &&
                u.DiscordSettings.EnabledNotifications.SwearMilestone == false)), Times.Once);
        }

        [Fact]
        public async Task UpdateDiscordSettings_ShouldParseDeathThresholdsFromString()
        {
            var user = new User { TwitchUserId = "12345" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var request = new UpdateDiscordSettingsRequest { DeathThresholds = "10, 25, 50, 100" };
            var result = await _controller.UpdateDiscordSettings(request);

            Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u =>
                u.DiscordSettings.MilestoneThresholds.Deaths.Contains(10) &&
                u.DiscordSettings.MilestoneThresholds.Deaths.Contains(25) &&
                u.DiscordSettings.MilestoneThresholds.Deaths.Contains(50) &&
                u.DiscordSettings.MilestoneThresholds.Deaths.Contains(100))), Times.Once);
        }

        [Fact]
        public async Task UpdateDiscordSettings_ShouldParseSwearThresholdsFromString()
        {
            var user = new User { TwitchUserId = "12345" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var request = new UpdateDiscordSettingsRequest { SwearThresholds = "5, 15, 30" };
            var result = await _controller.UpdateDiscordSettings(request);

            Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u =>
                u.DiscordSettings.MilestoneThresholds.Swears.Contains(5) &&
                u.DiscordSettings.MilestoneThresholds.Swears.Contains(15) &&
                u.DiscordSettings.MilestoneThresholds.Swears.Contains(30))), Times.Once);
        }

        [Fact]
        public async Task UpdateDiscordSettings_ShouldUseStructuredThresholds_WhenProvided()
        {
            var user = new User { TwitchUserId = "12345" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var request = new UpdateDiscordSettingsRequest
            {
                MilestoneThresholds = new UpdateMilestoneThresholdsRequest
                {
                    Deaths = new List<int> { 1, 2, 3 },
                    Swears = new List<int> { 4, 5, 6 }
                }
            };

            var result = await _controller.UpdateDiscordSettings(request);

            Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u =>
                u.DiscordSettings.MilestoneThresholds.Deaths.Contains(1) &&
                u.DiscordSettings.MilestoneThresholds.Swears.Contains(4))), Times.Once);
        }

        [Fact]
        public async Task UpdateDiscordSettings_ShouldHandleEmptyThresholdString()
        {
            var user = new User { TwitchUserId = "12345" };
            // Set initial thresholds
            user.DiscordSettings.MilestoneThresholds.Deaths = new List<int> { 10, 20, 30 };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var request = new UpdateDiscordSettingsRequest { DeathThresholds = "" };
            var result = await _controller.UpdateDiscordSettings(request);

            // Empty string results in an empty list being parsed and set
            Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.IsAny<User>()), Times.Once);
        }

        [Fact]
        public async Task UpdateDiscordSettings_ShouldHandleInvalidThresholdNumbers()
        {
            var user = new User { TwitchUserId = "12345" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var request = new UpdateDiscordSettingsRequest { DeathThresholds = "10, abc, 20, , 30" };
            var result = await _controller.UpdateDiscordSettings(request);

            Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u =>
                u.DiscordSettings.MilestoneThresholds.Deaths.Count == 3 &&
                u.DiscordSettings.MilestoneThresholds.Deaths.Contains(10) &&
                u.DiscordSettings.MilestoneThresholds.Deaths.Contains(20) &&
                u.DiscordSettings.MilestoneThresholds.Deaths.Contains(30))), Times.Once);
        }

        #endregion

        #region UpdateTemplateStyle Tests

        [Fact]
        public async Task UpdateTemplateStyle_ShouldAcceptAsylumThemed()
        {
            var user = new User { TwitchUserId = "12345" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var request = new TemplateStyleRequest { TemplateStyle = "asylum_themed" };
            var result = await _controller.UpdateTemplateStyle(request);

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task UpdateTemplateStyle_ShouldAcceptDetailed()
        {
            var user = new User { TwitchUserId = "12345" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var request = new TemplateStyleRequest { TemplateStyle = "detailed" };
            var result = await _controller.UpdateTemplateStyle(request);

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task UpdateTemplateStyle_ShouldAcceptMinimal()
        {
            var user = new User { TwitchUserId = "12345" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var request = new TemplateStyleRequest { TemplateStyle = "minimal" };
            var result = await _controller.UpdateTemplateStyle(request);

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task UpdateTemplateStyle_ShouldUpdateBothFeaturesAndDiscordSettings()
        {
            var user = new User { TwitchUserId = "12345" };
            _mockUserRepository.Setup(x => x.GetUserAsync("12345")).ReturnsAsync(user);

            var request = new TemplateStyleRequest { TemplateStyle = "minimal" };
            await _controller.UpdateTemplateStyle(request);

            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u =>
                u.Features.TemplateStyle == "minimal" &&
                u.DiscordSettings.TemplateStyle == "minimal")), Times.Once);
        }

        #endregion
    }
}
