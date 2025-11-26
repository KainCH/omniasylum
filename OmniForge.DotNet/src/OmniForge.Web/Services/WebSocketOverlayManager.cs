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

            var connectionId = Guid.NewGuid().ToString("N")[..8]; // Short ID for tracking
            _logger.LogInformation("🟢 Overlay WebSocket connected for user {UserId} (conn: {ConnectionId}). Total connections: {Count}", 
                userId, connectionId, userSockets.Count);

            var buffer = new byte[1024 * 4];
            try
            {
                while (webSocket.State == WebSocketState.Open)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogInformation("🔵 Overlay WebSocket close requested by client for user {UserId} (conn: {ConnectionId}). CloseStatus: {Status}, Description: {Description}", 
                            userId, connectionId, result.CloseStatus, result.CloseStatusDescription);
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    }
                }
                _logger.LogInformation("🔵 Overlay WebSocket loop ended for user {UserId} (conn: {ConnectionId}). Final state: {State}", 
                    userId, connectionId, webSocket.State);
            }
            catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
            {
                _logger.LogWarning("⚠️ Overlay WebSocket closed prematurely for user {UserId} (conn: {ConnectionId}). Error: {Error}", 
                    userId, connectionId, ex.WebSocketErrorCode);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("🔵 Overlay WebSocket cancelled for user {UserId} (conn: {ConnectionId})", userId, connectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "🔴 Overlay WebSocket error for user {UserId} (conn: {ConnectionId}). State: {State}", 
                    userId, connectionId, webSocket.State);
            }
            finally
            {
                // Cleanup is tricky with ConcurrentBag, we'll just filter closed sockets on send
                _logger.LogInformation("🔴 Overlay WebSocket disconnected for user {UserId} (conn: {ConnectionId}). Final state: {State}", 
                    userId, connectionId, webSocket.State);
            }
        }

        public async Task SendToUserAsync(string userId, string method, object data)
        {
            if (_sockets.TryGetValue(userId, out var userSockets))
            {
                var payload = new { method, data };
                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var bytes = Encoding.UTF8.GetBytes(json);
                var segment = new ArraySegment<byte>(bytes);

                var activeSockets = new ConcurrentBag<WebSocket>();
                var sentCount = 0;
                var closedCount = 0;
                
                foreach (var socket in userSockets)
                {
                    if (socket.State == WebSocketState.Open)
                    {
                        try
                        {
                            await socket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
                            activeSockets.Add(socket);
                            sentCount++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "🔴 Error sending '{Method}' to overlay WebSocket for user {UserId}", method, userId);
                        }
                    }
                    else
                    {
                        closedCount++;
                    }
                }

                if (sentCount > 0 || closedCount > 0)
                {
                    _logger.LogInformation("📤 Sent '{Method}' to {SentCount} overlay sockets for user {UserId} (skipped {ClosedCount} closed)", 
                        method, sentCount, userId, closedCount);
                }
                else if (userSockets.Count == 0)
                {
                    _logger.LogDebug("📤 No overlay sockets for user {UserId} to send '{Method}'", userId, method);
                }

                // Update the bag with only active sockets
                _sockets.TryUpdate(userId, activeSockets, userSockets);
            }
            else
            {
                _logger.LogDebug("📤 No overlay sockets registered for user {UserId} to send '{Method}'", userId, method);
            }
        }
    }
}
