using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniForge.SceneSync.Configuration;

namespace OmniForge.SceneSync.Services
{
    /// <summary>
    /// Hosted service that connects to Streamlabs Desktop (SLOBS) via its named pipe JSON-RPC API
    /// and listens for scene switch events. Includes reconnection logic with exponential backoff.
    /// </summary>
    public class StreamlabsSceneService : IHostedService, IDisposable
    {
        private readonly SceneSyncSettings _settings;
        private readonly SceneSyncOrchestrator _orchestrator;
        private readonly ILogger<StreamlabsSceneService> _logger;

        private CancellationTokenSource? _cts;
        private Task? _connectTask;
        private string? _currentScene;
        private bool _disposed;
        private int _jsonRpcId;

        private static readonly int[] BackoffSeconds = { 5, 10, 30, 60 };

        public StreamlabsSceneService(
            IOptions<SceneSyncSettings> settings,
            SceneSyncOrchestrator orchestrator,
            ILogger<StreamlabsSceneService> logger)
        {
            _settings = settings.Value;
            _orchestrator = orchestrator;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (!_settings.Streamlabs.Enabled)
            {
                _logger.LogInformation("⏭️  Streamlabs Desktop integration is disabled in configuration");
                return Task.CompletedTask;
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _connectTask = ConnectWithRetryAsync(_cts.Token);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_cts != null)
            {
                await _cts.CancelAsync();
            }

            if (_connectTask != null)
            {
                try { await _connectTask; } catch (OperationCanceledException) { }
            }
        }

        private async Task ConnectWithRetryAsync(CancellationToken ct)
        {
            var attempt = 0;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("🔄 Connecting to Streamlabs Desktop via pipe '{Pipe}'...", _settings.Streamlabs.PipeName);
                    await RunPipeSessionAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Streamlabs Desktop connection failed");
                }

                if (ct.IsCancellationRequested) break;

                var backoff = BackoffSeconds[Math.Min(attempt, BackoffSeconds.Length - 1)];
                _logger.LogInformation("⏳ Retrying Streamlabs connection in {Seconds}s (attempt {Attempt})...", backoff, attempt + 1);
                attempt++;

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(backoff), ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task RunPipeSessionAsync(CancellationToken ct)
        {
            using var pipe = new NamedPipeClientStream(
                ".", _settings.Streamlabs.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(_settings.Streamlabs.TimeoutSeconds));

            await pipe.ConnectAsync(cts.Token);
            _logger.LogInformation("✅ Connected to Streamlabs Desktop");

            // Subscribe to scene switch events via JSON-RPC
            await SubscribeToSceneSwitchAsync(pipe, ct);

            // Read the initial active scene
            await FetchActiveSceneAsync(pipe, ct);

            // Continuously read events
            var buffer = new byte[8192];
            var messageBuffer = new StringBuilder();

            while (!ct.IsCancellationRequested && pipe.IsConnected)
            {
                var bytesRead = await pipe.ReadAsync(buffer, ct);
                if (bytesRead == 0)
                {
                    _logger.LogWarning("🔌 Streamlabs pipe closed by remote");
                    break;
                }

                messageBuffer.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));

                // SLOBS sends newline-delimited JSON
                var raw = messageBuffer.ToString();
                var lines = raw.Split('\n');

                // Keep the last incomplete line in the buffer
                messageBuffer.Clear();
                if (!raw.EndsWith('\n'))
                {
                    messageBuffer.Append(lines[^1]);
                    lines = lines[..^1];
                }

                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;

                    try
                    {
                        ProcessMessage(trimmed);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Failed to process SLOBS message: {Message}", trimmed.Length > 500 ? trimmed[..500] : trimmed);
                    }
                }
            }
        }

        private async Task SubscribeToSceneSwitchAsync(NamedPipeClientStream pipe, CancellationToken ct)
        {
            var request = new
            {
                jsonrpc = "2.0",
                id = Interlocked.Increment(ref _jsonRpcId),
                method = "sceneSwitched",
                @params = new
                {
                    resource = "ScenesService",
                    args = Array.Empty<object>()
                }
            };

            var json = JsonSerializer.Serialize(request) + "\n";
            var bytes = Encoding.UTF8.GetBytes(json);
            await pipe.WriteAsync(bytes, ct);
            await pipe.FlushAsync(ct);

            _logger.LogDebug("📤 Subscribed to ScenesService.sceneSwitched");
        }

        private async Task FetchActiveSceneAsync(NamedPipeClientStream pipe, CancellationToken ct)
        {
            var request = new
            {
                jsonrpc = "2.0",
                id = Interlocked.Increment(ref _jsonRpcId),
                method = "activeScene",
                @params = new
                {
                    resource = "ScenesService",
                    args = Array.Empty<object>()
                }
            };

            var json = JsonSerializer.Serialize(request) + "\n";
            var bytes = Encoding.UTF8.GetBytes(json);
            await pipe.WriteAsync(bytes, ct);
            await pipe.FlushAsync(ct);

            _logger.LogDebug("📤 Requested active scene from ScenesService");
        }

        private void ProcessMessage(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // JSON-RPC response (from our activeScene request)
            if (root.TryGetProperty("result", out var result))
            {
                if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("name", out var name))
                {
                    var sceneName = name.GetString();
                    if (!string.IsNullOrEmpty(sceneName))
                    {
                        _currentScene = sceneName;
                        _logger.LogInformation("🎬 Current Streamlabs scene: {Scene}", _currentScene);
                    }
                }
                return;
            }

            // JSON-RPC event (scene switch notification)
            if (root.TryGetProperty("method", out var method) ||
                root.TryGetProperty("result", out _) == false)
            {
                // Check for event-style messages
                // SLOBS events come as: {"jsonrpc":"2.0","result":{"_type":"EVENT","resourceId":"ScenesService.sceneSwitched","data":{...}}}
                // OR as top-level: {"_type":"EVENT","resourceId":"ScenesService.sceneSwitched","data":{...}}
                JsonElement dataElement = default;
                string? resourceId = null;

                if (root.TryGetProperty("_type", out var typeEl) && typeEl.GetString() == "EVENT")
                {
                    root.TryGetProperty("resourceId", out var resId);
                    resourceId = resId.GetString();
                    root.TryGetProperty("data", out dataElement);
                }
                else if (root.TryGetProperty("result", out var resultObj) &&
                         resultObj.ValueKind == JsonValueKind.Object &&
                         resultObj.TryGetProperty("_type", out var innerType) &&
                         innerType.GetString() == "EVENT")
                {
                    resultObj.TryGetProperty("resourceId", out var resId);
                    resourceId = resId.GetString();
                    resultObj.TryGetProperty("data", out dataElement);
                }

                if (resourceId?.Contains("sceneSwitched", StringComparison.OrdinalIgnoreCase) == true &&
                    dataElement.ValueKind == JsonValueKind.Object)
                {
                    string? sceneName = null;
                    if (dataElement.TryGetProperty("name", out var sceneNameProp))
                    {
                        sceneName = sceneNameProp.GetString();
                    }

                    if (!string.IsNullOrEmpty(sceneName))
                    {
                        var previous = _currentScene;
                        _currentScene = sceneName;
                        _logger.LogInformation("🎬 Streamlabs scene changed: {Previous} → {Current}", previous ?? "(none)", _currentScene);

                        _ = _orchestrator.ReportSceneChangeAsync(_currentScene, previous, "Streamlabs");
                    }
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cts?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
