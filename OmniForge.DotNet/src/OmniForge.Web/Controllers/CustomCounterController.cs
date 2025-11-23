using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;

namespace OmniForge.Web.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/custom-counters")]
    public class CustomCounterController : ControllerBase
    {
        private readonly ICounterRepository _counterRepository;
        private readonly IUserRepository _userRepository;
        private readonly IOverlayNotifier _overlayNotifier;

        public CustomCounterController(
            ICounterRepository counterRepository,
            IUserRepository userRepository,
            IOverlayNotifier overlayNotifier)
        {
            _counterRepository = counterRepository;
            _userRepository = userRepository;
            _overlayNotifier = overlayNotifier;
        }

        [HttpGet]
        public async Task<IActionResult> GetCustomCounters()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var config = await _counterRepository.GetCustomCountersConfigAsync(userId);
            return Ok(config);
        }

        [HttpPut]
        public async Task<IActionResult> SaveCustomCounters([FromBody] CustomCounterConfiguration config)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            if (config.Counters == null)
            {
                return BadRequest("Counters configuration is required");
            }

            // Validate
            foreach (var kvp in config.Counters)
            {
                if (string.IsNullOrEmpty(kvp.Value.Name) || string.IsNullOrEmpty(kvp.Value.Icon))
                {
                    return BadRequest($"Counter {kvp.Key} missing required fields (name, icon)");
                }
            }

            await _counterRepository.SaveCustomCountersConfigAsync(userId, config);

            // Emit update via SignalR
            await _overlayNotifier.NotifyCustomAlertAsync(userId, "customCountersUpdated", new { counters = config.Counters });

            return Ok(new { success = true, counters = config.Counters });
        }

        [HttpPost("{counterId}/increment")]
        public async Task<IActionResult> IncrementCounter(string counterId)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            // Get configuration to know increment amount and milestones
            var config = await _counterRepository.GetCustomCountersConfigAsync(userId);
            if (!config.Counters.TryGetValue(counterId, out var counterDef))
            {
                return NotFound(new { error = "Custom counter not found" });
            }

            // Get current value to check milestones
            var counters = await _counterRepository.GetCountersAsync(userId);
            int previousValue = 0;
            if (counters.CustomCounters.TryGetValue(counterId, out int val))
            {
                previousValue = val;
            }

            // Increment
            var updatedCounter = await _counterRepository.IncrementCounterAsync(userId, counterId, counterDef.IncrementBy);
            int newValue = updatedCounter.CustomCounters[counterId];
            int change = counterDef.IncrementBy;

            // Check milestones
            if (counterDef.Milestones != null && counterDef.Milestones.Any())
            {
                var crossedMilestones = counterDef.Milestones.Where(t => previousValue < t && newValue >= t).ToList();
                foreach (var milestone in crossedMilestones)
                {
                    // Send overlay notification
                    await _overlayNotifier.NotifyCustomAlertAsync(userId, "customMilestoneReached", new
                    {
                        counterId = counterId,
                        counterName = counterDef.Name,
                        milestone = milestone,
                        newValue = newValue,
                        icon = counterDef.Icon
                    });
                }
            }

            // Send update to overlay
            await _overlayNotifier.NotifyCustomAlertAsync(userId, "customCounterUpdate", new
            {
                counterId = counterId,
                value = newValue,
                change = change
            });

            return Ok(new { counterId = counterId, value = newValue, change = change });
        }

        [HttpPost("{counterId}/decrement")]
        public async Task<IActionResult> DecrementCounter(string counterId)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            // Get configuration
            var config = await _counterRepository.GetCustomCountersConfigAsync(userId);
            if (!config.Counters.TryGetValue(counterId, out var counterDef))
            {
                return NotFound(new { error = "Custom counter not found" });
            }

            int decrementBy = counterDef.DecrementBy > 0 ? counterDef.DecrementBy : counterDef.IncrementBy;
            if (decrementBy <= 0) decrementBy = 1;

            // Get previous value to calculate actual change (can't go below 0)
            var counters = await _counterRepository.GetCountersAsync(userId);
            int previousValue = counters.CustomCounters.ContainsKey(counterId) ? counters.CustomCounters[counterId] : 0;

            var updatedCounter = await _counterRepository.DecrementCounterAsync(userId, counterId, decrementBy);
            int newValue = updatedCounter.CustomCounters.ContainsKey(counterId) ? updatedCounter.CustomCounters[counterId] : 0;
            int change = newValue - previousValue;

            // Send update to overlay
            await _overlayNotifier.NotifyCustomAlertAsync(userId, "customCounterUpdate", new
            {
                counterId = counterId,
                value = newValue,
                change = change
            });

            return Ok(new { counterId = counterId, value = newValue, change = change });
        }

        [HttpPost("{counterId}/reset")]
        public async Task<IActionResult> ResetCounter(string counterId)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            // Get configuration
            var config = await _counterRepository.GetCustomCountersConfigAsync(userId);
            if (!config.Counters.ContainsKey(counterId))
            {
                return NotFound(new { error = "Custom counter not found" });
            }

            // Get previous value
            var counters = await _counterRepository.GetCountersAsync(userId);
            int previousValue = counters.CustomCounters.ContainsKey(counterId) ? counters.CustomCounters[counterId] : 0;

            var updatedCounter = await _counterRepository.ResetCounterAsync(userId, counterId);
            int change = -previousValue;

            // Send update to overlay
            await _overlayNotifier.NotifyCustomAlertAsync(userId, "customCounterUpdate", new
            {
                counterId = counterId,
                value = 0,
                change = change
            });

            return Ok(new { counterId = counterId, value = 0, change = change });
        }
    }
}
