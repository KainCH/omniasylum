using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using OmniForge.Core.Interfaces;

namespace OmniForge.Web.Services
{
    public class WebSocketOverlayManager : IWebSocketOverlayManager
    {
        // Use ConcurrentDictionary with connection ID as key for proper cleanup
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, WebSocket>> _userSockets = new();
        private readonly ILogger<WebSocketOverlayManager> _logger;
        private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(30); // Keep alive every 30 seconds

        public WebSocketOverlayManager(ILogger<WebSocketOverlayManager> logger)
        {
            _logger = logger;
        }

        public async Task HandleConnectionAsync(string userId, WebSocket webSocket)
        {
            var connectionId = Guid.NewGuid().ToString("N")[..8]; // Short ID for tracking
            var userConnections = _userSockets.GetOrAdd(userId, _ => new ConcurrentDictionary<string, WebSocket>());
            userConnections.TryAdd(connectionId, webSocket);

            var activeCount = userConnections.Count(kvp => kvp.Value.State == WebSocketState.Open);
            _logger.LogInformation("🟢 Overlay WebSocket connected for user {UserId} (conn: {ConnectionId}). Active connections: {Count}",
                userId, connectionId, activeCount);

            var buffer = new byte[1024 * 4];
            using var pingCts = new CancellationTokenSource();

            // Start ping task to keep connection alive
            var pingTask = SendPingsAsync(webSocket, connectionId, userId, pingCts.Token);

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
                    // Pong responses are handled automatically by the WebSocket protocol
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
                // Stop ping task
                pingCts.Cancel();
                try { await pingTask; } catch { /* ignore */ }

                // Proper cleanup - remove this specific connection
                userConnections.TryRemove(connectionId, out _);
                var remainingActive = userConnections.Count(kvp => kvp.Value.State == WebSocketState.Open);

                _logger.LogInformation("🔴 Overlay WebSocket disconnected for user {UserId} (conn: {ConnectionId}). Final state: {State}. Remaining active: {Count}",
                    userId, connectionId, webSocket.State, remainingActive);

                // Clean up empty user entries
                if (userConnections.IsEmpty)
                {
                    _userSockets.TryRemove(userId, out _);
                }
            }
        }

        private async Task SendPingsAsync(WebSocket webSocket, string connectionId, string userId, CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && webSocket.State == WebSocketState.Open)
                {
                    await Task.Delay(PingInterval, cancellationToken);

                    if (webSocket.State == WebSocketState.Open)
                    {
                        try
                        {
                            // Send a ping message to keep connection alive
                            var pingData = Encoding.UTF8.GetBytes("{\"method\":\"ping\",\"data\":{}}");
                            await webSocket.SendAsync(new ArraySegment<byte>(pingData), WebSocketMessageType.Text, true, cancellationToken);
                            _logger.LogDebug("📡 Sent ping to overlay for user {UserId} (conn: {ConnectionId})", userId, connectionId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "⚠️ Failed to send ping to overlay for user {UserId} (conn: {ConnectionId})", userId, connectionId);
                            break;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Ping task error for user {UserId} (conn: {ConnectionId})", userId, connectionId);
            }
        }

        public async Task SendToUserAsync(string userId, string method, object data)
        {
            if (_userSockets.TryGetValue(userId, out var userConnections))
            {
                var payload = new { method, data };
                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var bytes = Encoding.UTF8.GetBytes(json);
                var segment = new ArraySegment<byte>(bytes);

                var sentCount = 0;
                var closedCount = 0;
                var toRemove = new List<string>();

                foreach (var kvp in userConnections)
                {
                    var connId = kvp.Key;
                    var socket = kvp.Value;

                    if (socket.State == WebSocketState.Open)
                    {
                        try
                        {
                            await socket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
                            sentCount++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "🔴 Error sending '{Method}' to overlay WebSocket for user {UserId} (conn: {ConnId})", method, userId, connId);
                            toRemove.Add(connId);
                        }
                    }
                    else
                    {
                        closedCount++;
                        toRemove.Add(connId);
                    }
                }

                // Clean up dead connections
                foreach (var connId in toRemove)
                {
                    userConnections.TryRemove(connId, out _);
                }

                if (sentCount > 0 || closedCount > 0)
                {
                    _logger.LogInformation("📤 Sent '{Method}' to {SentCount} overlay sockets for user {UserId} (cleaned up {ClosedCount} closed)",
                        method, sentCount, userId, closedCount);
                }
            }
            else
            {
                _logger.LogDebug("📤 No overlay sockets registered for user {UserId} to send '{Method}'", userId, method);
            }
        }
    }
}
