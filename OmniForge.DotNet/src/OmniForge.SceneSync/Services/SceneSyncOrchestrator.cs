using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniForge.SceneSync.Configuration;

namespace OmniForge.SceneSync.Services
{
    /// <summary>
    /// Coordinates scene change reports from both OBS and Streamlabs services.
    /// Provides debouncing to prevent duplicate rapid-fire events and delegates to the API client.
    /// </summary>
    public class SceneSyncOrchestrator
    {
        private readonly OmniForgeApiClient _apiClient;
        private readonly ILogger<SceneSyncOrchestrator> _logger;
        private readonly int _debounceMs;

        private string? _lastReportedScene;
        private DateTimeOffset _lastReportedAt = DateTimeOffset.MinValue;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public SceneSyncOrchestrator(
            OmniForgeApiClient apiClient,
            IOptions<SceneSyncSettings> settings,
            ILogger<SceneSyncOrchestrator> logger)
        {
            _apiClient = apiClient;
            _logger = logger;
            _debounceMs = settings.Value.DebounceMs;
        }

        /// <summary>
        /// Report a scene change. Debounces duplicate events within the configured window.
        /// </summary>
        public async Task ReportSceneChangeAsync(string sceneName, string? previousScene, string source)
        {
            await _lock.WaitAsync();
            try
            {
                var now = DateTimeOffset.UtcNow;
                var sinceLastReport = (now - _lastReportedAt).TotalMilliseconds;

                // Debounce: skip if the same scene was reported within the debounce window
                if (string.Equals(_lastReportedScene, sceneName, StringComparison.Ordinal) &&
                    sinceLastReport < _debounceMs)
                {
                    _logger.LogDebug("⏭️  Skipping duplicate scene report for '{Scene}' (debounce: {Ms}ms since last)",
                        sceneName, sinceLastReport);
                    return;
                }

                _lastReportedScene = sceneName;
                _lastReportedAt = now;
            }
            finally
            {
                _lock.Release();
            }

            await _apiClient.SendSceneChangeAsync(sceneName, previousScene, source);
        }
    }
}
