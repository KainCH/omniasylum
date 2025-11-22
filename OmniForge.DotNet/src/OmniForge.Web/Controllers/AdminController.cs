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
    [Authorize(Roles = "admin")]
    [ApiController]
    [Route("api/admin")]
    public class AdminController : ControllerBase
    {
        private readonly IUserRepository _userRepository;

        public AdminController(IUserRepository userRepository)
        {
            _userRepository = userRepository;
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
                validUsers = users.Where(u => !string.IsNullOrEmpty(u.TwitchUserId) && !string.IsNullOrEmpty(u.Username)).Select(u => u.Username),
                brokenUsers = users.Where(u => string.IsNullOrEmpty(u.TwitchUserId)).Select(u => u.Username),
                suspiciousUsers = users.Where(u => !string.IsNullOrEmpty(u.TwitchUserId) && string.IsNullOrEmpty(u.Username)).Select(u => u.TwitchUserId)
            };

            return Ok(diagnostics);
        }

        [HttpGet("users/{userId}")]
        public async Task<IActionResult> GetUser(string userId)
        {
            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            return Ok(user);
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

        [HttpPut("users/{userId}/status")]
        public async Task<IActionResult> UpdateUserStatus(string userId, [FromBody] UpdateUserStatusRequest request)
        {
            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            user.IsActive = request.IsActive;
            await _userRepository.SaveUserAsync(user);

            return Ok(new { message = $"User status updated to {(request.IsActive ? "active" : "inactive")}", user });
        }

        [HttpPost("users/{userId}/features")]
        public async Task<IActionResult> UpdateFeatures(string userId, [FromBody] FeatureFlags features)
        {
            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound();

            user.Features = features;
            await _userRepository.SaveUserAsync(user);

            return Ok(user);
        }

        [HttpDelete("users/{userId}")]
        public async Task<IActionResult> DeleteUser(string userId)
        {
            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            // Prevent deleting self
            var currentUserId = User.FindFirst("userId")?.Value;
            if (userId == currentUserId)
            {
                return BadRequest("Cannot delete your own account");
            }

            await _userRepository.DeleteUserAsync(userId);

            return Ok(new { message = "User deleted successfully" });
        }
    }

    public class UpdateRoleRequest
    {
        public string Role { get; set; } = string.Empty;
    }

    public class UpdateUserStatusRequest
    {
        public bool IsActive { get; set; }
    }
}
