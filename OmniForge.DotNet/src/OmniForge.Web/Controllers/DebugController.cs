using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;

namespace OmniForge.Web.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/debug")]
    public class DebugController : ControllerBase
    {
        private readonly IUserRepository _userRepository;
        private readonly IDiscordService _discordService;
        private readonly ISeriesRepository _seriesRepository;
        private readonly ILogger<DebugController> _logger;

        public DebugController(
            IUserRepository userRepository,
            IDiscordService discordService,
            ISeriesRepository seriesRepository,
            ILogger<DebugController> logger)
        {
            _userRepository = userRepository;
            _discordService = discordService;
            _seriesRepository = seriesRepository;
            _logger = logger;
        }

        [HttpPost("restore-series-save")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> RestoreSeriesSave([FromBody] RestoreSeriesRequest request)
        {
            _logger.LogInformation("🔄 DEBUG: Restoring series save for user {TargetUserId}", request.TwitchUserId);

            // Validate request
            if (string.IsNullOrEmpty(request.TwitchUserId) || string.IsNullOrEmpty(request.SeriesName))
            {
                return BadRequest("TwitchUserId and SeriesName are required");
            }

            // Generate Series ID (RowKey) - Format: <timestamp>_<sanitized_series_name>
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var sanitizedName = System.Text.RegularExpressions.Regex.Replace(request.SeriesName, "[^a-zA-Z0-9]", "_");
            var seriesId = $"{timestamp}_{sanitizedName}";

            var series = new Series
            {
                UserId = request.TwitchUserId,
                Id = seriesId,
                Name = request.SeriesName,
                Description = request.Description ?? "Restored via Admin Debug",
                Snapshot = new Counter
                {
                    TwitchUserId = request.TwitchUserId,
                    Deaths = request.Counters.Deaths,
                    Swears = request.Counters.Swears,
                    Screams = request.Counters.Screams,
                    Bits = request.Counters.Bits,
                    CustomCounters = request.Counters.CustomCounters ?? new System.Collections.Generic.Dictionary<string, int>(),
                    LastUpdated = DateTimeOffset.UtcNow
                },
                CreatedAt = DateTimeOffset.UtcNow,
                LastUpdated = DateTimeOffset.UtcNow,
                IsActive = true
            };

            await _seriesRepository.CreateSeriesAsync(series);

            return Ok(new
            {
                success = true,
                message = "Series save restored successfully",
                seriesId,
                series
            });
        }

        [HttpPost("test-webhook-save")]
        public async Task<IActionResult> TestWebhookSave()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var testWebhookUrl = "https://discord.com/api/webhooks/1234567890/test-webhook-token-12345";
            _logger.LogInformation("🧪 DEBUG: Testing webhook save for user {UserId}", userId);

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            var originalUrl = user.DiscordWebhookUrl;

            // Update
            user.DiscordWebhookUrl = testWebhookUrl;
            await _userRepository.SaveUserAsync(user);

            // Verify
            var updatedUser = await _userRepository.GetUserAsync(userId);
            var success = updatedUser?.DiscordWebhookUrl == testWebhookUrl;

            // Restore (optional, but good for testing) - actually the legacy code leaves it?
            // Legacy code: "Test the save operation" -> updates it.
            // It doesn't seem to restore it. But it's a test endpoint.

            return Ok(new
            {
                success = true,
                message = "Webhook save test completed",
                results = new
                {
                    originalUrl,
                    savedUrl = updatedUser?.DiscordWebhookUrl,
                    success
                }
            });
        }

        [HttpGet("test-webhook-read")]
        public async Task<IActionResult> TestWebhookRead()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            _logger.LogInformation("🧪 DEBUG: Testing webhook read for user {UserId}", userId);

            var user = await _userRepository.GetUserAsync(userId);

            return Ok(new
            {
                success = true,
                webhookData = new
                {
                    webhookUrl = user?.DiscordWebhookUrl
                }
            });
        }

        [HttpPost("test-stream-notification")]
        public async Task<IActionResult> TestStreamNotification()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            if (string.IsNullOrEmpty(user.DiscordWebhookUrl))
            {
                return BadRequest(new
                {
                    success = false,
                    error = "No Discord webhook configured",
                    recommendation = "Configure Discord webhook first"
                });
            }

            _logger.LogInformation("🚀 Triggering test stream notification for {Username}", user.Username);

            var mockEvent = new
            {
                id = $"test_stream_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                broadcaster_user_id = userId,
                broadcaster_user_name = user.Username,
                started_at = DateTimeOffset.UtcNow.ToString("o")
            };

            await _discordService.SendNotificationAsync(user, "stream-online", mockEvent);

            return Ok(new
            {
                success = true,
                message = $"Test stream notification triggered for {user.Username}",
                mockEvent,
                webhookConfigured = true
            });
        }

        [HttpPost("cleanup-user-data")]
        public async Task<IActionResult> CleanupUserData()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            _logger.LogInformation("🧹 DEBUG: Cleaning up user data for {UserId}", userId);

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            var cleanedFields = new System.Collections.Generic.List<string>();

            // Check for test webhook data
            if (!string.IsNullOrEmpty(user.DiscordWebhookUrl) && user.DiscordWebhookUrl.Contains("test-webhook-token"))
            {
                user.DiscordWebhookUrl = "";
                cleanedFields.Add("DiscordWebhookUrl (test data removed)");
            }

            if (cleanedFields.Count > 0)
            {
                await _userRepository.SaveUserAsync(user);
            }

            return Ok(new
            {
                success = true,
                message = "Cleanup completed",
                cleanedFields,
                user = new { user.TwitchUserId, user.DiscordWebhookUrl }
            });
        }
    }

    public class RestoreSeriesRequest
    {
        public string TwitchUserId { get; set; } = string.Empty;
        public string SeriesName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public CounterValues Counters { get; set; } = new CounterValues();

        public class CounterValues
        {
            public int Deaths { get; set; }
            public int Swears { get; set; }
            public int Screams { get; set; }
            public int Bits { get; set; }
            public System.Collections.Generic.Dictionary<string, int> CustomCounters { get; set; } = new System.Collections.Generic.Dictionary<string, int>();
        }
    }
}
