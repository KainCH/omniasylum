using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Moq;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Middleware;
using Xunit;

namespace OmniForge.Tests.Middleware
{
    public class WebSocketOverlayMiddlewareTests
    {
        private readonly Mock<IWebSocketOverlayManager> _mockWebSocketManager;
        private readonly Mock<RequestDelegate> _mockNext;

        public WebSocketOverlayMiddlewareTests()
        {
            _mockWebSocketManager = new Mock<IWebSocketOverlayManager>();
            _mockNext = new Mock<RequestDelegate>();
            _mockNext.Setup(x => x(It.IsAny<HttpContext>())).Returns(Task.CompletedTask);
        }

        private static HttpContext CreateHttpContext(string path, bool isWebSocket = false, string? userId = null)
        {
            var context = new DefaultHttpContext();
            context.Request.Path = path;

            if (userId != null)
            {
                context.Request.QueryString = new QueryString($"?userId={userId}");
            }

            if (isWebSocket)
            {
                var mockWebSocketManager = new Mock<WebSocketManager>();
                mockWebSocketManager.Setup(x => x.IsWebSocketRequest).Returns(true);

                var mockWebSocket = new Mock<WebSocket>();
                mockWebSocket.Setup(x => x.State).Returns(WebSocketState.Open);
                mockWebSocketManager
                    .Setup(x => x.AcceptWebSocketAsync())
                    .ReturnsAsync(mockWebSocket.Object);

                // Store the mock for later verification
                context.Items["MockWebSocket"] = mockWebSocket.Object;
                context.Items["MockWebSocketManager"] = mockWebSocketManager;

                // Set up the WebSocket feature
                var mockFeature = new Mock<IHttpWebSocketFeature>();
                mockFeature.Setup(x => x.IsWebSocketRequest).Returns(true);
                mockFeature
                    .Setup(x => x.AcceptAsync(It.IsAny<WebSocketAcceptContext>()))
                    .ReturnsAsync(mockWebSocket.Object);
                context.Features.Set(mockFeature.Object);
            }

            return context;
        }

        [Fact]
        public async Task InvokeAsync_NonOverlayPath_ShouldCallNext()
        {
            // Arrange
            var middleware = new WebSocketOverlayMiddleware(_mockNext.Object, _mockWebSocketManager.Object);
            var context = CreateHttpContext("/api/test");

            // Act
            await middleware.InvokeAsync(context);

            // Assert
            _mockNext.Verify(x => x(context), Times.Once);
            _mockWebSocketManager.Verify(x => x.HandleConnectionAsync(It.IsAny<string>(), It.IsAny<WebSocket>()), Times.Never);
        }

        [Fact]
        public async Task InvokeAsync_OverlayPathNotWebSocket_ShouldReturn400()
        {
            // Arrange
            var middleware = new WebSocketOverlayMiddleware(_mockNext.Object, _mockWebSocketManager.Object);
            var context = CreateHttpContext("/ws/overlay", isWebSocket: false);

            // Act
            await middleware.InvokeAsync(context);

            // Assert
            Assert.Equal(400, context.Response.StatusCode);
            _mockNext.Verify(x => x(context), Times.Never);
        }

        [Fact]
        public async Task InvokeAsync_WebSocketWithoutUserId_ShouldReturn400()
        {
            // Arrange
            var middleware = new WebSocketOverlayMiddleware(_mockNext.Object, _mockWebSocketManager.Object);
            var context = CreateHttpContext("/ws/overlay", isWebSocket: true, userId: null);

            // Act
            await middleware.InvokeAsync(context);

            // Assert
            Assert.Equal(400, context.Response.StatusCode);
            _mockWebSocketManager.Verify(x => x.HandleConnectionAsync(It.IsAny<string>(), It.IsAny<WebSocket>()), Times.Never);
        }

        [Fact]
        public async Task InvokeAsync_WebSocketWithEmptyUserId_ShouldReturn400()
        {
            // Arrange
            var middleware = new WebSocketOverlayMiddleware(_mockNext.Object, _mockWebSocketManager.Object);
            var context = CreateHttpContext("/ws/overlay", isWebSocket: true, userId: "");

            // Act
            await middleware.InvokeAsync(context);

            // Assert
            Assert.Equal(400, context.Response.StatusCode);
        }

        [Theory]
        [InlineData("/")]
        [InlineData("/api/counters")]
        [InlineData("/overlay")]
        [InlineData("/ws")]
        [InlineData("/ws/other")]
        public async Task InvokeAsync_OtherPaths_ShouldPassThrough(string path)
        {
            // Arrange
            var middleware = new WebSocketOverlayMiddleware(_mockNext.Object, _mockWebSocketManager.Object);
            var context = CreateHttpContext(path);

            // Act
            await middleware.InvokeAsync(context);

            // Assert
            _mockNext.Verify(x => x(context), Times.Once);
        }
    }
}
