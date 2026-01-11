using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using OmniForge.Web.Services;
using Xunit;

namespace OmniForge.Tests
{
    public class WebSocketOverlayManagerTests
    {
        private sealed class CloseImmediatelyWebSocket : WebSocket
        {
            private WebSocketState _state = WebSocketState.Open;
            private WebSocketCloseStatus? _closeStatus;
            private string? _closeStatusDescription;

            public override WebSocketCloseStatus? CloseStatus => _closeStatus;
            public override string? CloseStatusDescription => _closeStatusDescription;
            public override WebSocketState State => _state;
            public override string? SubProtocol => null;

            public override void Abort()
            {
                _state = WebSocketState.Aborted;
            }

            public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
            {
                _closeStatus = closeStatus;
                _closeStatusDescription = statusDescription;
                _state = WebSocketState.Closed;
                return Task.CompletedTask;
            }

            public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
                => CloseAsync(closeStatus, statusDescription, cancellationToken);

            public override void Dispose()
            {
                _state = WebSocketState.Closed;
            }

            public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
            {
                // Immediately request a close to exit the receive loop.
                _state = WebSocketState.CloseReceived;
                return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true, WebSocketCloseStatus.NormalClosure, "done"));
            }

            public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
                => Task.CompletedTask;
        }

        [Fact]
        public async Task HandleConnectionAsync_WhenClientCloses_ShouldExitAndCleanup()
        {
            var logger = new Mock<ILogger<WebSocketOverlayManager>>();
            var manager = new WebSocketOverlayManager(logger.Object);

            using var socket = new CloseImmediatelyWebSocket();

            await manager.HandleConnectionAsync("user1", socket);

            Assert.Equal(WebSocketState.Closed, socket.State);
        }
    }
}
