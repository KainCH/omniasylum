using OmniForge.SyncAgent.Abstractions;

namespace OmniForge.SyncAgent.Services
{
    public class StreamingSoftwareDetectorOptions
    {
        public string PreferredMode { get; set; } = "auto";
        public int ObsPort { get; set; } = 4455;
    }

    public class StreamingSoftwareDetector
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly AgentConfigStore _configStore;
        private readonly StreamingSoftwareDetectorOptions _options;
        private readonly IConfiguration _config;
        private readonly ILogger<StreamingSoftwareDetector> _logger;

        public StreamingSoftwareDetector(
            IServiceProvider serviceProvider,
            AgentConfigStore configStore,
            StreamingSoftwareDetectorOptions options,
            IConfiguration config,
            ILogger<StreamingSoftwareDetector> logger)
        {
            _serviceProvider = serviceProvider;
            _configStore = configStore;
            _options = options;
            _config = config;
            _logger = logger;
        }

        /// <summary>
        /// Polls until streaming software is detected or cancellation is requested.
        /// Returns the detected client type name and a connected client instance.
        /// </summary>
        public async Task<(string SoftwareType, IStreamingSoftwareClient Client)> DetectAsync(CancellationToken ct)
        {
            // If explicitly configured, skip detection
            if (_options.PreferredMode == "obs")
            {
                _logger.LogInformation("Streaming software explicitly set to OBS");
                return ("OBS Studio", CreateObsClient());
            }
            if (_options.PreferredMode == "streamlabs")
            {
                _logger.LogInformation("Streaming software explicitly set to Streamlabs Desktop");
                return ("Streamlabs Desktop", CreateStreamlabsClient());
            }

            // Auto-detect: poll for available software
            _logger.LogInformation("Scanning for streaming software...");
            var scanInterval = TimeSpan.FromSeconds(5);

            while (!ct.IsCancellationRequested)
            {
                // Check OBS WebSocket
                if (IsObsAvailable())
                {
                    _logger.LogInformation("Detected OBS Studio WebSocket on port {Port}", _options.ObsPort);
                    return ("OBS Studio", CreateObsClient());
                }

                // Check Streamlabs Desktop named pipe
                if (IsStreamlabsAvailable())
                {
                    _logger.LogInformation("Detected Streamlabs Desktop via named pipe");
                    return ("Streamlabs Desktop", CreateStreamlabsClient());
                }

                _logger.LogDebug("No streaming software detected, retrying in {Seconds}s...", scanInterval.TotalSeconds);
                try
                {
                    await Task.Delay(scanInterval, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            throw new OperationCanceledException("Detection cancelled before streaming software was found");
        }

        private bool IsObsAvailable()
        {
            try
            {
                using var tcp = new System.Net.Sockets.TcpClient();
                // Use a short connect timeout — the default can block for ~20 seconds
                // on Windows when nothing is listening, making detection extremely slow.
                return tcp.ConnectAsync("127.0.0.1", _options.ObsPort)
                           .Wait(TimeSpan.FromMilliseconds(500));
            }
            catch
            {
                return false;
            }
        }

        private bool IsStreamlabsAvailable()
        {
            try
            {
                return File.Exists(@"\\.\pipe\slobs");
            }
            catch
            {
                return false;
            }
        }

        private ObsWebSocketClient CreateObsClient()
        {
            var logger = _serviceProvider.GetRequiredService<ILogger<ObsWebSocketClient>>();
            var client = new ObsWebSocketClient(_config, logger);
            var savedPassword = _configStore.Config.ObsPassword;
            if (!string.IsNullOrEmpty(savedPassword))
                client.SetPassword(savedPassword);
            return client;
        }

        private StreamlabsDesktopClient CreateStreamlabsClient()
        {
            var logger = _serviceProvider.GetRequiredService<ILogger<StreamlabsDesktopClient>>();
            return new StreamlabsDesktopClient(_config, logger);
        }
    }
}
