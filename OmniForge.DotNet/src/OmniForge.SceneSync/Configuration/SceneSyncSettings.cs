namespace OmniForge.SceneSync.Configuration
{
    /// <summary>
    /// Root configuration for the SceneSync application.
    /// </summary>
    public class SceneSyncSettings
    {
        public ServerSettings Server { get; set; } = new();
        public ObsSettings OBS { get; set; } = new();
        public StreamlabsSettings Streamlabs { get; set; } = new();

        /// <summary>
        /// Debounce interval in milliseconds to prevent rapid duplicate scene change reports.
        /// </summary>
        public int DebounceMs { get; set; } = 500;
    }

    /// <summary>
    /// OmniForge server connection settings.
    /// </summary>
    public class ServerSettings
    {
        /// <summary>
        /// Base URL of the OmniForge API (e.g. "https://stream-tool.cerillia.net" or "http://localhost:5000").
        /// </summary>
        public string BaseUrl { get; set; } = "http://localhost:5000";

        /// <summary>
        /// JWT Bearer token for authentication. This is the same Twitch JWT token used by the OmniForge web app.
        /// Copy it from your browser cookies (token name: "token") after logging in at stream-tool.cerillia.net.
        /// </summary>
        public string AuthToken { get; set; } = string.Empty;
    }

    /// <summary>
    /// OBS Studio WebSocket connection settings.
    /// </summary>
    public class ObsSettings
    {
        /// <summary>
        /// Whether to connect to OBS Studio.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// OBS WebSocket server host (default: localhost).
        /// </summary>
        public string Host { get; set; } = "localhost";

        /// <summary>
        /// OBS WebSocket server port (default: 4455 for obs-websocket v5).
        /// </summary>
        public int Port { get; set; } = 4455;

        /// <summary>
        /// OBS WebSocket server password (leave empty if authentication is disabled in OBS).
        /// </summary>
        public string Password { get; set; } = string.Empty;
    }

    /// <summary>
    /// Streamlabs Desktop connection settings.
    /// </summary>
    public class StreamlabsSettings
    {
        /// <summary>
        /// Whether to connect to Streamlabs Desktop.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Streamlabs named pipe name (default: slobs).
        /// The full pipe path on Windows is \\.\pipe\slobs.
        /// </summary>
        public string PipeName { get; set; } = "slobs";

        /// <summary>
        /// Connection timeout in seconds.
        /// </summary>
        public int TimeoutSeconds { get; set; } = 10;
    }
}
