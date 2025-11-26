using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Interfaces;
using System;
using System.Text.Json;
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
        private readonly INotificationService _notificationService;
        private readonly IOverlayNotifier _overlayNotifier;
        private readonly ILogger<CounterController> _logger;

        public CounterController(
            ICounterRepository counterRepository,
            IUserRepository userRepository,
            INotificationService notificationService,
            IOverlayNotifier overlayNotifier,
            ILogger<CounterController> logger)
        {
            _counterRepository = counterRepository;
            _userRepository = userRepository;
            _notificationService = notificationService;
            _overlayNotifier = overlayNotifier;
            _logger = logger;
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
            _logger.LogInformation("⚙️ GetOverlaySettings called for userId: {UserId}", userId);

            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            if (!user.Features.StreamOverlay)
            {
                _logger.LogWarning("⚠️ Stream overlay feature not enabled for user {UserId}", userId);
                return StatusCode(403, new { error = "Stream overlay feature is not enabled for your account" });
            }

            _logger.LogDebug("📋 Returning overlay settings: Position={Position}, Scale={Scale}",
                user.OverlaySettings?.Position, user.OverlaySettings?.Scale);

            return Ok(user.OverlaySettings ?? new Core.Entities.OverlaySettings());
        }

        [HttpPut("overlay/settings")]
        public async Task<IActionResult> UpdateOverlaySettings([FromBody] Core.Entities.OverlaySettings request)
        {
            var userId = User.FindFirst("userId")?.Value;
            _logger.LogInformation("💾 UpdateOverlaySettings called for userId: {UserId}", userId);
            _logger.LogDebug("📥 Received settings: Position={Position}, Scale={Scale}, Enabled={Enabled}",
                request?.Position, request?.Scale, request?.Enabled);

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
                _logger.LogWarning("⚠️ Stream overlay feature not enabled for user {UserId}", userId);
                return StatusCode(403, new { error = "Stream overlay feature is not enabled for your account" });
            }

            // Validate position
            var validPositions = new[] { "top-left", "top-right", "bottom-left", "bottom-right" };
            if (!string.IsNullOrEmpty(request.Position) && !validPositions.Contains(request.Position))
            {
                _logger.LogWarning("⚠️ Invalid position: {Position}", request.Position);
                return BadRequest(new { error = "Invalid position. Must be one of: " + string.Join(", ", validPositions) });
            }

            user.OverlaySettings = request;
            await _userRepository.SaveUserAsync(user);

            _logger.LogInformation("✅ Overlay settings updated successfully for user {UserId}", userId);

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
            _logger.LogInformation("📊 GetPublicCounters called for userId: {UserId}", userId);

            var counters = await _counterRepository.GetCountersAsync(userId);
            if (counters == null)
            {
                _logger.LogWarning("⚠️ Counters not found for userId: {UserId}", userId);
                return NotFound();
            }

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null)
            {
                _logger.LogWarning("⚠️ User not found for userId: {UserId}", userId);
                return NotFound();
            }

            _logger.LogDebug("📋 User OverlaySettings for {UserId}: Position={Position}, Scale={Scale}, Enabled={Enabled}",
                userId, user.OverlaySettings?.Position, user.OverlaySettings?.Scale, user.OverlaySettings?.Enabled);
            _logger.LogDebug("📋 OverlaySettings.Counters: Deaths={Deaths}, Swears={Swears}, Screams={Screams}, Bits={Bits}",
                user.OverlaySettings?.Counters?.Deaths, user.OverlaySettings?.Counters?.Swears,
                user.OverlaySettings?.Counters?.Screams, user.OverlaySettings?.Counters?.Bits);

            var response = new
            {
                deaths = counters.Deaths,
                swears = counters.Swears,
                screams = counters.Screams,
                bits = counters.Bits,
                lastUpdated = counters.LastUpdated,
                streamStarted = counters.StreamStarted,
                settings = user.OverlaySettings
            };

            _logger.LogInformation("✅ Returning public counters for {UserId}: Deaths={Deaths}, Swears={Swears}",
                userId, counters.Deaths, counters.Swears);

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
