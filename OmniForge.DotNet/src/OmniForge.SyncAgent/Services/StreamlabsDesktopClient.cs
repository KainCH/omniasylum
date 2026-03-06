using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using OmniForge.SyncAgent.Abstractions;

namespace OmniForge.SyncAgent.Services
{
    public class StreamlabsDesktopClient : IStreamingSoftwareClient, IDisposable
    {
        private readonly string? _token;
        private readonly ILogger<StreamlabsDesktopClient> _logger;
        private NamedPipeClientStream? _pipe;
        private StreamReader? _reader;
        private StreamWriter? _writer;
        private CancellationTokenSource? _cts;
        private bool _connected;
        private bool _intentionalDisconnect;
        private int _nextId = 1;

        public bool IsConnected => _connected;
        public string SoftwareType => "streamlabs";

        public event Action<string>? SceneChanged;
        public event Action<string[]>? SceneListUpdated;
        public event Action? Connected;
        public event Action<string>? Disconnected;

        public StreamlabsDesktopClient(IConfiguration config, ILogger<StreamlabsDesktopClient> logger)
        {
            _logger = logger;
            _token = config.GetValue<string?>("Streamlabs:Token");
        }

        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            _intentionalDisconnect = false;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            await ConnectPipeAsync(_cts.Token);
        }

        private async Task ConnectPipeAsync(CancellationToken ct)
        {
            var delay = TimeSpan.FromSeconds(2);
            var maxDelay = TimeSpan.FromSeconds(60);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    _pipe = new NamedPipeClientStream(".", "slobs", PipeDirection.InOut, PipeOptions.Asynchronous);
                    await _pipe.ConnectAsync(5000, ct);

                    _reader = new StreamReader(_pipe, Encoding.UTF8);
                    _writer = new StreamWriter(_pipe, Encoding.UTF8) { AutoFlush = true };

                    // Auth if token provided
                    if (!string.IsNullOrEmpty(_token))
                    {
                        await SendRequestAsync("auth", "TcpServerService", new[] { _token });
                    }

                    _connected = true;
                    _logger.LogInformation("Connected to Streamlabs Desktop via named pipe");
                    Connected?.Invoke();

                    // Subscribe to scene switches
                    await SendRequestAsync("sceneSwitched", "ScenesService");

                    // Start listening for events
                    _ = ListenLoopAsync(_cts!.Token);
                    return;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Failed to connect to Streamlabs Desktop pipe");
                    CleanupPipe();
                    await Task.Delay(delay, ct);
                    delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, maxDelay.TotalSeconds));
                }
            }
        }

        public async Task DisconnectAsync()
        {
            _intentionalDisconnect = true;
            _cts?.Cancel();
            CleanupPipe();
            _connected = false;
            await Task.CompletedTask;
        }

        public async Task<string[]> GetScenesAsync()
        {
            var response = await SendRequestAsync("getScenes", "ScenesService");
            if (response == null) return Array.Empty<string>();

            try
            {
                using var doc = JsonDocument.Parse(response);
                var result = doc.RootElement.GetProperty("result");
                var names = new List<string>();
                foreach (var scene in result.EnumerateArray())
                {
                    if (scene.TryGetProperty("name", out var name))
                    {
                        names.Add(name.GetString() ?? "");
                    }
                }
                var sceneNames = names.ToArray();
                SceneListUpdated?.Invoke(sceneNames);
                return sceneNames;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse getScenes response");
                return Array.Empty<string>();
            }
        }

        public async Task<string?> GetActiveSceneAsync()
        {
            var response = await SendRequestAsync("activeScene", "ScenesService");
            if (response == null) return null;

            try
            {
                using var doc = JsonDocument.Parse(response);
                return doc.RootElement.GetProperty("result").GetProperty("name").GetString();
            }
            catch
            {
                return null;
            }
        }

        private async Task<string?> SendRequestAsync(string method, string resource, object[]? args = null)
        {
            if (_writer == null || _pipe == null || !_pipe.IsConnected) return null;

            var id = Interlocked.Increment(ref _nextId);
            var request = new
            {
                jsonrpc = "2.0",
                id,
                method,
                @params = new { resource, args = args ?? Array.Empty<object>() }
            };

            var json = JsonSerializer.Serialize(request);
            await _writer.WriteLineAsync(json);

            // For subscription requests we don't wait for a direct response
            if (method == "sceneSwitched" || method == "auth") return null;

            // Read response (simplified — in production, correlate by id)
            if (_reader == null) return null;
            var line = await _reader.ReadLineAsync();
            return line;
        }

        private async Task ListenLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && _reader != null)
                {
                    var line = await _reader.ReadLineAsync(ct);
                    if (line == null) break;

                    ProcessMessage(line);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Streamlabs listen loop error");
            }
            finally
            {
                if (_connected)
                {
                    _connected = false;
                    Disconnected?.Invoke("Pipe closed");

                    if (!_intentionalDisconnect && _cts is { IsCancellationRequested: false })
                    {
                        _ = ConnectPipeAsync(_cts.Token);
                    }
                }
            }
        }

        private void ProcessMessage(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Event messages from subscriptions
                if (root.TryGetProperty("result", out var result) &&
                    result.ValueKind == JsonValueKind.Object &&
                    result.TryGetProperty("_type", out var type) &&
                    type.GetString() == "EVENT")
                {
                    if (result.TryGetProperty("data", out var data) &&
                        data.TryGetProperty("name", out var name))
                    {
                        var sceneName = name.GetString();
                        if (!string.IsNullOrEmpty(sceneName))
                        {
                            _logger.LogInformation("Streamlabs scene changed to: {Scene}", sceneName);
                            SceneChanged?.Invoke(sceneName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not parse Streamlabs message");
            }
        }

        private void CleanupPipe()
        {
            _reader?.Dispose();
            _writer?.Dispose();
            _pipe?.Dispose();
            _reader = null;
            _writer = null;
            _pipe = null;
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            CleanupPipe();
        }
    }
}
