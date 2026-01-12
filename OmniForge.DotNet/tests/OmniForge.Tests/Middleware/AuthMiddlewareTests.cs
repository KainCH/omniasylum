using System;
using System.IO;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Exceptions;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Middleware;
using Xunit;

namespace OmniForge.Tests.Middleware
{
    public class AuthMiddlewareTests
    {
        private readonly Mock<IUserRepository> _mockUserRepository;
        private readonly Mock<RequestDelegate> _mockNext;
        private readonly Mock<ILogger<AuthMiddleware>> _mockLogger;

        public AuthMiddlewareTests()
        {
            _mockUserRepository = new Mock<IUserRepository>();
            _mockNext = new Mock<RequestDelegate>();
            _mockLogger = new Mock<ILogger<AuthMiddleware>>();
            _mockNext.Setup(x => x(It.IsAny<HttpContext>())).Returns(Task.CompletedTask);
        }

        private HttpContext CreateHttpContext(string path = "/", bool isAuthenticated = false, string? userId = null)
        {
            var context = new DefaultHttpContext();
            context.Request.Path = path;
            context.Response.Body = new MemoryStream();

            if (isAuthenticated && userId != null)
            {
                var claims = new[] { new Claim("userId", userId) };
                var identity = new ClaimsIdentity(claims, "TestAuth");
                context.User = new ClaimsPrincipal(identity);
            }

            return context;
        }

        [Theory]
        [InlineData("/_blazor")]
        [InlineData("/_blazor/negotiate")]
        [InlineData("/_framework")]
        [InlineData("/_framework/blazor.server.js")]
        [InlineData("/overlayHub")]
        [InlineData("/ws/overlay")]
        [InlineData("/overlay")]
        [InlineData("/overlay/123")]
        public async Task InvokeAsync_SkippedPaths_ShouldCallNextWithoutValidation(string path)
        {
            // Arrange
            var middleware = new AuthMiddleware(_mockNext.Object, _mockLogger.Object);
            var context = CreateHttpContext(path);

            // Act
            await middleware.InvokeAsync(context, _mockUserRepository.Object);

            // Assert
            _mockNext.Verify(x => x(context), Times.Once);
            _mockUserRepository.Verify(x => x.GetUserAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task InvokeAsync_WebSocketRequest_ShouldSkipValidation()
        {
            // Arrange
            var middleware = new AuthMiddleware(_mockNext.Object, _mockLogger.Object);
            var context = new DefaultHttpContext();
            context.Request.Path = "/some-path";

            // Simulate WebSocket request by setting the upgrade header
            var webSocketFeature = new Mock<IHttpWebSocketFeature>();
            webSocketFeature.Setup(x => x.IsWebSocketRequest).Returns(true);
            context.Features.Set(webSocketFeature.Object);

            // Act
            await middleware.InvokeAsync(context, _mockUserRepository.Object);

            // Assert
            _mockNext.Verify(x => x(context), Times.Once);
            _mockUserRepository.Verify(x => x.GetUserAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task InvokeAsync_UnauthenticatedUser_ShouldCallNextWithoutValidation()
        {
            // Arrange
            var middleware = new AuthMiddleware(_mockNext.Object, _mockLogger.Object);
            var context = CreateHttpContext("/api/test", isAuthenticated: false);

            // Act
            await middleware.InvokeAsync(context, _mockUserRepository.Object);

            // Assert
            _mockNext.Verify(x => x(context), Times.Once);
            _mockUserRepository.Verify(x => x.GetUserAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task InvokeAsync_AuthenticatedUserWithValidUser_ShouldAttachUserToContext()
        {
            // Arrange
            var middleware = new AuthMiddleware(_mockNext.Object, _mockLogger.Object);
            var context = CreateHttpContext("/api/test", isAuthenticated: true, userId: "123");
            var user = new User { TwitchUserId = "123", DisplayName = "TestUser" };
            _mockUserRepository.Setup(x => x.GetUserAsync("123")).ReturnsAsync(user);

            // Act
            await middleware.InvokeAsync(context, _mockUserRepository.Object);

            // Assert
            _mockNext.Verify(x => x(context), Times.Once);
            Assert.Equal(user, context.Items["User"]);
        }

        [Fact]
        public async Task InvokeAsync_AuthenticatedUserNotFound_ShouldReturn401()
        {
            // Arrange
            var middleware = new AuthMiddleware(_mockNext.Object, _mockLogger.Object);
            var context = CreateHttpContext("/api/test", isAuthenticated: true, userId: "999");
            context.Response.Body = new MemoryStream();
            _mockUserRepository.Setup(x => x.GetUserAsync("999")).ReturnsAsync((User?)null);

            // Act
            await middleware.InvokeAsync(context, _mockUserRepository.Object);

            // Assert
            Assert.Equal(401, context.Response.StatusCode);
            _mockNext.Verify(x => x(context), Times.Never);
        }

        [Fact]
        public async Task InvokeAsync_AuthenticatedUserWithNoClaim_ShouldCallNext()
        {
            // Arrange
            var middleware = new AuthMiddleware(_mockNext.Object, _mockLogger.Object);
            var context = new DefaultHttpContext();
            context.Request.Path = "/api/test";

            // Create authenticated user but without userId claim
            var identity = new ClaimsIdentity(new Claim[] { }, "TestAuth");
            context.User = new ClaimsPrincipal(identity);

            // Act
            await middleware.InvokeAsync(context, _mockUserRepository.Object);

            // Assert
            _mockNext.Verify(x => x(context), Times.Once);
            _mockUserRepository.Verify(x => x.GetUserAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task InvokeAsync_DatabaseError_ShouldContinueWithoutBlocking()
        {
            // Arrange
            var middleware = new AuthMiddleware(_mockNext.Object, _mockLogger.Object);
            var context = CreateHttpContext("/api/test", isAuthenticated: true, userId: "123");
            _mockUserRepository.Setup(x => x.GetUserAsync("123")).ThrowsAsync(new Exception("DB error"));

            // Act
            await middleware.InvokeAsync(context, _mockUserRepository.Object);

            // Assert
            _mockNext.Verify(x => x(context), Times.Once);
            Assert.False(context.Items.ContainsKey("User"));
        }

        [Fact]
        public async Task InvokeAsync_RootPath_ShouldProcessNormally()
        {
            // Arrange
            var middleware = new AuthMiddleware(_mockNext.Object, _mockLogger.Object);
            var context = CreateHttpContext("/", isAuthenticated: false);

            // Act
            await middleware.InvokeAsync(context, _mockUserRepository.Object);

            // Assert
            _mockNext.Verify(x => x(context), Times.Once);
        }

        [Fact]
        public async Task InvokeAsync_WhenNextThrowsReauthRequired_ForApi_ShouldSignOutAndReturn401Json()
        {
            // Arrange
            var authService = new Mock<IAuthenticationService>();
            authService
                .Setup(s => s.SignOutAsync(It.IsAny<HttpContext>(), It.IsAny<string?>(), It.IsAny<AuthenticationProperties?>()))
                .Returns(Task.CompletedTask);

            var services = new ServiceCollection();
            services.AddSingleton(authService.Object);
            var provider = services.BuildServiceProvider();

            _mockNext
                .Setup(x => x(It.IsAny<HttpContext>()))
                .ThrowsAsync(new ReauthRequiredException("expired"));

            var middleware = new AuthMiddleware(_mockNext.Object, _mockLogger.Object);
            var context = CreateHttpContext("/api/test", isAuthenticated: false);
            context.RequestServices = provider;
            context.Request.Headers.Accept = "application/json";
            context.Response.Body = new MemoryStream();

            // Act
            await middleware.InvokeAsync(context, _mockUserRepository.Object);

            // Assert
            authService.Verify(
                s => s.SignOutAsync(
                    It.IsAny<HttpContext>(),
                    It.Is<string?>(scheme => scheme == CookieAuthenticationDefaults.AuthenticationScheme),
                    It.IsAny<AuthenticationProperties?>()),
                Times.Once);

            Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
            Assert.StartsWith("application/json", context.Response.ContentType, StringComparison.OrdinalIgnoreCase);

            context.Response.Body.Position = 0;
            var json = await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync();
            Assert.Contains("requireReauth", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("/auth/twitch", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("/auth/logout?reauth=1", json, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task InvokeAsync_WhenNextThrowsReauthRequired_ForNonApi_ShouldRedirectToLogout()
        {
            // Arrange
            var authService = new Mock<IAuthenticationService>();
            authService
                .Setup(s => s.SignOutAsync(It.IsAny<HttpContext>(), It.IsAny<string?>(), It.IsAny<AuthenticationProperties?>()))
                .Returns(Task.CompletedTask);

            var services = new ServiceCollection();
            services.AddSingleton(authService.Object);
            var provider = services.BuildServiceProvider();

            _mockNext
                .Setup(x => x(It.IsAny<HttpContext>()))
                .ThrowsAsync(new ReauthRequiredException("expired"));

            var middleware = new AuthMiddleware(_mockNext.Object, _mockLogger.Object);
            var context = CreateHttpContext("/dashboard", isAuthenticated: false);
            context.RequestServices = provider;
            context.Request.QueryString = new QueryString("?x=1");

            // Act
            await middleware.InvokeAsync(context, _mockUserRepository.Object);

            // Assert
            authService.Verify(
                s => s.SignOutAsync(
                    It.IsAny<HttpContext>(),
                    It.Is<string?>(scheme => scheme == CookieAuthenticationDefaults.AuthenticationScheme),
                    It.IsAny<AuthenticationProperties?>()),
                Times.Once);

            Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);

            var location = context.Response.Headers.Location.ToString();
            Assert.StartsWith("/auth/logout?reauth=1&returnUrl=", location, StringComparison.Ordinal);
            Assert.Contains("%2Fdashboard%3Fx%3D1", location, StringComparison.OrdinalIgnoreCase);
        }
    }
}
