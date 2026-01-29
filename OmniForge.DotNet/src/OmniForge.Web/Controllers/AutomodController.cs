using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmniForge.Core.Exceptions;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Core.Utilities;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;

namespace OmniForge.Web.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/automod")]
    public class AutomodController : ControllerBase
    {
        private readonly ITwitchApiService _twitchApiService;
        private readonly ILogger<AutomodController> _logger;

        public AutomodController(ITwitchApiService twitchApiService, ILogger<AutomodController> logger)
        {
            _twitchApiService = twitchApiService;
            _logger = logger;
        }

        [HttpGet("settings")]
        public async Task<IActionResult> GetSettings()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var safeUserId = userId!;

            try
            {
                _logger.LogInformation("API AutoMod GET requested for user {UserId}", (safeUserId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
                var settings = await _twitchApiService.GetAutomodSettingsAsync(safeUserId!);
                return Ok(settings);
            }
            catch (ReauthRequiredException ex)
            {
                _logger.LogWarning(ex, "API AutoMod GET requires reauth for user {UserId}: {Message}", (safeUserId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"), (ex.Message ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
                return Unauthorized(new { error = "Authentication expired", requireReauth = true, authUrl = "/auth/twitch", logoutUrl = "/auth/logout?reauth=1" });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "API AutoMod GET blocked for user {UserId}: {Message}", (safeUserId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"), (ex.Message ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
                return StatusCode(403, new { error = ex.Message, reauthUrl = "/auth/twitch" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "API AutoMod GET failed for user {UserId}: {Message}", (safeUserId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"), (ex.Message ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
                return StatusCode(500, new { error = "Failed to get AutoMod settings", details = ex.Message });
            }
        }

        [HttpPut("settings")]
        public async Task<IActionResult> UpdateSettings([FromBody, Required] AutomodSettingsDto settings)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var safeUserId = userId!;

            try
            {
                _logger.LogInformation("API AutoMod UPDATE requested for user {UserId}. OverallLevel={OverallLevel}", (safeUserId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"), settings.OverallLevel);
                var updated = await _twitchApiService.UpdateAutomodSettingsAsync(safeUserId!, settings);
                return Ok(updated);
            }
            catch (ReauthRequiredException ex)
            {
                _logger.LogWarning(ex, "API AutoMod UPDATE requires reauth for user {UserId}: {Message}", (safeUserId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"), (ex.Message ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
                return Unauthorized(new { error = "Authentication expired", requireReauth = true, authUrl = "/auth/twitch", logoutUrl = "/auth/logout?reauth=1" });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "API AutoMod UPDATE blocked for user {UserId}: {Message}", (safeUserId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"), (ex.Message ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
                return StatusCode(403, new { error = ex.Message, reauthUrl = "/auth/twitch" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "API AutoMod UPDATE failed for user {UserId}: {Message}", (safeUserId ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"), (ex.Message ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n"));
                return StatusCode(500, new { error = "Failed to update AutoMod settings", details = ex.Message });
            }
        }
    }
}
