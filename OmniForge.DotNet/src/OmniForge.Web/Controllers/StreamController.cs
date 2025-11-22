using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;

namespace OmniForge.Web.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/stream")]
    public class StreamController : ControllerBase
    {
        private readonly IUserRepository _userRepository;
        private readonly ICounterRepository _counterRepository;
        private readonly IOverlayNotifier _overlayNotifier;

        public StreamController(
            IUserRepository userRepository,
            ICounterRepository counterRepository,
            IOverlayNotifier overlayNotifier)
        {
            _userRepository = userRepository;
            _counterRepository = counterRepository;
            _overlayNotifier = overlayNotifier;
        }

        [HttpPost("status")]
        public async Task<IActionResult> UpdateStatus([FromBody] UpdateStatusRequest request)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            var currentStatus = user.StreamStatus ?? "offline";
            var newStatus = currentStatus;

            switch (request.Action)
            {
                case "prep":
                    if (currentStatus == "offline") newStatus = "prepping";
                    break;
                case "go-live":
                    if (currentStatus == "prepping")
                    {
                        newStatus = "live";
                        await StartStreamInternal(userId);
                    }
                    break;
                case "end-stream":
                    if (currentStatus == "live" || currentStatus == "ending")
                    {
                        newStatus = "offline";
                        await EndStreamInternal(userId);
                    }
                    break;
                case "cancel-prep":
                    if (currentStatus == "prepping") newStatus = "offline";
                    break;
                default:
                    return BadRequest("Invalid action");
            }

            user.StreamStatus = newStatus;
            user.IsActive = newStatus == "prepping" || newStatus == "live";
            await _userRepository.SaveUserAsync(user);

            await _overlayNotifier.NotifyStreamStatusUpdateAsync(userId, newStatus);

            return Ok(new
            {
                message = $"Stream status updated to {newStatus}",
                streamStatus = newStatus,
                previousStatus = currentStatus
            });
        }

        [HttpGet("session")]
        public async Task<IActionResult> GetSession()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var counters = await _counterRepository.GetCountersAsync(userId);
            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            return Ok(new
            {
                isLive = counters?.StreamStarted != null,
                streamStarted = counters?.StreamStarted,
                counters = new
                {
                    deaths = counters?.Deaths ?? 0,
                    swears = counters?.Swears ?? 0,
                    bits = counters?.Bits ?? 0
                },
                settings = user.Features.StreamSettings
            });
        }

        [HttpPost("start")]
        public async Task<IActionResult> StartStream()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var counters = await StartStreamInternal(userId);

            return Ok(new
            {
                message = "Stream started successfully",
                streamStarted = counters.StreamStarted,
                counters = counters
            });
        }

        [HttpPost("end")]
        public async Task<IActionResult> EndStream()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var counters = await EndStreamInternal(userId);

            return Ok(new
            {
                message = "Stream ended successfully",
                counters = counters
            });
        }

        [HttpGet("settings")]
        public async Task<IActionResult> GetSettings()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            return Ok(user.Features.StreamSettings);
        }

        [HttpPut("settings")]
        public async Task<IActionResult> UpdateSettings([FromBody] StreamSettings request)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            // Validate thresholds
            if (request.BitThresholds != null)
            {
                if (request.BitThresholds.Death < 1) return BadRequest("Death threshold must be a positive number");
                if (request.BitThresholds.Swear < 1) return BadRequest("Swear threshold must be a positive number");
                if (request.BitThresholds.Celebration < 1) return BadRequest("Celebration threshold must be a positive number");
            }

            user.Features.StreamSettings = request;
            await _userRepository.SaveUserAsync(user);

            return Ok(user.Features.StreamSettings);
        }

        private async Task<Counter> StartStreamInternal(string userId)
        {
            var counters = await _counterRepository.GetCountersAsync(userId);
            if (counters == null)
            {
                counters = new Counter { TwitchUserId = userId };
            }

            counters.Bits = 0;
            counters.StreamStarted = DateTimeOffset.UtcNow;

            await _counterRepository.SaveCountersAsync(counters);
            await _overlayNotifier.NotifyStreamStartedAsync(userId, counters);

            return counters;
        }

        private async Task<Counter> EndStreamInternal(string userId)
        {
            var counters = await _counterRepository.GetCountersAsync(userId);
            if (counters == null)
            {
                counters = new Counter { TwitchUserId = userId };
            }

            counters.StreamStarted = null;
            // Clear last notified stream ID if we were tracking it (not implemented in Counter entity yet, but logic is in JS)

            await _counterRepository.SaveCountersAsync(counters);
            await _overlayNotifier.NotifyStreamEndedAsync(userId, counters);

            return counters;
        }
    }

    public class UpdateStatusRequest
    {
        public string Action { get; set; } = string.Empty;
    }
}
