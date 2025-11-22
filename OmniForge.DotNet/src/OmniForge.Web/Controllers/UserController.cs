using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using System;
using System.Threading.Tasks;

namespace OmniForge.Web.Controllers
{
    [Route("api/user")]
    [ApiController]
    [Authorize(AuthenticationSchemes = "Bearer,Cookies")]
    public class UserController : ControllerBase
    {
        private readonly IUserRepository _userRepository;
        private readonly IOverlayNotifier _overlayNotifier;
        private readonly IDiscordService _discordService;

        public UserController(
            IUserRepository userRepository,
            IOverlayNotifier overlayNotifier,
            IDiscordService discordService)
        {
            _userRepository = userRepository;
            _overlayNotifier = overlayNotifier;
            _discordService = discordService;
        }

        [HttpGet("settings")]
        public async Task<IActionResult> GetSettings()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            return Ok(new
            {
                streamStatus = user.StreamStatus,
                overlaySettings = user.OverlaySettings,
                features = user.Features
            });
        }

        [HttpPut("overlay-settings")]
        public async Task<IActionResult> UpdateOverlaySettings([FromBody] OverlaySettings newSettings)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            user.OverlaySettings = newSettings;
            await _userRepository.SaveUserAsync(user);

            // Broadcast update
            await _overlayNotifier.NotifySettingsUpdateAsync(userId, newSettings);

            return Ok(new { message = "Overlay settings updated successfully", overlaySettings = newSettings });
        }

        [HttpGet("discord-webhook")]
        public async Task<IActionResult> GetDiscordWebhook()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            return Ok(new
            {
                webhookUrl = user.DiscordWebhookUrl,
                enabled = !string.IsNullOrEmpty(user.DiscordWebhookUrl) // Simplified logic
            });
        }

        [HttpPut("discord-webhook")]
        public async Task<IActionResult> UpdateDiscordWebhook([FromBody] UpdateWebhookRequest request)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            // Basic validation
            if (!string.IsNullOrEmpty(request.WebhookUrl) && !request.WebhookUrl.StartsWith("https://discord.com/api/webhooks/"))
            {
                return BadRequest(new { error = "Invalid Discord webhook URL format" });
            }

            user.DiscordWebhookUrl = request.WebhookUrl ?? string.Empty;
            // Note: 'enabled' flag is often derived from presence of URL in this simple model,
            // but if we want explicit enable/disable without clearing URL, we need a field in User entity.
            // The User entity has FeatureFlags.DiscordWebhook, maybe use that?
            // For now, we'll just save the URL.

            await _userRepository.SaveUserAsync(user);

            return Ok(new
            {
                message = "Discord webhook updated successfully",
                webhookUrl = user.DiscordWebhookUrl
            });
        }

        [HttpPost("discord-webhook/test")]
        public async Task<IActionResult> TestDiscordWebhook()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            if (string.IsNullOrEmpty(user.DiscordWebhookUrl))
            {
                return BadRequest(new { error = "No Discord webhook URL configured" });
            }

            try
            {
                await _discordService.SendTestNotificationAsync(user);
                return Ok(new { message = "Test notification sent successfully!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to send test notification", details = ex.Message });
            }
        }

        [HttpGet("discord-settings")]
        public async Task<IActionResult> GetDiscordSettings()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            return Ok(user.DiscordSettings);
        }

        [HttpPut("discord-settings")]
        public async Task<IActionResult> UpdateDiscordSettings([FromBody] DiscordSettings settings)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            user.DiscordSettings = settings;
            await _userRepository.SaveUserAsync(user);

            return Ok(new { message = "Discord notification settings updated successfully", settings });
        }

        [HttpGet("discord-invite")]
        public async Task<IActionResult> GetDiscordInvite()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            return Ok(new
            {
                discordInviteLink = user.DiscordInviteLink,
                hasInvite = !string.IsNullOrEmpty(user.DiscordInviteLink)
            });
        }

        [HttpPut("discord-invite")]
        public async Task<IActionResult> UpdateDiscordInvite([FromBody] UpdateInviteRequest request)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            user.DiscordInviteLink = request.DiscordInviteLink ?? string.Empty;
            await _userRepository.SaveUserAsync(user);

            return Ok(new
            {
                message = "Discord invite link updated successfully",
                discordInviteLink = user.DiscordInviteLink
            });
        }

        [HttpPut("template-style")]
        public async Task<IActionResult> UpdateTemplateStyle([FromBody] TemplateStyleRequest request)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            var validTemplates = new[] { "asylum_themed", "detailed", "minimal" };
            if (Array.IndexOf(validTemplates, request.TemplateStyle) == -1)
            {
                return BadRequest(new { error = "Invalid template style" });
            }

            user.Features.TemplateStyle = request.TemplateStyle;
            // Also update DiscordSettings to keep in sync if needed, or just rely on Features
            user.DiscordSettings.TemplateStyle = request.TemplateStyle;

            await _userRepository.SaveUserAsync(user);

            return Ok(new { message = "Template style updated successfully", templateStyle = request.TemplateStyle });
        }
    }

    public class UpdateWebhookRequest
    {
        public string? WebhookUrl { get; set; }
        public bool Enabled { get; set; }
    }

    public class UpdateInviteRequest
    {
        public string? DiscordInviteLink { get; set; }
    }

    public class TemplateStyleRequest
    {
        public string TemplateStyle { get; set; } = string.Empty;
    }
}
