using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Types.Events;
using OmniForge.SceneSync.Configuration;

namespace OmniForge.SceneSync.Services
{
    /// <summary>
    /// Hosted service that connects to OBS Studio via obs-websocket v5 and listens for scene changes.
    /// Includes reconnection logic with exponential backoff.
    /// </summary>
    public class ObsSceneService : IHostedService, IDisposable
    {
        private readonly OBSWebsocket _obs;
        private readonly SceneSyncSettings _settings;
        private readonly SceneSyncOrchestrator _orchestrator;
        private readonly ILogger<ObsSceneService> _logger;

        private CancellationTokenSource? _cts;
        private Task? _connectTask;
        private string? _currentScene;
        private bool _disposed;

        private static readonly int[] BackoffSeconds = { 5, 10, 30, 60 };

        public ObsSceneService(
            IOptions<SceneSyncSettings> settings,
            SceneSyncOrchestrator orchestrator,
            ILogger<ObsSceneService> logger)
        {
            _settings = settings.Value;
            _orchestrator = orchestrator;
            _logger = logger;

            _obs = new OBSWebsocket();
            _obs.Connected += OnConnected;
            _obs.Disconnected += OnDisconnected;
            _obs.CurrentProgramSceneChanged += OnSceneChanged;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (!_settings.OBS.Enabled)
            {
                _logger.LogInformation("⏭️  OBS integration is disabled in configuration");
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

            if (_obs.IsConnected)
            {
                _obs.Disconnect();
                _logger.LogInformation("🔌 Disconnected from OBS Studio");
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
                    var url = $"ws://{_settings.OBS.Host}:{_settings.OBS.Port}";
                    _logger.LogInformation("🔄 Connecting to OBS Studio at {Url}...", url);

                    _obs.ConnectAsync(url, _settings.OBS.Password);

                    // Wait for connection or cancellation
                    var timeout = TimeSpan.FromSeconds(10);
                    var start = DateTime.UtcNow;
                    while (!_obs.IsConnected && !ct.IsCancellationRequested && (DateTime.UtcNow - start) < timeout)
                    {
                        await Task.Delay(250, ct);
                    }

                    if (_obs.IsConnected)
                    {
                        attempt = 0; // Reset backoff on success

                        // Fetch the initial scene
                        try
                        {
                            var currentScene = _obs.GetCurrentProgramScene();
                            _currentScene = currentScene;
                            _logger.LogInformation("🎬 Current OBS scene: {Scene}", _currentScene);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "⚠️ Could not fetch initial scene");
                        }

                        // Stay connected — wait for disconnection or cancellation
                        while (_obs.IsConnected && !ct.IsCancellationRequested)
                        {
                            await Task.Delay(1000, ct);
                        }
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ OBS connection attempt failed");
                }

                if (ct.IsCancellationRequested) break;

                var backoff = BackoffSeconds[Math.Min(attempt, BackoffSeconds.Length - 1)];
                _logger.LogInformation("⏳ Retrying OBS connection in {Seconds}s (attempt {Attempt})...", backoff, attempt + 1);
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

        private void OnConnected(object? sender, EventArgs e)
        {
            _logger.LogInformation("✅ Connected to OBS Studio");
        }

        private void OnDisconnected(object? sender, OBSWebsocketDotNet.Communication.ObsDisconnectionInfo e)
        {
            _logger.LogWarning("🔌 Disconnected from OBS Studio: {Reason}", e.DisconnectReason ?? "unknown");
        }

        private void OnSceneChanged(object? sender, ProgramSceneChangedEventArgs e)
        {
            var previous = _currentScene;
            _currentScene = e.SceneName;

            _logger.LogInformation("🎬 OBS scene changed: {Previous} → {Current}", previous ?? "(none)", _currentScene);

            // Fire and forget — the orchestrator handles debouncing and API calls
            _ = _orchestrator.ReportSceneChangeAsync(_currentScene, previous, "OBS");
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
