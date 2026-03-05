using OmniForge.SyncAgent.Abstractions;

namespace OmniForge.SyncAgent.Services
{
    public class StreamingSoftwareMonitor : IHostedService
    {
        private readonly IStreamingSoftwareClient _client;
        private readonly ILogger<StreamingSoftwareMonitor> _logger;

        public event Action<string[]>? ScenesDiscovered;
        public event Action<string>? SceneActivated;
        public event Action? SoftwareConnected;
        public event Action<string>? SoftwareDisconnected;

        public IStreamingSoftwareClient Client => _client;

        public StreamingSoftwareMonitor(IStreamingSoftwareClient client, ILogger<StreamingSoftwareMonitor> logger)
        {
            _client = client;
            _logger = logger;

            _client.Connected += OnSoftwareConnected;
            _client.Disconnected += OnSoftwareDisconnected;
            _client.SceneChanged += scene => SceneActivated?.Invoke(scene);
            _client.SceneListUpdated += scenes => ScenesDiscovered?.Invoke(scenes);
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting streaming software monitor ({Software})...", _client.SoftwareType);
            await _client.ConnectAsync(cancellationToken);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping streaming software monitor...");
            await _client.DisconnectAsync();
        }

        private async void OnSoftwareConnected()
        {
            _logger.LogInformation("Streaming software connected ({Software})", _client.SoftwareType);
            SoftwareConnected?.Invoke();

            try
            {
                var scenes = await _client.GetScenesAsync();
                if (scenes.Length > 0)
                {
                    ScenesDiscovered?.Invoke(scenes);
                }

                var activeScene = await _client.GetActiveSceneAsync();
                if (!string.IsNullOrEmpty(activeScene))
                {
                    SceneActivated?.Invoke(activeScene);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get initial scene data after connect");
            }
        }

        private void OnSoftwareDisconnected(string reason)
        {
            _logger.LogWarning("Streaming software disconnected: {Reason}", reason);
            SoftwareDisconnected?.Invoke(reason);
        }
    }
}
