using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;

namespace OmniForge.Web.Controllers
{
    [Route("api/profiles")]
    [ApiController]
    [Authorize(AuthenticationSchemes = "Bearer,Cookies")]
    public class BroadcastProfileController : ControllerBase
    {
        private readonly IBroadcastProfileRepository _profileRepo;
        private readonly ISceneActionRepository _sceneActionRepo;
        private readonly IUserRepository _userRepo;

        public BroadcastProfileController(
            IBroadcastProfileRepository profileRepo,
            ISceneActionRepository sceneActionRepo,
            IUserRepository userRepo)
        {
            _profileRepo = profileRepo;
            _sceneActionRepo = sceneActionRepo;
            _userRepo = userRepo;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepo.GetUserAsync(userId);
            if (user == null) return NotFound();
            if (!user.Features.SceneSync)
                return StatusCode(403, new { error = "Scene sync feature is not enabled" });

            var profiles = await _profileRepo.GetAllAsync(userId);
            return Ok(profiles);
        }

        [HttpGet("{profileId}")]
        public async Task<IActionResult> Get(string profileId)
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepo.GetUserAsync(userId);
            if (user == null) return NotFound();
            if (!user.Features.SceneSync)
                return StatusCode(403, new { error = "Scene sync feature is not enabled" });

            var profile = await _profileRepo.GetAsync(userId, profileId);
            if (profile == null) return NotFound();

            return Ok(profile);
        }

        [HttpPost]
        public async Task<IActionResult> Save([FromBody] BroadcastProfile profile)
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepo.GetUserAsync(userId);
            if (user == null) return NotFound();
            if (!user.Features.SceneSync)
                return StatusCode(403, new { error = "Scene sync feature is not enabled" });

            profile.UserId = userId;
            if (string.IsNullOrEmpty(profile.ProfileId))
                profile.ProfileId = Guid.NewGuid().ToString();

            await _profileRepo.SaveAsync(profile);
            return Ok(profile);
        }

        [HttpDelete("{profileId}")]
        public async Task<IActionResult> Delete(string profileId)
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepo.GetUserAsync(userId);
            if (user == null) return NotFound();
            if (!user.Features.SceneSync)
                return StatusCode(403, new { error = "Scene sync feature is not enabled" });

            await _profileRepo.DeleteAsync(userId, profileId);
            return NoContent();
        }

        [HttpPost("{profileId}/load")]
        public async Task<IActionResult> Load(string profileId)
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepo.GetUserAsync(userId);
            if (user == null) return NotFound();
            if (!user.Features.SceneSync)
                return StatusCode(403, new { error = "Scene sync feature is not enabled" });

            var profile = await _profileRepo.GetAsync(userId, profileId);
            if (profile == null) return NotFound(new { error = "Profile not found" });

            // Replace current scene actions with profile's scene actions
            var existing = await _sceneActionRepo.GetAllAsync(userId);
            foreach (var action in existing)
            {
                await _sceneActionRepo.DeleteAsync(userId, action.SceneName);
            }

            foreach (var action in profile.SceneActions)
            {
                action.UserId = userId;
                await _sceneActionRepo.SaveAsync(action);
            }

            return Ok(new { message = "Profile loaded", sceneActionsCount = profile.SceneActions.Count });
        }

        private string? GetUserId()
        {
            return User.FindFirst("userId")?.Value
                ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;
        }
    }
}
