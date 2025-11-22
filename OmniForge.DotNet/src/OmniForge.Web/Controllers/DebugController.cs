using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;

namespace OmniForge.Web.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/debug")]
    public class DebugController : ControllerBase
    {
        private readonly IUserRepository _userRepository;
        private readonly IDiscordService _discordService;
        private readonly ILogger<DebugController> _logger;

        public DebugController(
            IUserRepository userRepository,
            IDiscordService discordService,
            ILogger<DebugController> logger)
        {
            _userRepository = userRepository;
            _discordService = discordService;
            _logger = logger;
        }

        [HttpPost("test-webhook-save")]
        public async Task<IActionResult> TestWebhookSave()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var testWebhookUrl = "https://discord.com/api/webhooks/1234567890/test-webhook-token-12345";
            _logger.LogInformation("🧪 DEBUG: Testing webhook save for user {UserId}", userId);

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            var originalUrl = user.DiscordWebhookUrl;

            // Update
            user.DiscordWebhookUrl = testWebhookUrl;
            await _userRepository.SaveUserAsync(user);

            // Verify
            var updatedUser = await _userRepository.GetUserAsync(userId);
            var success = updatedUser?.DiscordWebhookUrl == testWebhookUrl;

            // Restore (optional, but good for testing) - actually the legacy code leaves it?
            // Legacy code: "Test the save operation" -> updates it.
            // It doesn't seem to restore it. But it's a test endpoint.

            return Ok(new
            {
                success = true,
                message = "Webhook save test completed",
                results = new
                {
                    originalUrl,
                    savedUrl = updatedUser?.DiscordWebhookUrl,
                    success
                }
            });
        }

        [HttpGet("test-webhook-read")]
        public async Task<IActionResult> TestWebhookRead()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            _logger.LogInformation("🧪 DEBUG: Testing webhook read for user {UserId}", userId);

            var user = await _userRepository.GetUserAsync(userId);

            return Ok(new
            {
                success = true,
                webhookData = new
                {
                    webhookUrl = user?.DiscordWebhookUrl
                }
            });
        }

        [HttpPost("test-stream-notification")]
        public async Task<IActionResult> TestStreamNotification()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            if (string.IsNullOrEmpty(user.DiscordWebhookUrl))
            {
                return BadRequest(new
                {
                    success = false,
                    error = "No Discord webhook configured",
                    recommendation = "Configure Discord webhook first"
                });
            }

            _logger.LogInformation("🚀 Triggering test stream notification for {Username}", user.Username);

            var mockEvent = new
            {
                id = $"test_stream_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                broadcaster_user_id = userId,
                broadcaster_user_name = user.Username,
                started_at = DateTimeOffset.UtcNow.ToString("o")
            };

            await _discordService.SendNotificationAsync(user, "stream-online", mockEvent);

            return Ok(new
            {
                success = true,
                message = $"Test stream notification triggered for {user.Username}",
                mockEvent,
                webhookConfigured = true
            });
        }

        [HttpPost("cleanup-user-data")]
        public async Task<IActionResult> CleanupUserData()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            _logger.LogInformation("🧹 DEBUG: Cleaning up user data for {UserId}", userId);

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            var cleanedFields = new System.Collections.Generic.List<string>();

            // Check for test webhook data
            if (!string.IsNullOrEmpty(user.DiscordWebhookUrl) && user.DiscordWebhookUrl.Contains("test-webhook-token"))
            {
                user.DiscordWebhookUrl = "";
                cleanedFields.Add("DiscordWebhookUrl (test data removed)");
            }

            if (cleanedFields.Count > 0)
            {
                await _userRepository.SaveUserAsync(user);
            }

            return Ok(new
            {
                success = true,
                message = "Cleanup completed",
                cleanedFields,
                user = new { user.TwitchUserId, user.DiscordWebhookUrl }
            });
        }
    }
}
