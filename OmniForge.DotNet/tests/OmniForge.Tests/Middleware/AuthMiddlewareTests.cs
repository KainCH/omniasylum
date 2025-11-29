using System;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Moq;
using OmniForge.Core.Entities;
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
    }
}
