using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Utilities;
using OmniForge.Web;

namespace OmniForge.Web.Services
{
    public class WebSocketOverlayManager : IWebSocketOverlayManager
    {
        // Use ConcurrentDictionary with connection ID as key for proper cleanup
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, WebSocket>> _userSockets = new();
        private readonly ILogger<WebSocketOverlayManager> _logger;
        private readonly TimeSpan _pingInterval;

        public WebSocketOverlayManager(ILogger<WebSocketOverlayManager> logger, TimeSpan? pingInterval = null)
        {
            _logger = logger;
            _pingInterval = pingInterval ?? TimeSpan.FromSeconds(30); // Keep alive every 30 seconds
        }

        public async Task HandleConnectionAsync(string userId, WebSocket webSocket)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return;
            }

            var safeUserId = userId!;
            var connectionId = Guid.NewGuid().ToString("N")[..8]; // Short ID for tracking
            var userConnections = _userSockets.GetOrAdd(safeUserId, _ => new ConcurrentDictionary<string, WebSocket>());
            userConnections.TryAdd(connectionId, webSocket);

            var activeCount = userConnections.Count(kvp => kvp.Value.State == WebSocketState.Open);
            _logger.LogInformation("🟢 Overlay WebSocket connected for user {UserId} (conn: {ConnectionId}). Active connections: {Count}",
                (safeUserId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"), connectionId, activeCount);

            var buffer = new byte[1024 * 4];
            using var pingCts = new CancellationTokenSource();

            // Send initial server info so the overlay can detect server restarts without an HTTP health check.
            // This is always from the same replica as the socket, avoiding load-balancer confusion.
            try
            {
                var infoPayload = new { method = "serverInfo", data = new { serverInstanceId = ServerInstance.Id } };
                var infoJson = System.Text.Json.JsonSerializer.Serialize(infoPayload,
                    new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                var infoBytes = Encoding.UTF8.GetBytes(infoJson);
                await webSocket.SendAsync(new ArraySegment<byte>(infoBytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Failed to send serverInfo to overlay for user {UserId} (conn: {ConnectionId})",
                    (safeUserId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"), connectionId);
            }

            // Start ping task to keep connection alive
            var pingTask = SendPingsAsync(webSocket, connectionId, safeUserId!, pingCts.Token);

            try
            {
                while (webSocket.State == WebSocketState.Open)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogInformation("🔵 Overlay WebSocket close requested by client for user {UserId} (conn: {ConnectionId}). CloseStatus: {Status}, Description: {Description}",
                            (safeUserId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"), connectionId, result.CloseStatus, (result.CloseStatusDescription ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    }
                    // Pong responses are handled automatically by the WebSocket protocol
                }
                _logger.LogInformation("🔵 Overlay WebSocket loop ended for user {UserId} (conn: {ConnectionId}). Final state: {State}",
                    (safeUserId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"), connectionId, webSocket.State);
            }
            catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
            {
                _logger.LogWarning("⚠️ Overlay WebSocket closed prematurely for user {UserId} (conn: {ConnectionId}). Error: {Error}",
                    (safeUserId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"), connectionId, ex.WebSocketErrorCode);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("🔵 Overlay WebSocket cancelled for user {UserId} (conn: {ConnectionId})", (safeUserId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"), connectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "🔴 Overlay WebSocket error for user {UserId} (conn: {ConnectionId}). State: {State}",
                    (safeUserId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"), connectionId, webSocket.State);
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
                    (safeUserId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"), connectionId, webSocket.State, remainingActive);

                // Clean up empty user entries
                if (userConnections.IsEmpty)
                {
                    _userSockets.TryRemove(safeUserId!, out _);
                }
            }
        }

        private async Task SendPingsAsync(WebSocket webSocket, string connectionId, string userId, CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && webSocket.State == WebSocketState.Open)
                {
                    await Task.Delay(_pingInterval, cancellationToken);

                    if (webSocket.State == WebSocketState.Open)
                    {
                        try
                        {
                            // Send a ping message to keep connection alive
                            var pingData = Encoding.UTF8.GetBytes("{\"method\":\"ping\",\"data\":{}}");
                            await webSocket.SendAsync(new ArraySegment<byte>(pingData), WebSocketMessageType.Text, true, cancellationToken);
                            _logger.LogDebug("📡 Sent ping to overlay for user {UserId} (conn: {ConnectionId})", (userId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"), connectionId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "⚠️ Failed to send ping to overlay for user {UserId} (conn: {ConnectionId})", (userId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"), connectionId);
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
                _logger.LogWarning(ex, "⚠️ Ping task error for user {UserId} (conn: {ConnectionId})", (userId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"), connectionId);
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
                            _logger.LogError(ex, "🔴 Error sending '{Method}' to overlay WebSocket for user {UserId} (conn: {ConnId})", (method ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"), (userId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"), connId);
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
                    // streamStatusUpdate is a periodic live heartbeat; keep it out of Info logs to avoid noise.
                    if (string.Equals(method, "streamStatusUpdate", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug("📤 Sent '{Method}' to {SentCount} overlay sockets for user {UserId} (cleaned up {ClosedCount} closed)",
                            (method ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"), sentCount, (userId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"), closedCount);
                    }
                    else
                    {
                        _logger.LogInformation("📤 Sent '{Method}' to {SentCount} overlay sockets for user {UserId} (cleaned up {ClosedCount} closed)",
                            (method ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"), sentCount, (userId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"), closedCount);
                    }
                }
            }
            else
            {
                _logger.LogDebug("📤 No overlay sockets registered for user {UserId} to send '{Method}'", (userId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"), (method ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
            }
        }
    }
}
