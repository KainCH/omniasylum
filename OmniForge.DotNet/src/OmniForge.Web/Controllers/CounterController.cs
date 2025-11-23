using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmniForge.Core.Interfaces;
using System;
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

        public CounterController(
            ICounterRepository counterRepository,
            IUserRepository userRepository,
            INotificationService notificationService,
            IOverlayNotifier overlayNotifier)
        {
            _counterRepository = counterRepository;
            _userRepository = userRepository;
            _notificationService = notificationService;
            _overlayNotifier = overlayNotifier;
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
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            if (!user.Features.StreamOverlay)
            {
                return StatusCode(403, new { error = "Stream overlay feature is not enabled for your account" });
            }

            return Ok(user.OverlaySettings ?? new Core.Entities.OverlaySettings());
        }

        [HttpPut("overlay/settings")]
        public async Task<IActionResult> UpdateOverlaySettings([FromBody] Core.Entities.OverlaySettings request)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            if (!user.Features.StreamOverlay)
            {
                return StatusCode(403, new { error = "Stream overlay feature is not enabled for your account" });
            }

            // Validate position
            var validPositions = new[] { "top-left", "top-right", "bottom-left", "bottom-right" };
            if (!string.IsNullOrEmpty(request.Position) && !System.Linq.Enumerable.Contains(validPositions, request.Position))
            {
                return BadRequest(new { error = "Invalid position. Must be one of: " + string.Join(", ", validPositions) });
            }

            user.OverlaySettings = request;
            await _userRepository.SaveUserAsync(user);

            return Ok(new
            {
                message = "Overlay settings updated successfully",
                settings = user.OverlaySettings
            });
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
