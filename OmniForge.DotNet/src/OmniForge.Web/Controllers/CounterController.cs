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
}
