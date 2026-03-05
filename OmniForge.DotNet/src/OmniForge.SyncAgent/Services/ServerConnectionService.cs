using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;

namespace OmniForge.SyncAgent.Services
{
    public class ServerConnectionService : IHostedService, IDisposable
    {
        private readonly StreamingSoftwareMonitor _monitor;
        private readonly AgentConfigStore _configStore;
        private readonly ILogger<ServerConnectionService> _logger;
        private HubConnection? _hub;
        private CancellationTokenSource? _cts;
        private bool _connected;
        private System.Threading.Timer? _tokenRefreshTimer;

        public bool IsConnectedToServer => _connected;
        public HubConnection? Hub => _hub;

        public event Action? ServerConnected;
        public event Action<string>? ServerDisconnected;
        public event Action<int, int>? ChecklistProgressReceived;
        public event Action<bool>? StreamStatusChanged;

        public ServerConnectionService(
            StreamingSoftwareMonitor monitor,
            AgentConfigStore configStore,
            ILogger<ServerConnectionService> logger)
        {
            _monitor = monitor;
            _configStore = configStore;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // Wire up monitor events (these persist regardless of connection state)
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

            if (!_configStore.HasToken())
            {
                _logger.LogInformation("No token configured - waiting for pairing");
                return;
            }

            await ConnectWithCurrentTokenAsync(_cts.Token);
            StartTokenRefreshTimer();
        }

        public async Task UpdateTokenAndReconnectAsync(string token)
        {
            // Disconnect old hub
            if (_hub != null)
            {
                try
                {
                    await _hub.DisposeAsync();
                }
                catch { }
                _hub = null;
                _connected = false;
            }

            // Save token
            _configStore.SaveToken(token, DateTimeOffset.UtcNow.AddDays(7));

            // Reconnect
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            await ConnectWithCurrentTokenAsync(_cts.Token);
            StartTokenRefreshTimer();
        }

        public async Task DisconnectAsync()
        {
            _tokenRefreshTimer?.Dispose();
            _tokenRefreshTimer = null;

            if (_hub != null)
            {
                try
                {
                    await _hub.DisposeAsync();
                }
                catch { }
                _hub = null;
            }

            _connected = false;
            ServerDisconnected?.Invoke("Signed out");
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _cts?.Cancel();
            _tokenRefreshTimer?.Dispose();
            if (_hub != null)
            {
                await _hub.DisposeAsync();
            }
        }

        private async Task ConnectWithCurrentTokenAsync(CancellationToken ct)
        {
            var token = _configStore.Config.Token;
            if (string.IsNullOrEmpty(token)) return;

            _hub = BuildHubConnection(token);
            await ConnectAsync(ct);
        }

        private HubConnection BuildHubConnection(string token)
        {
            var serverUrl = _configStore.ServerUrl.TrimEnd('/');

            var hub = new HubConnectionBuilder()
                .WithUrl($"{serverUrl}/hubs/sync-agent", options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                })
                .WithAutomaticReconnect(new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) })
                .Build();

            hub.Reconnecting += error =>
            {
                _connected = false;
                _logger.LogWarning("SignalR reconnecting: {Error}", error?.Message);
                ServerDisconnected?.Invoke("Reconnecting");
                return Task.CompletedTask;
            };

            hub.Reconnected += async connectionId =>
            {
                _connected = true;
                _logger.LogInformation("SignalR reconnected: {ConnectionId}", connectionId);
                ServerConnected?.Invoke();
                await IdentifyAndReportScenesAsync();
            };

            hub.Closed += error =>
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

            hub.On<int, int>("ReceiveChecklistProgress", (completed, total) =>
            {
                ChecklistProgressReceived?.Invoke(completed, total);
            });

            hub.On<bool>("StreamStatusChanged", isLive =>
            {
                StreamStatusChanged?.Invoke(isLive);
            });

            return hub;
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

        private void StartTokenRefreshTimer()
        {
            _tokenRefreshTimer?.Dispose();
            _tokenRefreshTimer = new System.Threading.Timer(async _ => await RefreshTokenIfNeededAsync(), null,
                TimeSpan.FromHours(1), TimeSpan.FromHours(1));
        }

        private async Task RefreshTokenIfNeededAsync()
        {
            try
            {
                if (!_configStore.HasToken()) return;

                var expiresAt = _configStore.Config.TokenExpiresAt;
                if (expiresAt == null || expiresAt.Value - DateTimeOffset.UtcNow > TimeSpan.FromDays(2))
                    return;

                _logger.LogInformation("Token expires soon, refreshing...");

                var serverUrl = _configStore.ServerUrl.TrimEnd('/');
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _configStore.Config.Token);

                var response = await httpClient.PostAsync($"{serverUrl}/auth/refresh", null);
                if (response.IsSuccessStatusCode)
                {
                    var newToken = response.Headers.TryGetValues("X-New-Token", out var values)
                        ? values.FirstOrDefault()
                        : null;

                    if (!string.IsNullOrEmpty(newToken))
                    {
                        _configStore.SaveToken(newToken, DateTimeOffset.UtcNow.AddDays(7));
                        _logger.LogInformation("Token refreshed successfully");
                    }
                }
                else
                {
                    _logger.LogWarning("Token refresh failed: {Status}", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during token refresh");
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _tokenRefreshTimer?.Dispose();
        }
    }
}
