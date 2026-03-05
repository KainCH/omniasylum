using Microsoft.AspNetCore.SignalR.Client;

namespace OmniForge.SyncAgent.Services
{
    public class ServerConnectionService : IHostedService, IDisposable
    {
        private readonly StreamingSoftwareMonitor _monitor;
        private readonly IConfiguration _config;
        private readonly ILogger<ServerConnectionService> _logger;
        private HubConnection? _hub;
        private CancellationTokenSource? _cts;
        private bool _connected;

        public bool IsConnectedToServer => _connected;
        public HubConnection? Hub => _hub;

        public event Action? ServerConnected;
        public event Action<string>? ServerDisconnected;
        public event Action<int, int>? ChecklistProgressReceived;

        public ServerConnectionService(
            StreamingSoftwareMonitor monitor,
            IConfiguration config,
            ILogger<ServerConnectionService> logger)
        {
            _monitor = monitor;
            _config = config;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            var serverUrl = _config.GetValue<string>("OmniForge:ServerUrl") ?? "https://localhost:5001";
            var apiToken = _config.GetValue<string>("OmniForge:ApiToken") ?? "";

            _hub = new HubConnectionBuilder()
                .WithUrl($"{serverUrl.TrimEnd('/')}/hubs/sync-agent", options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(apiToken);
                })
                .WithAutomaticReconnect(new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) })
                .Build();

            _hub.Reconnecting += error =>
            {
                _connected = false;
                _logger.LogWarning("SignalR reconnecting: {Error}", error?.Message);
                ServerDisconnected?.Invoke("Reconnecting");
                return Task.CompletedTask;
            };

            _hub.Reconnected += async connectionId =>
            {
                _connected = true;
                _logger.LogInformation("SignalR reconnected: {ConnectionId}", connectionId);
                ServerConnected?.Invoke();
                await IdentifyAndReportScenesAsync();
            };

            _hub.Closed += error =>
            {
                _connected = false;
                _logger.LogWarning("SignalR closed: {Error}", error?.Message);
                ServerDisconnected?.Invoke(error?.Message ?? "Closed");
                if (!_cts!.IsCancellationRequested)
                {
                    _ = ReconnectLoopAsync(_cts.Token);
                }
                return Task.CompletedTask;
            };

            // Server-to-client: checklist progress for tray tooltip
            _hub.On<int, int>("ReceiveChecklistProgress", (completed, total) =>
            {
                ChecklistProgressReceived?.Invoke(completed, total);
            });

            // Wire up monitor events
            _monitor.ScenesDiscovered += async scenes =>
            {
                if (_hub?.State == HubConnectionState.Connected)
                {
                    try
                    {
                        await _hub.InvokeAsync("ReportScenes", scenes, _monitor.Client.SoftwareType);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to report scenes to server");
                    }
                }
            };

            _monitor.SceneActivated += async scene =>
            {
                if (_hub?.State == HubConnectionState.Connected)
                {
                    try
                    {
                        await _hub.InvokeAsync("ReportSceneChange", scene);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to report scene change to server");
                    }
                }
            };

            await ConnectAsync(_cts.Token);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _cts?.Cancel();
            if (_hub != null)
            {
                await _hub.DisposeAsync();
            }
        }

        private async Task ConnectAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await _hub!.StartAsync(ct);
                    _connected = true;
                    _logger.LogInformation("Connected to OmniForge server");
                    ServerConnected?.Invoke();
                    await IdentifyAndReportScenesAsync();
                    return;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Failed to connect to server, retrying in 5s...");
                    await Task.Delay(5000, ct);
                }
            }
        }

        private async Task ReconnectLoopAsync(CancellationToken ct)
        {
            await Task.Delay(5000, ct);
            if (!ct.IsCancellationRequested)
            {
                await ConnectAsync(ct);
            }
        }

        private async Task IdentifyAndReportScenesAsync()
        {
            if (_hub == null) return;
            try
            {
                await _hub.InvokeAsync("Identify", _monitor.Client.SoftwareType);

                if (_monitor.Client.IsConnected)
                {
                    var scenes = await _monitor.Client.GetScenesAsync();
                    if (scenes.Length > 0)
                    {
                        await _hub.InvokeAsync("ReportScenes", scenes, _monitor.Client.SoftwareType);
                    }

                    var active = await _monitor.Client.GetActiveSceneAsync();
                    if (!string.IsNullOrEmpty(active))
                    {
                        await _hub.InvokeAsync("ReportSceneChange", active);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to identify/report scenes after connect");
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
    }
}
