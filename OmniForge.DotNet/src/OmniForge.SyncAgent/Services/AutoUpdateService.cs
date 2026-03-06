using System.Net.Http.Json;
using System.Reflection;

namespace OmniForge.SyncAgent.Services
{
    public class AutoUpdateService : IHostedService, IDisposable
    {
        private readonly AgentConfigStore _configStore;
        private readonly ServerConnectionService _serverConnection;
        private readonly ILogger<AutoUpdateService> _logger;
        private System.Threading.Timer? _checkTimer;
        private string? _pendingUpdatePath;
        private bool _isLive;
        private CancellationTokenSource? _cts;

        /// <summary>Fires when a newer version is found. Args: (currentVersion, remoteVersion).</summary>
        public event Action<string, string>? UpdateAvailable;

        /// <summary>Fires when there is no update (manual check only).</summary>
        public event Action<string>? AlreadyUpToDate;

        /// <summary>Fires just before the agent relaunches to apply an update.</summary>
        public event Action? UpdateApplying;

        public AutoUpdateService(
            AgentConfigStore configStore,
            ServerConnectionService serverConnection,
            ILogger<AutoUpdateService> logger)
        {
            _configStore = configStore;
            _serverConnection = serverConnection;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            _serverConnection.StreamStatusChanged += isLive =>
            {
                _isLive = isLive;
                if (!isLive && _pendingUpdatePath != null)
                {
                    _ = ApplyUpdateAsync();
                }
            };

            // Check for updates at startup + every 4 hours
            _checkTimer = new System.Threading.Timer(async _ => await CheckForUpdateAsync(),
                null, TimeSpan.FromMinutes(1), TimeSpan.FromHours(4));

            return Task.CompletedTask;
        }

        /// <summary>
        /// Manually trigger an update check. Safe to call from any thread.
        /// </summary>
        public Task CheckNowAsync() => CheckForUpdateAsync(notifyIfCurrent: true);

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _cts?.Cancel();
            _checkTimer?.Dispose();
            return Task.CompletedTask;
        }

        private async Task CheckForUpdateAsync(bool notifyIfCurrent = false)
        {
            if (!_configStore.HasToken()) return;

            try
            {
                var serverUrl = _configStore.ServerUrl.TrimEnd('/');
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _configStore.Config.Token);

                var response = await httpClient.GetAsync($"{serverUrl}/api/sync/agent/version");
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("Version check returned {Status}", response.StatusCode);
                    return;
                }

                var result = await response.Content.ReadFromJsonAsync<VersionResult>();
                if (result == null) return;

                _isLive = result.IsLive;

                var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
                if (!Version.TryParse(result.Version, out var remoteVersion)) return;

                if (remoteVersion <= currentVersion)
                {
                    _logger.LogDebug("Agent is up to date ({Current})", currentVersion);
                    if (notifyIfCurrent)
                        AlreadyUpToDate?.Invoke(currentVersion.ToString());
                    return;
                }

                _logger.LogInformation("Update available: {Current} -> {Remote}", currentVersion, remoteVersion);
                UpdateAvailable?.Invoke(currentVersion.ToString(), remoteVersion.ToString());
                await DownloadUpdateAsync(serverUrl + result.DownloadUrl);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error checking for updates");
            }
        }

        private async Task DownloadUpdateAsync(string url)
        {
            try
            {
                var updateDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "omni-forge", "update");
                Directory.CreateDirectory(updateDir);

                // Must use a proper .exe extension - Windows shell won't execute .exe.new
                var updatePath = Path.Combine(updateDir, "OmniForge.SyncAgent.exe");

                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _configStore.Config.Token);

                using var downloadStream = await httpClient.GetStreamAsync(url);
                using var fileStream = File.Create(updatePath);
                await downloadStream.CopyToAsync(fileStream);

                _pendingUpdatePath = updatePath;
                _logger.LogInformation("Update downloaded to {Path}", updatePath);

                if (!_isLive)
                {
                    await ApplyUpdateAsync();
                }
                else
                {
                    _logger.LogInformation("Stream is live, deferring update until offline");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error downloading update");
            }
        }

        private Task ApplyUpdateAsync()
        {
            if (_pendingUpdatePath == null || !File.Exists(_pendingUpdatePath)) return Task.CompletedTask;

            try
            {
                var currentExePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(currentExePath))
                {
                    _logger.LogWarning("Cannot determine current exe path for update");
                    return Task.CompletedTask;
                }

                _logger.LogInformation("Applying update...");
                UpdateApplying?.Invoke();

                // Launch the new exe with --update-from pointing to our current location
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_pendingUpdatePath)
                {
                    Arguments = $"--update-from \"{currentExePath}\"",
                    UseShellExecute = true
                });

                // Exit current process
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply update");
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _checkTimer?.Dispose();
        }

        private record VersionResult(string Version, bool IsLive, string DownloadUrl);
    }
}
