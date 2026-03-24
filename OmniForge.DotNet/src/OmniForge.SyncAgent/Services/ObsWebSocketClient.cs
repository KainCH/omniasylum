using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Types.Events;
using OmniForge.SyncAgent.Abstractions;

namespace OmniForge.SyncAgent.Services
{
    public class ObsWebSocketClient : IStreamingSoftwareClient, IDisposable
    {
        private readonly OBSWebsocket _obs = new();
        private readonly string _url;
        private string? _password;
        private readonly ILogger<ObsWebSocketClient> _logger;
        private CancellationTokenSource? _reconnectCts;
        private bool _intentionalDisconnect;

        public bool IsConnected => _obs.IsConnected;
        public string SoftwareType => "obs";

        /// <summary>Raised when authentication fails (wrong or missing password).</summary>
        public event Action? AuthenticationFailed;

        public event Action<string>? SceneChanged;
        public event Action<string[]>? SceneListUpdated;
        public event Action? Connected;
        public event Action<string>? Disconnected;

        public ObsWebSocketClient(IConfiguration config, ILogger<ObsWebSocketClient> logger)
        {
            _logger = logger;
            _url = config.GetValue("Obs:Url", "ws://localhost:4455")!;
            _password = config.GetValue<string?>("Obs:Password");

            _obs.Connected += OnConnected;
            _obs.Disconnected += OnDisconnected;
            _obs.CurrentProgramSceneChanged += OnSceneChanged;
            _obs.SceneListChanged += OnSceneListChanged;
        }

        public void SetPassword(string? password)
        {
            _password = password;
        }

        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            _intentionalDisconnect = false;
            _reconnectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            _logger.LogInformation("Connecting to OBS at {Url}...", _url);
            try
            {
                await Task.Run(() => _obs.ConnectAsync(_url, _password ?? ""), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Initial connect to OBS at {Url} failed — starting reconnect loop", _url);
                _ = ReconnectLoopAsync(_reconnectCts.Token);
            }
        }

        public Task DisconnectAsync()
        {
            _intentionalDisconnect = true;
            _reconnectCts?.Cancel();
            if (_obs.IsConnected)
            {
                _obs.Disconnect();
            }
            return Task.CompletedTask;
        }

        public Task<string[]> GetScenesAsync()
        {
            var scenes = _obs.GetSceneList();
            return Task.FromResult(scenes.Scenes.Select(s => s.Name).ToArray());
        }

        public Task<string?> GetActiveSceneAsync()
        {
            var scene = _obs.GetCurrentProgramScene();
            return Task.FromResult<string?>(scene);
        }

        private void OnConnected(object? sender, EventArgs e)
        {
            _logger.LogInformation("Connected to OBS at {Url}", _url);
            Connected?.Invoke();
        }

        private void OnDisconnected(object? sender, OBSWebsocketDotNet.Communication.ObsDisconnectionInfo info)
        {
            var reason = info.DisconnectReason ?? "Unknown";
            _logger.LogWarning("Disconnected from OBS: {Reason}", reason);
            Disconnected?.Invoke(reason);

            // OBS WebSocket closes with code 4008 / "Authentication Failed" when the password is wrong.
            if (reason.Contains("Authentication", StringComparison.OrdinalIgnoreCase)
                || reason.Contains("auth", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("OBS authentication failed — password may be incorrect or missing");
                AuthenticationFailed?.Invoke();
                return; // Don't auto-reconnect with the same bad password
            }

            if (!_intentionalDisconnect && _reconnectCts is { IsCancellationRequested: false })
            {
                _ = ReconnectLoopAsync(_reconnectCts.Token);
            }
        }

        private void OnSceneChanged(object? sender, ProgramSceneChangedEventArgs e)
        {
            _logger.LogInformation("OBS scene changed to: {Scene}", e.SceneName);
            SceneChanged?.Invoke(e.SceneName);
        }

        private void OnSceneListChanged(object? sender, SceneListChangedEventArgs e)
        {
            var names = e.Scenes
                .Select(s => s["sceneName"]?.ToString())
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
                .ToArray();
            _logger.LogInformation("OBS scene list updated: {Count} scenes", names.Length);
            SceneListUpdated?.Invoke(names);
        }

        private async Task ReconnectLoopAsync(CancellationToken ct)
        {
            var delay = TimeSpan.FromSeconds(5);
            const double maxDelaySeconds = 60;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(delay, ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                try
                {
                    _logger.LogInformation("Attempting to reconnect to OBS at {Url}...", _url);
                    await Task.Run(() => _obs.ConnectAsync(_url, _password ?? ""), ct);
                    return; // Connected event will fire on success
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "OBS reconnect attempt failed");
                    delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, maxDelaySeconds));
                }
            }
        }

        public void Dispose()
        {
            _reconnectCts?.Cancel();
            _reconnectCts?.Dispose();
            _obs.Disconnect();
        }
    }
}
