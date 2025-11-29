using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Infrastructure.Configuration;
using OmniForge.Web.Controllers;
using Xunit;

namespace OmniForge.Tests
{
    public class AuthControllerTests
    {
        private readonly Mock<ITwitchAuthService> _mockTwitchAuthService;
        private readonly Mock<IUserRepository> _mockUserRepository;
        private readonly Mock<IJwtService> _mockJwtService;
        private readonly Mock<IConfiguration> _mockConfiguration;
        private readonly Mock<IAuthenticationService> _mockAuthService;
        private readonly Mock<IServiceProvider> _mockServiceProvider;
        private readonly Mock<ILogger<AuthController>> _mockLogger;
        private readonly AuthController _controller;

        public AuthControllerTests()
        {
            _mockTwitchAuthService = new Mock<ITwitchAuthService>();
            _mockUserRepository = new Mock<IUserRepository>();
            _mockJwtService = new Mock<IJwtService>();
            _mockConfiguration = new Mock<IConfiguration>();
            _mockAuthService = new Mock<IAuthenticationService>();
            _mockServiceProvider = new Mock<IServiceProvider>();
            _mockLogger = new Mock<ILogger<AuthController>>();

            var twitchSettings = Options.Create(new TwitchSettings { ClientId = "client-id" });

            _mockConfiguration.Setup(x => x["Twitch:RedirectUri"]).Returns("http://localhost/callback");
            _mockConfiguration.Setup(x => x["FrontendUrl"]).Returns("http://localhost:5500");

            _mockServiceProvider
                .Setup(x => x.GetService(typeof(IAuthenticationService)))
                .Returns(_mockAuthService.Object);

            _controller = new AuthController(
                _mockTwitchAuthService.Object,
                _mockUserRepository.Object,
                _mockJwtService.Object,
                twitchSettings,
                _mockConfiguration.Object,
                _mockLogger.Object);

            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = _mockServiceProvider.Object
                }
            };
        }

        [Fact]
        public void Login_ShouldRedirectToTwitch()
        {
            _mockTwitchAuthService.Setup(x => x.GetAuthorizationUrl(It.IsAny<string>()))
                .Returns("https://twitch.tv/auth");

            var result = _controller.Login();

            var redirectResult = Assert.IsType<RedirectResult>(result);
            Assert.Equal("https://twitch.tv/auth", redirectResult.Url);
        }

        [Fact]
        public async Task Callback_ShouldReturnBadRequest_WhenCodeMissing()
        {
            var result = await _controller.Callback("");
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task Callback_ShouldReturnBadRequest_WhenExchangeFails()
        {
            _mockTwitchAuthService.Setup(x => x.ExchangeCodeForTokenAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((TwitchTokenResponse?)null);

            var result = await _controller.Callback("code");
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task Callback_ShouldReturnBadRequest_WhenUserInfoFails()
        {
            var tokenResponse = new TwitchTokenResponse { AccessToken = "token" };
            _mockTwitchAuthService.Setup(x => x.ExchangeCodeForTokenAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(tokenResponse);
            _mockTwitchAuthService.Setup(x => x.GetUserInfoAsync("token", "client-id"))
                .ReturnsAsync((TwitchUserInfo?)null);

            var result = await _controller.Callback("code");
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task Callback_ShouldRedirectToFrontend_WhenSuccess()
        {
            var tokenResponse = new TwitchTokenResponse { AccessToken = "token", ExpiresIn = 3600 };
            var userInfo = new TwitchUserInfo { Id = "123", Login = "user", DisplayName = "User" };

            _mockTwitchAuthService.Setup(x => x.ExchangeCodeForTokenAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(tokenResponse);
            _mockTwitchAuthService.Setup(x => x.GetUserInfoAsync("token", "client-id"))
                .ReturnsAsync(userInfo);
            _mockUserRepository.Setup(x => x.GetUserAsync("123")).ReturnsAsync((User?)null);
            _mockJwtService.Setup(x => x.GenerateToken(It.IsAny<User>())).Returns("jwt-token");

            var result = await _controller.Callback("code");

            var redirectResult = Assert.IsType<RedirectResult>(result);
            Assert.Equal("/portal", redirectResult.Url);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.IsAny<User>()), Times.Once);
            _mockAuthService.Verify(x => x.SignInAsync(
                It.IsAny<HttpContext>(),
                CookieAuthenticationDefaults.AuthenticationScheme,
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<AuthenticationProperties>()), Times.Once);
        }

        // Tests for Refresh with [Authorize] middleware are simplified as middleware handles token validation.
        // We only test the controller logic assuming a valid user.


        [Fact]
        public async Task Refresh_ShouldReturnNotFound_WhenUserNotFound()
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim("userId", "123")
            }, "Bearer"));
            _controller.ControllerContext.HttpContext.User = principal;
            _mockUserRepository.Setup(x => x.GetUserAsync("123")).ReturnsAsync((User?)null);

            var result = await _controller.Refresh();
            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task Refresh_ShouldReturnOk_WhenTokenValid()
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim("userId", "123")
            }, "Bearer"));
            _controller.ControllerContext.HttpContext.User = principal;
            var user = new User { TwitchUserId = "123", TokenExpiry = DateTimeOffset.UtcNow.AddHours(2) };

            _mockUserRepository.Setup(x => x.GetUserAsync("123")).ReturnsAsync(user);

            var result = await _controller.Refresh();
            var okResult = Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task Refresh_ShouldReturnUnauthorized_WhenTwitchRefreshFails()
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim("userId", "123")
            }, "Bearer"));
            _controller.ControllerContext.HttpContext.User = principal;
            var user = new User { TwitchUserId = "123", TokenExpiry = DateTimeOffset.UtcNow.AddMinutes(10), RefreshToken = "refresh" };

            _mockUserRepository.Setup(x => x.GetUserAsync("123")).ReturnsAsync(user);
            _mockTwitchAuthService.Setup(x => x.RefreshTokenAsync("refresh")).ReturnsAsync((TwitchTokenResponse?)null);

            var result = await _controller.Refresh();
            Assert.IsType<UnauthorizedObjectResult>(result);
        }

        [Fact]
        public async Task Refresh_ShouldReturnOk_WhenTwitchRefreshSuccess()
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim("userId", "123")
            }, "Bearer"));
            _controller.ControllerContext.HttpContext.User = principal;
            var user = new User { TwitchUserId = "123", TokenExpiry = DateTimeOffset.UtcNow.AddMinutes(10), RefreshToken = "refresh" };
            var newTokens = new TwitchTokenResponse { AccessToken = "new", RefreshToken = "new_refresh", ExpiresIn = 3600 };

            _mockUserRepository.Setup(x => x.GetUserAsync("123")).ReturnsAsync(user);
            _mockTwitchAuthService.Setup(x => x.RefreshTokenAsync("refresh")).ReturnsAsync(newTokens);
            _mockJwtService.Setup(x => x.GenerateToken(user)).Returns("new_jwt");

            var result = await _controller.Refresh();
            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockUserRepository.Verify(x => x.SaveUserAsync(It.Is<User>(u => u.AccessToken == "new")), Times.Once);
        }

        [Fact]
        public async Task Logout_ShouldSignOutAndRedirect()
        {
            // Arrange
            _mockAuthService
                .Setup(x => x.SignOutAsync(It.IsAny<HttpContext>(), CookieAuthenticationDefaults.AuthenticationScheme, null))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _controller.Logout();

            // Assert
            var redirectResult = Assert.IsType<RedirectResult>(result);
            Assert.Equal("/", redirectResult.Url);
            _mockAuthService.Verify(x => x.SignOutAsync(
                It.IsAny<HttpContext>(),
                CookieAuthenticationDefaults.AuthenticationScheme,
                null), Times.Once);
        }
    }
}
