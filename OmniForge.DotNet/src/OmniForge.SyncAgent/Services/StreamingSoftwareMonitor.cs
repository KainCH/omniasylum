using OmniForge.SyncAgent.Abstractions;

namespace OmniForge.SyncAgent.Services
{
    public class StreamingSoftwareMonitor : IHostedService
    {
        private readonly StreamingSoftwareDetector _detector;
        private readonly ILogger<StreamingSoftwareMonitor> _logger;
        private IStreamingSoftwareClient? _client;

        public event Action<string[]>? ScenesDiscovered;
        public event Action<string>? SceneActivated;
        public event Action? SoftwareConnected;
        public event Action<string>? SoftwareDisconnected;

        /// <summary>Raised once when streaming software is first detected. Arg is the friendly name (e.g. "OBS Studio").</summary>
        public event Action<string>? SoftwareDetected;

        /// <summary>The active client, or null if no software has been detected yet.</summary>
        public IStreamingSoftwareClient? Client => _client;

        /// <summary>Friendly name of the detected software (e.g. "OBS Studio"), or null.</summary>
        public string? DetectedSoftwareName { get; private set; }

        public StreamingSoftwareMonitor(StreamingSoftwareDetector detector, ILogger<StreamingSoftwareMonitor> logger)
        {
            _detector = detector;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Waiting for streaming software to be detected...");

            try
            {
                var (softwareName, client) = await _detector.DetectAsync(cancellationToken);
                DetectedSoftwareName = softwareName;
                _client = client;

                _client.Connected += OnSoftwareConnected;
                _client.Disconnected += OnSoftwareDisconnected;
                _client.SceneChanged += scene => SceneActivated?.Invoke(scene);
                _client.SceneListUpdated += scenes => ScenesDiscovered?.Invoke(scenes);

                _logger.LogInformation("Streaming software detected: {Software}", softwareName);
                SoftwareDetected?.Invoke(softwareName);

                await _client.ConnectAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Streaming software detection cancelled");
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping streaming software monitor...");
            if (_client != null)
                await _client.DisconnectAsync();
        }

        private async void OnSoftwareConnected()
        {
            _logger.LogInformation("Streaming software connected ({Software})", _client!.SoftwareType);
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
