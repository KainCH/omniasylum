using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
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

        [HttpPut("~/api/overlay-settings")]
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
                channelId = user.DiscordChannelId,
                enabled = !string.IsNullOrEmpty(user.DiscordChannelId) || !string.IsNullOrEmpty(user.DiscordWebhookUrl)
            });
        }

        [HttpPut("discord-webhook")]
        public async Task<IActionResult> UpdateDiscordWebhook([FromBody] UpdateWebhookRequest request)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            // Handle backward compatibility (legacy webhook) + new bot channelId
            string actualWebhookUrl = request.WebhookUrl ?? request.DiscordWebhookUrl ?? string.Empty;
            string actualChannelId = request.ChannelId ?? request.DiscordChannelId ?? string.Empty;

            // Clearing
            if (string.IsNullOrEmpty(actualWebhookUrl) && string.IsNullOrEmpty(actualChannelId))
            {
                user.DiscordWebhookUrl = string.Empty;
                user.DiscordChannelId = string.Empty;
                await _userRepository.SaveUserAsync(user);

                return Ok(new
                {
                    message = "Discord destination cleared successfully",
                    webhookUrl = user.DiscordWebhookUrl,
                    channelId = user.DiscordChannelId,
                    verified = new { webhookUrl = user.DiscordWebhookUrl, channelId = user.DiscordChannelId, enabled = false }
                });
            }

            // Preferred: channelId validation
            if (!string.IsNullOrEmpty(actualChannelId))
            {
                if (!IsValidSnowflake(actualChannelId))
                {
                    return BadRequest(new { error = "Invalid Discord channel ID format" });
                }

                var channelValid = await _discordService.ValidateDiscordChannelAsync(actualChannelId);
                if (!channelValid)
                {
                    return BadRequest(new { error = "The Discord channel ID is invalid or the bot does not have access. Ensure the bot is invited and has Send Messages + Embed Links." });
                }

                user.DiscordChannelId = actualChannelId;
            }

            // Legacy: webhook URL validation (kept for migration)
            if (!string.IsNullOrEmpty(actualWebhookUrl))
            {
                if (!actualWebhookUrl.StartsWith("https://discord.com/api/webhooks/"))
                {
                    return BadRequest(new { error = "Invalid Discord webhook URL format" });
                }

                // Validate that the webhook actually exists
                var isValid = await _discordService.ValidateWebhookAsync(actualWebhookUrl);
                if (!isValid)
                {
                    return BadRequest(new { error = "The Discord webhook URL is invalid or does not exist. Please create a new webhook in Discord." });
                }

                user.DiscordWebhookUrl = actualWebhookUrl;
            }

            await _userRepository.SaveUserAsync(user);

            return Ok(new
            {
                message = "Discord destination updated successfully",
                webhookUrl = user.DiscordWebhookUrl,
                channelId = user.DiscordChannelId,
                verified = new { webhookUrl = user.DiscordWebhookUrl, channelId = user.DiscordChannelId, enabled = !string.IsNullOrEmpty(user.DiscordChannelId) || !string.IsNullOrEmpty(user.DiscordWebhookUrl) }
            });
        }

        private static bool IsValidSnowflake(string value)
        {
            // Discord snowflakes are numeric strings (typically 17-20 digits). Keep validation permissive.
            if (value.Length < 15 || value.Length > 25) return false;
            return value.All(char.IsDigit);
        }

        [HttpPost("discord-webhook/test")]
        public async Task<IActionResult> TestDiscordWebhook()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            if (string.IsNullOrEmpty(user.DiscordChannelId) && string.IsNullOrEmpty(user.DiscordWebhookUrl))
            {
                return BadRequest(new { error = "No Discord destination configured" });
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

            var ds = user.DiscordSettings;
            var defaultSettings = new DiscordSettings(); // To get defaults if needed

            // Map to flat structure for frontend compatibility
            var response = new
            {
                webhookUrl = user.DiscordWebhookUrl,
                channelId = user.DiscordChannelId,
                enabled = !string.IsNullOrEmpty(user.DiscordChannelId) || !string.IsNullOrEmpty(user.DiscordWebhookUrl),
                templateStyle = user.Features.TemplateStyle ?? ds.TemplateStyle ?? "asylum_themed",

                enableChannelNotifications = ds.EnableChannelNotifications,
                deathMilestoneEnabled = ds.EnabledNotifications.DeathMilestone,
                swearMilestoneEnabled = ds.EnabledNotifications.SwearMilestone,

                deathThresholds = string.Join(",", ds.MilestoneThresholds.Deaths),
                swearThresholds = string.Join(",", ds.MilestoneThresholds.Swears),

                enabledNotifications = new
                {
                    death_milestone = ds.EnabledNotifications.DeathMilestone,
                    swear_milestone = ds.EnabledNotifications.SwearMilestone,
                    stream_start = ds.EnabledNotifications.StreamStart,
                    stream_end = ds.EnabledNotifications.StreamEnd,
                    follower_goal = ds.EnabledNotifications.FollowerGoal,
                    subscriber_milestone = ds.EnabledNotifications.SubscriberMilestone,
                    channel_point_redemption = ds.EnabledNotifications.ChannelPointRedemption
                },
                milestoneThresholds = new
                {
                    deaths = ds.MilestoneThresholds.Deaths,
                    swears = ds.MilestoneThresholds.Swears
                }
            };

            return Ok(response);
        }

        [HttpPut("discord-settings")]
        public async Task<IActionResult> UpdateDiscordSettings([FromBody] UpdateDiscordSettingsRequest request)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            // Update Template Style
            if (!string.IsNullOrEmpty(request.TemplateStyle))
            {
                user.Features.TemplateStyle = request.TemplateStyle;
                user.DiscordSettings.TemplateStyle = request.TemplateStyle;
            }

            // Update Enabled Notifications
            if (request.EnabledNotifications != null)
            {
                user.DiscordSettings.EnabledNotifications.DeathMilestone = request.EnabledNotifications.DeathMilestone ?? request.DeathMilestoneEnabled ?? user.DiscordSettings.EnabledNotifications.DeathMilestone;
                user.DiscordSettings.EnabledNotifications.SwearMilestone = request.EnabledNotifications.SwearMilestone ?? request.SwearMilestoneEnabled ?? user.DiscordSettings.EnabledNotifications.SwearMilestone;
                user.DiscordSettings.EnabledNotifications.StreamStart = request.EnabledNotifications.StreamStart ?? user.DiscordSettings.EnabledNotifications.StreamStart;
                user.DiscordSettings.EnabledNotifications.StreamEnd = request.EnabledNotifications.StreamEnd ?? user.DiscordSettings.EnabledNotifications.StreamEnd;
                user.DiscordSettings.EnabledNotifications.FollowerGoal = request.EnabledNotifications.FollowerGoal ?? user.DiscordSettings.EnabledNotifications.FollowerGoal;
                user.DiscordSettings.EnabledNotifications.SubscriberMilestone = request.EnabledNotifications.SubscriberMilestone ?? user.DiscordSettings.EnabledNotifications.SubscriberMilestone;
                user.DiscordSettings.EnabledNotifications.ChannelPointRedemption = request.EnabledNotifications.ChannelPointRedemption ?? user.DiscordSettings.EnabledNotifications.ChannelPointRedemption;
            }
            else
            {
                // Fallback to flat properties if structured object is missing
                if (request.DeathMilestoneEnabled.HasValue) user.DiscordSettings.EnabledNotifications.DeathMilestone = request.DeathMilestoneEnabled.Value;
                if (request.SwearMilestoneEnabled.HasValue) user.DiscordSettings.EnabledNotifications.SwearMilestone = request.SwearMilestoneEnabled.Value;
            }

            // Update Thresholds
            if (!string.IsNullOrEmpty(request.DeathThresholds))
            {
                user.DiscordSettings.MilestoneThresholds.Deaths = ParseThresholds(request.DeathThresholds);
            }
            else if (request.MilestoneThresholds?.Deaths != null)
            {
                user.DiscordSettings.MilestoneThresholds.Deaths = request.MilestoneThresholds.Deaths;
            }

            if (!string.IsNullOrEmpty(request.SwearThresholds))
            {
                user.DiscordSettings.MilestoneThresholds.Swears = ParseThresholds(request.SwearThresholds);
            }
            else if (request.MilestoneThresholds?.Swears != null)
            {
                user.DiscordSettings.MilestoneThresholds.Swears = request.MilestoneThresholds.Swears;
            }

            // Legacy
            if (request.EnableChannelNotifications.HasValue)
            {
                user.DiscordSettings.EnableChannelNotifications = request.EnableChannelNotifications.Value;
            }

            await _userRepository.SaveUserAsync(user);

            // Return structured format
            return Ok(new { message = "Discord notification settings updated successfully", settings = user.DiscordSettings });
        }

        private List<int> ParseThresholds(string thresholds)
        {
            if (string.IsNullOrWhiteSpace(thresholds)) return new List<int>();
            var result = new List<int>();
            foreach (var s in thresholds.Split(','))
            {
                if (int.TryParse(s.Trim(), out int n))
                {
                    result.Add(n);
                }
            }
            return result;
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
        public string? DiscordWebhookUrl { get; set; }
        public string? ChannelId { get; set; }
        public string? DiscordChannelId { get; set; }
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

    public class UpdateDiscordSettingsRequest
    {
        public string? TemplateStyle { get; set; }
        public UpdateDiscordNotificationsRequest? EnabledNotifications { get; set; }
        public UpdateMilestoneThresholdsRequest? MilestoneThresholds { get; set; }

        // Flat properties
        public bool? EnableChannelNotifications { get; set; }
        public bool? DeathMilestoneEnabled { get; set; }
        public bool? SwearMilestoneEnabled { get; set; }
        public string? DeathThresholds { get; set; }
        public string? SwearThresholds { get; set; }
    }

    public class UpdateDiscordNotificationsRequest
    {
        public bool? DeathMilestone { get; set; }
        public bool? SwearMilestone { get; set; }
        public bool? StreamStart { get; set; }
        public bool? StreamEnd { get; set; }
        public bool? FollowerGoal { get; set; }
        public bool? SubscriberMilestone { get; set; }
        public bool? ChannelPointRedemption { get; set; }
    }

    public class UpdateMilestoneThresholdsRequest
    {
        public List<int>? Deaths { get; set; }
        public List<int>? Swears { get; set; }
    }
}
