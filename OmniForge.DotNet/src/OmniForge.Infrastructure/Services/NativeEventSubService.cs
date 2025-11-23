using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniForge.Infrastructure.Models.EventSub;

namespace OmniForge.Infrastructure.Services
{
    public class NativeEventSubService : INativeEventSubService, IDisposable
    {
        private const string TwitchEventSubUrl = "wss://eventsub.wss.twitch.tv/ws";
        private readonly ILogger<NativeEventSubService> _logger;
        private ClientWebSocket _webSocket;
        private CancellationTokenSource _cts;
        private bool _isDisposed;

        public string? SessionId { get; private set; }
        public bool IsConnected => _webSocket?.State == WebSocketState.Open;
        public DateTime LastKeepaliveTime { get; private set; }

        public event Func<EventSubMessage, Task>? OnNotification;
        public event Func<string, Task>? OnSessionWelcome;
        public event Func<Task>? OnDisconnected;

        public NativeEventSubService(ILogger<NativeEventSubService> logger)
        {
            _logger = logger;
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
            if (_webSocket.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting", CancellationToken.None);
            }
            _cts.Cancel();
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            var buffer = new byte[8192];
            var messageBuilder = new StringBuilder();

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
                        _logger.LogWarning("Server closed the connection.");
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server closed", CancellationToken.None);
                        OnDisconnected?.Invoke();
                        break;
                    }

                    var messageJson = messageBuilder.ToString();
                    await ProcessMessageAsync(messageJson);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in WebSocket receive loop.");
                OnDisconnected?.Invoke();
            }
        }

        private async Task ProcessMessageAsync(string json)
        {
            try
            {
                var message = JsonSerializer.Deserialize<EventSubMessage>(json);
                if (message == null) return;

                switch (message.Metadata.MessageType)
                {
                    case "session_welcome":
                        SessionId = message.Payload.Session?.Id;
                        LastKeepaliveTime = DateTime.UtcNow;
                        _logger.LogInformation($"Session Welcome! ID: {SessionId}, Keepalive: {message.Payload.Session?.KeepaliveTimeoutSeconds}s");
                        if (SessionId != null)
                        {
                            OnSessionWelcome?.Invoke(SessionId);
                        }
                        break;

                    case "session_keepalive":
                        LastKeepaliveTime = DateTime.UtcNow;
                        // _logger.LogDebug("Keepalive received.");
                        break;

                    case "notification":
                        LastKeepaliveTime = DateTime.UtcNow;
                        _logger.LogInformation($"Notification received: {message.Metadata.MessageId}");
                        if (OnNotification != null)
                        {
                            await OnNotification.Invoke(message);
                        }
                        break;

                    case "reconnect":
                        _logger.LogWarning($"Reconnect requested by server. URL: {message.Payload.Session?.ReconnectUrl}");
                        // Handle reconnect logic here if needed, or just let it disconnect and reconnect via watchdog
                        // For now, we'll treat it as a disconnect trigger
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Reconnect requested", CancellationToken.None);
                        OnDisconnected?.Invoke();
                        break;

                    case "revocation":
                        _logger.LogWarning($"Subscription revoked: {message.Payload.Subscription?.Id} - {message.Payload.Subscription?.Status}");
                        break;

                    default:
                        _logger.LogWarning($"Unknown message type: {message.Metadata.MessageType}");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing WebSocket message.");
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
