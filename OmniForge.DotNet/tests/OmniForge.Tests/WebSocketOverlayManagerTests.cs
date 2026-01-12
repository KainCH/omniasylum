using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
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

        private sealed class ControlledWebSocket : WebSocket
        {
            private WebSocketState _state = WebSocketState.Open;
            private WebSocketCloseStatus? _closeStatus;
            private string? _closeStatusDescription;
            private readonly TaskCompletionSource _receiveStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<WebSocketReceiveResult> _nextReceive = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public List<string> SentTextMessages { get; } = new();
            public bool ThrowOnSend { get; set; }

            public Task ReceiveStarted => _receiveStarted.Task;

            public override WebSocketCloseStatus? CloseStatus => _closeStatus;
            public override string? CloseStatusDescription => _closeStatusDescription;
            public override WebSocketState State => _state;
            public override string? SubProtocol => null;

            public void RequestClientClose(string? description = "done")
            {
                _state = WebSocketState.CloseReceived;
                _nextReceive.TrySetResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true, WebSocketCloseStatus.NormalClosure, description));
            }

            public void SetState(WebSocketState state)
            {
                _state = state;
            }

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

            public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
            {
                _receiveStarted.TrySetResult();
                using var reg = cancellationToken.Register(() => _nextReceive.TrySetCanceled(cancellationToken));
                return await _nextReceive.Task;
            }

            public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
            {
                if (ThrowOnSend)
                {
                    throw new InvalidOperationException("send failed");
                }

                if (messageType == WebSocketMessageType.Text)
                {
                    SentTextMessages.Add(Encoding.UTF8.GetString(buffer));
                }
                return Task.CompletedTask;
            }
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

        [Fact]
        public async Task SendToUserAsync_WhenUserHasOpenSocket_ShouldSendPayload()
        {
            var logger = new Mock<ILogger<WebSocketOverlayManager>>();
            var manager = new WebSocketOverlayManager(logger.Object);

            var socket = new ControlledWebSocket();
            var connectionTask = manager.HandleConnectionAsync("user1", socket);

            await socket.ReceiveStarted.WaitAsync(TimeSpan.FromSeconds(2));

            await manager.SendToUserAsync("user1", "update", new { X = 1 });

            Assert.Single(socket.SentTextMessages);
            Assert.Contains("\"method\":\"update\"", socket.SentTextMessages[0]);
            Assert.Contains("\"x\":1", socket.SentTextMessages[0]);

            socket.RequestClientClose();
            await connectionTask.WaitAsync(TimeSpan.FromSeconds(2));
        }

        [Fact]
        public async Task SendToUserAsync_WhenSocketSendThrows_ShouldNotThrow()
        {
            var logger = new Mock<ILogger<WebSocketOverlayManager>>();
            var manager = new WebSocketOverlayManager(logger.Object);

            var socket = new ControlledWebSocket { ThrowOnSend = true };
            var connectionTask = manager.HandleConnectionAsync("user1", socket);

            await socket.ReceiveStarted.WaitAsync(TimeSpan.FromSeconds(2));

            await manager.SendToUserAsync("user1", "update", new { X = 1 });

            socket.RequestClientClose();
            await connectionTask.WaitAsync(TimeSpan.FromSeconds(2));
        }

        [Fact]
        public async Task SendToUserAsync_WhenNoSocketsRegistered_ShouldNotThrow()
        {
            var logger = new Mock<ILogger<WebSocketOverlayManager>>();
            var manager = new WebSocketOverlayManager(logger.Object);

            await manager.SendToUserAsync("missing", "noop", new { });
        }
    }
}
