using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using System;
using System.Threading.Tasks;

namespace OmniForge.Web.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/automod")]
    public class AutomodController : ControllerBase
    {
        private readonly ITwitchApiService _twitchApiService;

        public AutomodController(ITwitchApiService twitchApiService)
        {
            _twitchApiService = twitchApiService;
        }

        [HttpGet("settings")]
        public async Task<IActionResult> GetSettings()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            try
            {
                var settings = await _twitchApiService.GetAutomodSettingsAsync(userId);
                return Ok(settings);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to get AutoMod settings", details = ex.Message });
            }
        }

        [HttpPut("settings")]
        public async Task<IActionResult> UpdateSettings([FromBody] AutomodSettingsDto settings)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            if (settings == null) return BadRequest("Settings payload is required");

            try
            {
                var updated = await _twitchApiService.UpdateAutomodSettingsAsync(userId, settings);
                return Ok(updated);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to update AutoMod settings", details = ex.Message });
            }
        }
    }
}
