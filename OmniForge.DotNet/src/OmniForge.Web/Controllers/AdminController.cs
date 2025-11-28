using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;

namespace OmniForge.Web.Controllers
{
    [Authorize(Roles = "admin", AuthenticationSchemes = "Bearer,Cookies")]
    [ApiController]
    [Route("api/admin")]
    public class AdminController : ControllerBase
    {
        private readonly IUserRepository _userRepository;
        private readonly ICounterRepository _counterRepository;
        private readonly ITwitchClientManager _twitchClientManager;

        public AdminController(
            IUserRepository userRepository,
            ICounterRepository counterRepository,
            ITwitchClientManager twitchClientManager)
        {
            _userRepository = userRepository;
            _counterRepository = counterRepository;
            _twitchClientManager = twitchClientManager;
        }

        [HttpGet("users")]
        public async Task<IActionResult> GetAllUsers()
        {
            var users = await _userRepository.GetAllUsersAsync();

            var sanitizedUsers = users.Select(user => new
            {
                userId = user.TwitchUserId,
                twitchUserId = user.TwitchUserId,
                username = user.Username,
                displayName = user.DisplayName,
                email = user.Email,
                profileImageUrl = user.ProfileImageUrl,
                role = user.Role,
                features = user.Features,
                isActive = user.IsActive,
                createdAt = user.CreatedAt,
                lastLogin = user.LastLogin,
                discordWebhookUrl = user.DiscordWebhookUrl,
                userStatus = string.IsNullOrEmpty(user.TwitchUserId) ? "broken" :
                             (string.IsNullOrEmpty(user.Username) ? "incomplete" : "complete")
            });

            return Ok(new
            {
                users = sanitizedUsers,
                total = sanitizedUsers.Count()
            });
        }

        [HttpGet("users/diagnostics")]
        public async Task<IActionResult> GetUserDiagnostics()
        {
            var users = await _userRepository.GetAllUsersAsync();

            var diagnostics = new
            {
                totalUsers = users.Count(),
                validUsers = users.Where(u => !string.IsNullOrEmpty(u.TwitchUserId) && !string.IsNullOrEmpty(u.Username))
                    .Select(u => new
                    {
                        username = u.Username,
                        displayName = u.DisplayName,
                        twitchUserId = u.TwitchUserId,
                        role = u.Role,
                        isActive = u.IsActive
                    }),
                brokenUsers = users.Where(u => string.IsNullOrEmpty(u.TwitchUserId))
                    .Select(u => new
                    {
                        username = u.Username,
                        tempDeleteId = new Random().Next(100, 999).ToString(),
                        canDelete = true
                    }),
                suspiciousUsers = users.Where(u => !string.IsNullOrEmpty(u.TwitchUserId) && string.IsNullOrEmpty(u.Username))
                    .Select(u => new
                    {
                        twitchUserId = u.TwitchUserId,
                        role = u.Role
                    })
            };

            return Ok(diagnostics);
        }

        [HttpGet("users/{userId}")]
        public async Task<IActionResult> GetUser(string userId)
        {
            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            var counters = await _counterRepository.GetCountersAsync(userId);

            return Ok(new
            {
                user = new
                {
                    userId = user.TwitchUserId,
                    username = user.Username,
                    displayName = user.DisplayName,
                    email = user.Email,
                    profileImageUrl = user.ProfileImageUrl,
                    role = user.Role,
                    features = user.Features,
                    isActive = user.IsActive,
                    createdAt = user.CreatedAt,
                    lastLogin = user.LastLogin
                },
                counters = counters
            });
        }

        [HttpPut("users/{userId}/role")]
        public async Task<IActionResult> UpdateUserRole(string userId, [FromBody] UpdateRoleRequest request)
        {
            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            if (request.Role != "admin" && request.Role != "streamer" && request.Role != "mod")
            {
                return BadRequest("Invalid role");
            }

            // Prevent removing own admin role
            var currentUserId = User.FindFirst("userId")?.Value;
            if (userId == currentUserId && user.Role == "admin" && request.Role != "admin")
            {
                return BadRequest("Cannot remove your own admin role");
            }

            user.Role = request.Role;
            await _userRepository.SaveUserAsync(user);

            return Ok(new { message = $"User role updated to {request.Role}", user });
        }

        [HttpPut("users/{userId}/features")]
        public async Task<IActionResult> UpdateFeatures(string userId, [FromBody] UpdateFeaturesRequest request)
        {
            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            var features = request.Features;
            if (features == null) return BadRequest("Invalid features object");

            bool wasChatEnabled = user.Features.ChatCommands;
            bool isChatBeingEnabled = features.ChatCommands && !wasChatEnabled;
            bool isChatBeingDisabled = !features.ChatCommands && wasChatEnabled;

            bool wasOverlayEnabled = user.Features.StreamOverlay;
            bool isOverlayBeingEnabled = features.StreamOverlay && !wasOverlayEnabled;

            user.Features = features;

            // Auto-enable overlay settings if feature enabled
            if (isOverlayBeingEnabled && !user.OverlaySettings.Enabled)
            {
                user.OverlaySettings.Enabled = true;
            }

            await _userRepository.SaveUserAsync(user);

            // Handle Twitch bot connection
            if (isChatBeingEnabled)
            {
                await _twitchClientManager.ConnectUserAsync(userId);
            }
            else if (isChatBeingDisabled)
            {
                await _twitchClientManager.DisconnectUserAsync(userId);
            }

            return Ok(new { message = "Features updated successfully", user });
        }

        [HttpGet("stats")]
        public async Task<IActionResult> GetStats()
        {
            var users = await _userRepository.GetAllUsersAsync();

            var stats = new
            {
                totalUsers = users.Count(),
                activeUsers = users.Count(u => u.IsActive),
                adminUsers = users.Count(u => u.Role == "admin"),
                recentLogins = users
                    .OrderByDescending(u => u.LastLogin)
                    .Take(10)
                    .Select(u => new
                    {
                        username = u.Username,
                        displayName = u.DisplayName,
                        lastLogin = u.LastLogin
                    }),
                featureUsage = new
                {
                    chatCommands = users.Count(u => u.Features.ChatCommands),
                    channelPoints = users.Count(u => u.Features.ChannelPoints),
                    autoClip = users.Count(u => u.Features.AutoClip),
                    customCommands = users.Count(u => u.Features.CustomCommands),
                    analytics = users.Count(u => u.Features.Analytics),
                    webhooks = users.Count(u => u.Features.Webhooks),
                    bitsIntegration = users.Count(u => u.Features.BitsIntegration),
                    streamOverlay = users.Count(u => u.Features.StreamOverlay),
                    alertAnimations = users.Count(u => u.Features.AlertAnimations),
                    streamAlerts = users.Count(u => u.Features.StreamAlerts)
                }
            };

            return Ok(stats);
        }

        [HttpDelete("users/{userId}")]
        public async Task<IActionResult> DeleteUser(string userId)
        {
            var user = await _userRepository.GetUserAsync(userId);

            // If user exists and is admin, prevent deletion
            if (user != null && user.Role == "admin")
            {
                return StatusCode(403, new { error = "Cannot delete admin accounts" });
            }

            // Prevent deleting self
            var currentUserId = User.FindFirst("userId")?.Value;
            if (userId == currentUserId)
            {
                return BadRequest("Cannot delete your own account");
            }

            await _userRepository.DeleteUserAsync(userId);

            return Ok(new { message = "User deleted successfully", userId });
        }
    }

    public class UpdateRoleRequest
    {
        public string Role { get; set; } = string.Empty;
    }

    public class UpdateFeaturesRequest
    {
        public FeatureFlags Features { get; set; } = new FeatureFlags();
    }
}
