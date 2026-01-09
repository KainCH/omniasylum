using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Utilities;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace OmniForge.Web.Controllers
{
    [Route("api/counters")]
    [ApiController]
    [Authorize(AuthenticationSchemes = "Bearer,Cookies")]
    public class CounterController : ControllerBase
    {
        private readonly ICounterRepository _counterRepository;
        private readonly IUserRepository _userRepository;
        private readonly IGameContextRepository _gameContextRepository;
        private readonly IGameCoreCountersConfigRepository _gameCoreCountersConfigRepository;
        private readonly INotificationService _notificationService;
        private readonly IOverlayNotifier _overlayNotifier;
        private readonly ILogger<CounterController> _logger;

        public CounterController(
            ICounterRepository counterRepository,
            IUserRepository userRepository,
            IGameContextRepository gameContextRepository,
            IGameCoreCountersConfigRepository gameCoreCountersConfigRepository,
            INotificationService notificationService,
            IOverlayNotifier overlayNotifier,
            ILogger<CounterController> logger)
        {
            _counterRepository = counterRepository;
            _userRepository = userRepository;
            _gameContextRepository = gameContextRepository;
            _gameCoreCountersConfigRepository = gameCoreCountersConfigRepository;
            _notificationService = notificationService;
            _overlayNotifier = overlayNotifier;
            _logger = logger;
        }

        private async Task<Core.Entities.OverlaySettings> GetEffectiveOverlaySettingsAsync(string userId, Core.Entities.User user)
        {
            var effective = user.OverlaySettings ?? new Core.Entities.OverlaySettings();

            try
            {
                var ctx = await _gameContextRepository.GetAsync(userId);
                if (ctx == null || string.IsNullOrWhiteSpace(ctx.ActiveGameId))
                {
                    return effective;
                }

                var selection = await _gameCoreCountersConfigRepository.GetAsync(userId, ctx.ActiveGameId);
                if (selection == null)
                {
                    return effective;
                }

                effective.Counters ??= new Core.Entities.OverlayCounters();
                effective.Counters.Deaths = selection.DeathsEnabled;
                effective.Counters.Swears = selection.SwearsEnabled;
                effective.Counters.Screams = selection.ScreamsEnabled;
                effective.Counters.Bits = selection.BitsEnabled;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed computing effective overlay settings for user {UserId}", LogSanitizer.Sanitize(userId));
            }

            return effective;
        }

        [HttpGet]
        public async Task<IActionResult> GetCounters()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var counters = await _counterRepository.GetCountersAsync(userId);
            return Ok(counters);
        }

        [HttpPost("{type}/increment")]
        public async Task<IActionResult> Increment(string type)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            try
            {
                var counters = await _counterRepository.IncrementCounterAsync(userId, type);

                // Notify via SignalR
                await _overlayNotifier.NotifyCounterUpdateAsync(userId, counters);

                // Check milestones
                var user = await _userRepository.GetUserAsync(userId);
                if (user != null)
                {
                    int newValue = GetValueByType(counters, type);
                    int previousValue = newValue - 1;

                    await _notificationService.CheckAndSendMilestoneNotificationsAsync(user, type, previousValue, newValue);
                }

                return Ok(counters);
            }
            catch (ArgumentException)
            {
                return BadRequest("Invalid counter type");
            }
        }

        [HttpPost("{type}/decrement")]
        public async Task<IActionResult> Decrement(string type)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            try
            {
                var counters = await _counterRepository.DecrementCounterAsync(userId, type);

                // Notify via SignalR
                await _overlayNotifier.NotifyCounterUpdateAsync(userId, counters);

                return Ok(counters);
            }
            catch (ArgumentException)
            {
                return BadRequest("Invalid counter type");
            }
        }

        [HttpPost("reset")]
        public async Task<IActionResult> ResetCounters()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var counters = await _counterRepository.GetCountersAsync(userId);
            if (counters == null) return NotFound("Counters not found");

            counters.Deaths = 0;
            counters.Swears = 0;
            counters.Screams = 0;
            // Bits are preserved

            counters.LastUpdated = DateTimeOffset.UtcNow;
            await _counterRepository.SaveCountersAsync(counters);

            await _overlayNotifier.NotifyCounterUpdateAsync(userId, counters);

            return Ok(counters);
        }

        [HttpGet("export")]
        public async Task<IActionResult> ExportData()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var counters = await _counterRepository.GetCountersAsync(userId);
            if (counters == null) return NotFound("Counters not found");

            var user = await _userRepository.GetUserAsync(userId);

            return Ok(new
            {
                deaths = counters.Deaths,
                swears = counters.Swears,
                screams = counters.Screams,
                bits = counters.Bits,
                customCounters = counters.CustomCounters,
                lastUpdated = counters.LastUpdated,
                username = user?.Username,
                exportedAt = DateTimeOffset.UtcNow
            });
        }

        [HttpPost("overlay/bits-progress")]
        public async Task<IActionResult> UpdateBitsProgress([FromBody] BitsProgressRequest request)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            if (request.Amount <= 0) return BadRequest("Valid amount is required");

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            if (user.OverlaySettings?.BitsGoal == null) return BadRequest("Bits goal not configured");

            var currentSettings = user.OverlaySettings;
            var newCurrent = Math.Min(currentSettings.BitsGoal.Current + request.Amount, currentSettings.BitsGoal.Target);

            currentSettings.BitsGoal.Current = newCurrent;
            user.OverlaySettings = currentSettings;

            await _userRepository.SaveUserAsync(user);

            // Notify overlay
            await _overlayNotifier.NotifySettingsUpdateAsync(userId, currentSettings);

            bool goalReached = newCurrent >= currentSettings.BitsGoal.Target;

            return Ok(new
            {
                message = "Bits goal progress updated",
                bitsGoal = currentSettings.BitsGoal,
                goalReached = goalReached,
                progress = (int)Math.Round((double)newCurrent / currentSettings.BitsGoal.Target * 100)
            });
        }

        [HttpGet("overlay/settings")]
        public async Task<IActionResult> GetOverlaySettings()
        {
            var userId = User.FindFirst("userId")?.Value;
            _logger.LogInformation("⚙️ GetOverlaySettings called for userId: {UserId}", LogSanitizer.Sanitize(userId));

            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            if (!user.Features.StreamOverlay)
            {
                _logger.LogWarning("⚠️ Stream overlay feature not enabled for user {UserId}", LogSanitizer.Sanitize(userId));
                return StatusCode(403, new { error = "Stream overlay feature is not enabled for your account" });
            }

            var effective = await GetEffectiveOverlaySettingsAsync(userId, user);

            _logger.LogDebug("📋 Returning overlay settings (effective): Position={Position}, Scale={Scale}",
                LogSanitizer.Sanitize(effective?.Position), effective?.Scale);

            return Ok(effective);
        }

        [HttpPut("overlay/settings")]
        public async Task<IActionResult> UpdateOverlaySettings([FromBody] Core.Entities.OverlaySettings request)
        {
            var userId = User.FindFirst("userId")?.Value;
            _logger.LogInformation("💾 UpdateOverlaySettings called for userId: {UserId}", LogSanitizer.Sanitize(userId));
            _logger.LogDebug("📥 Received settings: Position={Position}, Scale={Scale}, Enabled={Enabled}",
                LogSanitizer.Sanitize(request?.Position), request?.Scale, request?.Enabled);

            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            if (request == null)
            {
                _logger.LogWarning("⚠️ Request body is null");
                return BadRequest(new { error = "Request body is required" });
            }

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            if (!user.Features.StreamOverlay)
            {
                _logger.LogWarning("⚠️ Stream overlay feature not enabled for user {UserId}", LogSanitizer.Sanitize(userId));
                return StatusCode(403, new { error = "Stream overlay feature is not enabled for your account" });
            }

            // Validate position
            var validPositions = new[] { "top-left", "top-right", "bottom-left", "bottom-right" };
            if (!string.IsNullOrEmpty(request.Position) && !validPositions.Contains(request.Position))
            {
                _logger.LogWarning("⚠️ Invalid position: {Position}", LogSanitizer.Sanitize(request.Position));
                return BadRequest(new { error = "Invalid position. Must be one of: " + string.Join(", ", validPositions) });
            }

            user.OverlaySettings = request;
            await _userRepository.SaveUserAsync(user);

            // Persist core counter visibility to the active game's per-game selection, if applicable.
            try
            {
                var ctx = await _gameContextRepository.GetAsync(userId);
                if (ctx != null && !string.IsNullOrWhiteSpace(ctx.ActiveGameId) && request.Counters != null)
                {
                    var now = DateTimeOffset.UtcNow;
                    await _gameCoreCountersConfigRepository.SaveAsync(
                        userId,
                        ctx.ActiveGameId,
                        new Core.Entities.GameCoreCountersConfig(
                            UserId: userId,
                            GameId: ctx.ActiveGameId,
                            DeathsEnabled: request.Counters.Deaths,
                            SwearsEnabled: request.Counters.Swears,
                            ScreamsEnabled: request.Counters.Screams,
                            BitsEnabled: request.Counters.Bits,
                            UpdatedAt: now));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed persisting active game core counter selection for user {UserId}", LogSanitizer.Sanitize(userId));
            }

            _logger.LogInformation("✅ Overlay settings updated successfully for user {UserId}", LogSanitizer.Sanitize(userId));

            return Ok(new
            {
                message = "Overlay settings updated successfully",
                settings = user.OverlaySettings
            });
        }

        [HttpGet("{userId}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetPublicCounters(string userId)
        {
            _logger.LogInformation("📊 GetPublicCounters called for userId: {UserId}", LogSanitizer.Sanitize(userId));

            var counters = await _counterRepository.GetCountersAsync(userId);
            if (counters == null)
            {
                _logger.LogWarning("⚠️ Counters not found for userId: {UserId}", LogSanitizer.Sanitize(userId));
                return NotFound();
            }

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null)
            {
                _logger.LogWarning("⚠️ User not found for userId: {UserId}", LogSanitizer.Sanitize(userId));
                return NotFound();
            }

            var effectiveSettings = await GetEffectiveOverlaySettingsAsync(userId, user);

            _logger.LogDebug("📋 User OverlaySettings (effective) for {UserId}: Position={Position}, Scale={Scale}, Enabled={Enabled}",
                LogSanitizer.Sanitize(userId), LogSanitizer.Sanitize(effectiveSettings?.Position), effectiveSettings?.Scale, effectiveSettings?.Enabled);
            _logger.LogDebug("📋 OverlaySettings.Counters (effective): Deaths={Deaths}, Swears={Swears}, Screams={Screams}, Bits={Bits}",
                effectiveSettings?.Counters?.Deaths, effectiveSettings?.Counters?.Swears,
                effectiveSettings?.Counters?.Screams, effectiveSettings?.Counters?.Bits);

            var previewRequested = HttpContext.Request.Query.TryGetValue("preview", out var previewValues)
                && previewValues.ToString().Equals("true", StringComparison.OrdinalIgnoreCase);
            var offlinePreviewEnabled = effectiveSettings?.OfflinePreview ?? false;

            var streamStartedValue = counters.StreamStarted;
            if (streamStartedValue == null && (previewRequested || offlinePreviewEnabled))
            {
                streamStartedValue = DateTimeOffset.UtcNow;
            }

            var response = new
            {
                deaths = counters.Deaths,
                swears = counters.Swears,
                screams = counters.Screams,
                bits = counters.Bits,
                lastUpdated = counters.LastUpdated,
                streamStarted = streamStartedValue,
                settings = effectiveSettings
            };

            _logger.LogInformation("✅ Returning public counters for {UserId}: Deaths={Deaths}, Swears={Swears}, StreamStarted={StreamStarted}",
                LogSanitizer.Sanitize(userId), counters.Deaths, counters.Swears, counters.StreamStarted?.ToString("o") ?? "null");

            return Ok(response);
        }

        private int GetValueByType(Core.Entities.Counter counter, string type)
        {
            return type.ToLower() switch
            {
                "deaths" => counter.Deaths,
                "swears" => counter.Swears,
                "screams" => counter.Screams,
                "bits" => counter.Bits,
                _ => 0
            };
        }
    }

    public class BitsProgressRequest
    {
        public int Amount { get; set; }
    }
}
