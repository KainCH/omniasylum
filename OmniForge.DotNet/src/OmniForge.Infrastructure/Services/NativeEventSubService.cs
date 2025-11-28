using System;
using System.Diagnostics.CodeAnalysis;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniForge.Infrastructure.Models.EventSub;

namespace OmniForge.Infrastructure.Services
{
    /// <summary>
    /// Native WebSocket implementation for Twitch EventSub.
    /// WebSocket I/O code is excluded from coverage as it's infrastructure code
    /// that's difficult to unit test. Message processing logic is in EventSubMessageProcessor.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class NativeEventSubService : INativeEventSubService, IDisposable
    {
        private const string TwitchEventSubUrl = "wss://eventsub.wss.twitch.tv/ws";
        private readonly ILogger<NativeEventSubService> _logger;
        private readonly IEventSubMessageProcessor _messageProcessor;
        private ClientWebSocket _webSocket;
        private CancellationTokenSource _cts;
        private bool _isDisposed;

        public string? SessionId { get; private set; }
        public bool IsConnected => _webSocket?.State == WebSocketState.Open;
        public DateTime LastKeepaliveTime { get; private set; }

        public event Func<EventSubMessage, Task>? OnNotification;
        public event Func<string, Task>? OnSessionWelcome;
        public event Func<Task>? OnDisconnected;

        public NativeEventSubService(
            ILogger<NativeEventSubService> logger,
            IEventSubMessageProcessor messageProcessor)
        {
            _logger = logger;
            _messageProcessor = messageProcessor;
            _webSocket = new ClientWebSocket();
            _cts = new CancellationTokenSource();
        }

        public async Task ConnectAsync()
        {
            if (_webSocket.State == WebSocketState.Open) return;

            if (_webSocket.State == WebSocketState.Aborted || _webSocket.State == WebSocketState.Closed)
            {
                _webSocket.Dispose();
                _webSocket = new ClientWebSocket();
            }

            if (_cts.IsCancellationRequested)
            {
                _cts.Dispose();
                _cts = new CancellationTokenSource();
            }

            try
            {
                _logger.LogInformation("Connecting to Twitch EventSub WebSocket...");
                await _webSocket.ConnectAsync(new Uri(TwitchEventSubUrl), _cts.Token);
                _logger.LogInformation("Connected to Twitch EventSub WebSocket.");

                // Start receiving loop
                _ = ReceiveLoopAsync(_cts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to Twitch EventSub WebSocket.");
                throw;
            }
        }

        public async Task DisconnectAsync()
        {
            _logger.LogInformation("🔌 DisconnectAsync called. Current state: {State}, SessionId: {SessionId}",
                _webSocket.State, SessionId ?? "(null)");

            if (_webSocket.State == WebSocketState.Open)
            {
                _logger.LogInformation("🔌 Closing EventSub WebSocket gracefully...");
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "User requested disconnect", CancellationToken.None);
                _logger.LogInformation("🔌 EventSub WebSocket closed");
            }
            else
            {
                _logger.LogInformation("🔌 WebSocket not open (state: {State}), skipping close", _webSocket.State);
            }

            SessionId = null; // Clear session ID on disconnect
            _cts.Cancel();
            _logger.LogInformation("✅ EventSub disconnected. SessionId cleared, CancellationToken cancelled.");
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            var buffer = new byte[8192];
            var messageBuilder = new StringBuilder();

            _logger.LogInformation("📡 EventSub receive loop started. WebSocket state: {State}", _webSocket.State);

            try
            {
                while (_webSocket.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    WebSocketReceiveResult result;
                    messageBuilder.Clear();

                    do
                    {
                        result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                        messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogWarning("🔌 Server closed the EventSub WebSocket connection. CloseStatus: {Status}, Description: {Description}",
                            result.CloseStatus, result.CloseStatusDescription);
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server closed", CancellationToken.None);
                        OnDisconnected?.Invoke();
                        break;
                    }

                    var messageJson = messageBuilder.ToString();
                    await ProcessMessageAsync(messageJson);
                }

                _logger.LogWarning("📡 EventSub receive loop ended. WebSocket state: {State}, Cancellation requested: {Cancelled}",
                    _webSocket.State, token.IsCancellationRequested);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("📡 EventSub receive loop cancelled (normal shutdown)");
            }
            catch (WebSocketException wsEx)
            {
                _logger.LogError(wsEx, "🔴 WebSocket error in EventSub receive loop. State: {State}, ErrorCode: {ErrorCode}",
                    _webSocket.State, wsEx.WebSocketErrorCode);
                OnDisconnected?.Invoke();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "🔴 Error in EventSub WebSocket receive loop. State: {State}", _webSocket.State);
                OnDisconnected?.Invoke();
            }
        }

        private async Task ProcessMessageAsync(string json)
        {
            var result = _messageProcessor.Process(json);

            // Update keepalive time for all message types except unknown
            if (result.MessageType != EventSubMessageType.Unknown)
            {
                LastKeepaliveTime = DateTime.UtcNow;
            }

            switch (result.MessageType)
            {
                case EventSubMessageType.SessionWelcome:
                    SessionId = result.SessionId;
                    if (SessionId != null)
                    {
                        OnSessionWelcome?.Invoke(SessionId);
                    }
                    break;

                case EventSubMessageType.Notification:
                    if (OnNotification != null && result.Message != null)
                    {
                        await OnNotification.Invoke(result.Message);
                    }
                    break;

                case EventSubMessageType.Reconnect:
                    if (result.RequiresDisconnect)
                    {
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Reconnect requested", CancellationToken.None);
                        OnDisconnected?.Invoke();
                    }
                    break;

                case EventSubMessageType.SessionKeepalive:
                case EventSubMessageType.Revocation:
                case EventSubMessageType.Unknown:
                default:
                    // No additional action needed
                    break;
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _cts.Cancel();
            _webSocket.Dispose();
            _cts.Dispose();
        }
    }
}
