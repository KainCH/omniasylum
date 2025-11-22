using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Hubs;

namespace OmniForge.Web.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/custom-counters")]
    public class CustomCounterController : ControllerBase
    {
        private readonly ICounterRepository _counterRepository;
        private readonly IUserRepository _userRepository;
        private readonly IHubContext<OverlayHub> _hubContext;
        private readonly IOverlayNotifier _overlayNotifier;

        public CustomCounterController(
            ICounterRepository counterRepository,
            IUserRepository userRepository,
            IHubContext<OverlayHub> hubContext,
            IOverlayNotifier overlayNotifier)
        {
            _counterRepository = counterRepository;
            _userRepository = userRepository;
            _hubContext = hubContext;
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
            await _hubContext.Clients.Group($"user:{userId}").SendAsync("customCountersUpdated", new { counters = config.Counters });

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

            // Check milestones
            if (counterDef.Milestones != null && counterDef.Milestones.Any())
            {
                var crossedMilestones = counterDef.Milestones.Where(t => previousValue < t && newValue >= t).ToList();
                foreach (var milestone in crossedMilestones)
                {
                    // Send overlay notification
                    await _overlayNotifier.NotifyCustomAlertAsync(userId, "milestone", new
                    {
                        type = "custom_milestone",
                        counterId = counterId,
                        counterName = counterDef.Name,
                        count = milestone,
                        icon = counterDef.Icon
                    });
                }
            }

            // Send update to overlay
            await _overlayNotifier.NotifyCounterUpdateAsync(userId, updatedCounter);

            return Ok(new { success = true, value = newValue });
        }
    }
}
