using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using System.Threading.Tasks;

namespace OmniForge.Web.Controllers
{
    [Route("api/admin")]
    [ApiController]
    [Authorize(Roles = "admin")]
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
            return Ok(new { users, total = System.Linq.Enumerable.Count(users) });
        }

        [HttpGet("users/{userId}")]
        public async Task<IActionResult> GetUser(string userId)
        {
            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound();
            return Ok(user);
        }

        [HttpPost("users/{userId}/toggle-active")]
        public async Task<IActionResult> ToggleActive(string userId)
        {
            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound();

            user.IsActive = !user.IsActive;
            await _userRepository.SaveUserAsync(user);

            return Ok(new { message = $"User {(user.IsActive ? "activated" : "deactivated")}", isActive = user.IsActive });
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
            if (user == null) return NotFound();

            // Prevent deleting self
            var currentUserId = User.FindFirst("userId")?.Value;
            if (currentUserId == userId)
            {
                return BadRequest("Cannot delete your own account");
            }

            await _userRepository.DeleteUserAsync(userId);
            return Ok(new { message = "User deleted successfully" });
        }
    }
}
