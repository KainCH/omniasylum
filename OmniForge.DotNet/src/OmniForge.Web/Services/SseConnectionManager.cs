using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OmniForge.Web.Services
{
    public class SseClient
    {
        public string ConnectionId { get; init; } = string.Empty;
        public Stream ResponseBody { get; init; } = Stream.Null;
        public CancellationTokenSource Cts { get; init; } = new();
        public bool IsReady { get; set; }
    }

    public class SseConnectionManager : IHostedService, IDisposable
    {
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, SseClient>> _connections = new();
        private readonly ILogger<SseConnectionManager> _logger;
        private Timer? _keepaliveTimer;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public SseConnectionManager(ILogger<SseConnectionManager> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Registers a new SSE connection for a user.
        /// Sends the initial "connected" event with the assigned connectionId.
        /// Returns the connectionId.
        /// </summary>
        public async Task<string> RegisterAsync(string userId, Stream responseBody, CancellationToken ct)
        {
            var connectionId = Guid.NewGuid().ToString("N")[..8];

            var client = new SseClient
            {
                ConnectionId = connectionId,
                ResponseBody = responseBody,
                Cts = CancellationTokenSource.CreateLinkedTokenSource(ct)
            };

            var userConnections = _connections.GetOrAdd(userId, _ => new ConcurrentDictionary<string, SseClient>());
            userConnections[connectionId] = client;

            _logger.LogInformation("SSE connected: user_id={UserId}, connection_id={ConnectionId}", userId, connectionId);

            // Send retry interval (5 seconds) and the connected event
            await WriteRawAsync(client, $"retry: 5000\n\n");
            await WriteEventAsync(client, "connected", new { connectionId });

            return connectionId;
        }

        /// <summary>
        /// Marks a connection as ready and sends the init bundle.
        /// Called when the client POSTs to the ready endpoint.
        /// </summary>
        public async Task<bool> SendToConnectionAsync(string userId, string connectionId, string eventType, object data)
        {
            if (!_connections.TryGetValue(userId, out var userConnections))
                return false;
            if (!userConnections.TryGetValue(connectionId, out var client))
                return false;

            if (eventType == "init")
                client.IsReady = true;

            await WriteEventAsync(client, eventType, data);
            return true;
        }

        /// <summary>
        /// Broadcasts an SSE event to all ready connections for a user.
        /// </summary>
        public async Task SendEventAsync(string userId, string eventType, object data)
        {
            if (!_connections.TryGetValue(userId, out var userConnections))
                return;

            foreach (var kvp in userConnections)
            {
                var client = kvp.Value;
                if (!client.IsReady) continue;

                try
                {
                    await WriteEventAsync(client, eventType, data);
                }
                catch (Exception)
                {
                    // Client likely disconnected; remove it
                    RemoveClient(userId, kvp.Key);
                }
            }
        }

        /// <summary>
        /// Removes a specific client connection and cleans up the user entry if empty.
        /// </summary>
        public void RemoveClient(string userId, string connectionId)
        {
            if (!_connections.TryGetValue(userId, out var userConnections))
                return;

            if (userConnections.TryRemove(connectionId, out var client))
            {
                _logger.LogInformation("SSE disconnected: user_id={UserId}, connection_id={ConnectionId}", userId, connectionId);
                try { client.Cts.Cancel(); } catch { }
                try { client.Cts.Dispose(); } catch { }
            }

            if (userConnections.IsEmpty)
            {
                _connections.TryRemove(userId, out _);
            }
        }

        /// <summary>
        /// Returns the number of active connections for a user.
        /// </summary>
        public int GetConnectionCount(string userId)
        {
            return _connections.TryGetValue(userId, out var userConnections) ? userConnections.Count : 0;
        }

        private static async Task WriteEventAsync(SseClient client, string eventType, object data)
        {
            var json = JsonSerializer.Serialize(data, JsonOptions);
            var message = $"event: {eventType}\ndata: {json}\n\n";
            await WriteRawAsync(client, message);
        }

        private static async Task WriteRawAsync(SseClient client, string raw)
        {
            var bytes = Encoding.UTF8.GetBytes(raw);
            await client.ResponseBody.WriteAsync(bytes, client.Cts.Token);
            await client.ResponseBody.FlushAsync(client.Cts.Token);
        }

        // IHostedService — manages the keepalive timer

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // Send a keepalive comment every 30 seconds to prevent proxy/CDN timeouts
            _keepaliveTimer = new Timer(SendKeepalives, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _keepaliveTimer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        private void SendKeepalives(object? state)
        {
            foreach (var userKvp in _connections)
            {
                foreach (var clientKvp in userKvp.Value)
                {
                    var client = clientKvp.Value;
                    try
                    {
                        // SSE comment (starts with colon) — not a named event, just keeps the connection alive
                        var bytes = Encoding.UTF8.GetBytes(": keepalive\n\n");
                        client.ResponseBody.WriteAsync(bytes, client.Cts.Token).AsTask().GetAwaiter().GetResult();
                        client.ResponseBody.FlushAsync(client.Cts.Token).GetAwaiter().GetResult();
                    }
                    catch
                    {
                        RemoveClient(userKvp.Key, clientKvp.Key);
                    }
                }
            }
        }

        public void Dispose()
        {
            _keepaliveTimer?.Dispose();
        }
    }
}
