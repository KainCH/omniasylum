using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Utilities;

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
        private readonly IOverlayNotifier _overlayNotifier;
        private readonly ILogger<DebugController> _logger;

        public DebugController(
            IUserRepository userRepository,
            IDiscordService discordService,
            ISeriesRepository seriesRepository,
            IOverlayNotifier overlayNotifier,
            ILogger<DebugController> logger)
        {
            _userRepository = userRepository;
            _discordService = discordService;
            _seriesRepository = seriesRepository;
            _overlayNotifier = overlayNotifier;
            _logger = logger;
        }

        [HttpPost("interaction-banner")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> SendInteractionBanner([FromBody] InteractionBannerRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.TwitchUserId))
            {
                return BadRequest(new { success = false, error = "TwitchUserId is required" });
            }
            string targetUserId = request.TwitchUserId;

            if (string.IsNullOrWhiteSpace(request.TextPrompt))
            {
                return BadRequest(new { success = false, error = "TextPrompt is required" });
            }

            var duration = request.DurationMs.HasValue ? Math.Clamp(request.DurationMs.Value, 500, 30000) : 5000;

            _logger.LogInformation("🧪 DEBUG: Sending interaction banner to {TargetUserId}", LogValue.Safe(targetUserId));

            await _overlayNotifier.NotifyCustomAlertAsync(targetUserId!, "interactionBanner", new
            {
                textPrompt = request.TextPrompt,
                duration
            });

            return Ok(new { success = true, userId = request.TwitchUserId, duration });
        }

        [HttpPost("restore-series-save")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> RestoreSeriesSave([FromBody] RestoreSeriesRequest request)
        {
            if (string.IsNullOrEmpty(request.TwitchUserId) || string.IsNullOrEmpty(request.SeriesName))
            {
                return BadRequest("TwitchUserId and SeriesName are required");
            }

            if (request.Counters == null)
            {
                return BadRequest("Counters are required");
            }

            var targetUserId = request.TwitchUserId;
            _logger.LogInformation("🔄 DEBUG: Restoring series save for user {TargetUserId}", LogValue.Safe(targetUserId));

            // Validate request

            // Generate Series ID (RowKey) - Format: <timestamp>_<sanitized_series_name>
            // ⚠️ CRITICAL: This logic is duplicated in restore-series-save.js.
            // If you change the series ID format here, you MUST update it in both places to keep them in sync!
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var sanitizedName = System.Text.RegularExpressions.Regex.Replace(request.SeriesName, "[^a-zA-Z0-9]", "_");
            var seriesId = $"{timestamp}_{sanitizedName}";

            var series = new Series
            {
                UserId = targetUserId,
                Id = seriesId,
                Name = request.SeriesName ?? string.Empty,
                Description = request.Description ?? "Restored via Admin Debug",
                Snapshot = new Counter
                {
                    TwitchUserId = targetUserId,
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

            try
            {
                await _seriesRepository.CreateSeriesAsync(series);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restore series save for user {TargetUserId}", LogValue.Safe(targetUserId));
                return StatusCode(500, new { success = false, error = "Failed to create series save" });
            }

            return Ok(new
            {
                success = true,
                message = "Series save restored successfully",
                save = new
                {
                    seriesId,
                    seriesName = series.Name,
                    description = series.Description,
                    deaths = series.Snapshot.Deaths,
                    swears = series.Snapshot.Swears,
                    screams = series.Snapshot.Screams,
                    bits = series.Snapshot.Bits,
                    savedAt = series.LastUpdated
                }
            });
        }

        [HttpPost("test-stream-notification")]
        public async Task<IActionResult> TestStreamNotification()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var safeUserId = userId!;

            var user = await _userRepository.GetUserAsync(safeUserId!);
            if (user == null) return NotFound("User not found");

            if (string.IsNullOrEmpty(user.DiscordChannelId))
            {
                return BadRequest(new
                {
                    success = false,
                    error = "No Discord channel configured",
                    recommendation = "Configure Discord channel ID first"
                });
            }

            _logger.LogInformation("🚀 Triggering test stream notification for {Username}", LogValue.Safe(user.Username));

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
                channelConfigured = true
            });
        }

        [HttpPost("cleanup-user-data")]
        public async Task<IActionResult> CleanupUserData()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var safeUserId = userId!;

            _logger.LogInformation("🧹 DEBUG: Cleaning up user data for {UserId}", LogValue.Safe(safeUserId));

            var user = await _userRepository.GetUserAsync(safeUserId!);
            if (user == null) return NotFound("User not found");

            var cleanedFields = new System.Collections.Generic.List<string>();

            if (cleanedFields.Count > 0)
            {
                await _userRepository.SaveUserAsync(user);
            }

            return Ok(new
            {
                success = true,
                message = "Cleanup completed",
                cleanedFields,
                user = new { user.TwitchUserId }
            });
        }
    }

    public class InteractionBannerRequest
    {
        public string TwitchUserId { get; set; } = string.Empty;
        public string TextPrompt { get; set; } = string.Empty;
        public int? DurationMs { get; set; }
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
