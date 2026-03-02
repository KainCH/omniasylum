using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniForge.SceneSync.Configuration;

namespace OmniForge.SceneSync.Services
{
    /// <summary>
    /// HTTP client wrapper for communicating with the OmniForge API.
    /// </summary>
    public class OmniForgeApiClient
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<OmniForgeApiClient> _logger;

        public OmniForgeApiClient(
            HttpClient httpClient,
            IOptions<SceneSyncSettings> settings,
            ILogger<OmniForgeApiClient> logger)
        {
            _httpClient = httpClient;
            _logger = logger;

            var config = settings.Value;
            _httpClient.BaseAddress = new Uri(config.Server.BaseUrl.TrimEnd('/') + "/");

            if (!string.IsNullOrWhiteSpace(config.Server.AuthToken))
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", config.Server.AuthToken);
            }
        }

        /// <summary>
        /// Send a scene change event to the OmniForge server.
        /// </summary>
        public async Task<bool> SendSceneChangeAsync(string sceneName, string? previousScene, string source, CancellationToken ct = default)
        {
            try
            {
                var payload = new
                {
                    sceneName,
                    previousScene,
                    source
                };

                var response = await _httpClient.PostAsJsonAsync("api/stream/scene", payload, ct);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("✅ Scene change sent: {Scene} (from {Source})", sceneName, source);
                    return true;
                }

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    _logger.LogError("❌ Authentication failed (401). Your Twitch JWT token may be expired or invalid. " +
                        "Log in again at stream-tool.cerillia.net and copy the new 'token' cookie to AuthToken in appsettings.json.");
                    return false;
                }

                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("⚠️ Server returned {StatusCode}: {Body}", (int)response.StatusCode, body);
                return false;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "❌ Failed to reach the OmniForge server at {BaseUrl}. Is it running?",
                    _httpClient.BaseAddress);
                return false;
            }
            catch (TaskCanceledException) when (ct.IsCancellationRequested)
            {
                // Shutdown requested
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error sending scene change");
                return false;
            }
        }
    }
}
