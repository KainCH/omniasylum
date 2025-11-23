using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using OmniForge.Core.Interfaces;

namespace OmniForge.Web.Services
{
    public class WebSocketOverlayManager : IWebSocketOverlayManager
    {
        private readonly ConcurrentDictionary<string, ConcurrentBag<WebSocket>> _sockets = new();
        private readonly ILogger<WebSocketOverlayManager> _logger;

        public WebSocketOverlayManager(ILogger<WebSocketOverlayManager> logger)
        {
            _logger = logger;
        }

        public async Task HandleConnectionAsync(string userId, WebSocket webSocket)
        {
            var userSockets = _sockets.GetOrAdd(userId, _ => new ConcurrentBag<WebSocket>());
            userSockets.Add(webSocket);

            _logger.LogInformation("WebSocket connected for user {UserId}", userId);

            var buffer = new byte[1024 * 4];
            try
            {
                while (webSocket.State == WebSocketState.Open)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    }
                }
            }
            catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
            {
                _logger.LogInformation("WebSocket closed prematurely for user {UserId}", userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WebSocket error for user {UserId}", userId);
            }
            finally
            {
                // Cleanup is tricky with ConcurrentBag, we'll just filter closed sockets on send
                _logger.LogInformation("WebSocket disconnected for user {UserId}", userId);
            }
        }

        public async Task SendToUserAsync(string userId, string method, object data)
        {
            if (_sockets.TryGetValue(userId, out var userSockets))
            {
                var payload = new { method, data };
                var json = JsonSerializer.Serialize(payload);
                var bytes = Encoding.UTF8.GetBytes(json);
                var segment = new ArraySegment<byte>(bytes);

                var activeSockets = new ConcurrentBag<WebSocket>();
                foreach (var socket in userSockets)
                {
                    if (socket.State == WebSocketState.Open)
                    {
                        try
                        {
                            await socket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
                            activeSockets.Add(socket);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error sending to WebSocket for user {UserId}", userId);
                        }
                    }
                }

                // Update the bag with only active sockets
                _sockets.TryUpdate(userId, activeSockets, userSockets);
            }
        }
    }
}
