using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;

namespace OmniForge.Web.Controllers
{
    [Route("api/sync")]
    [ApiController]
    [Authorize(AuthenticationSchemes = "Bearer,Cookies")]
    public class SyncController : ControllerBase
    {
        private readonly ISyncAgentTracker _tracker;
        private readonly ISceneRepository _sceneRepository;
        private readonly ISceneActionRepository _sceneActionRepository;
        private readonly IUserRepository _userRepository;
        private readonly BlobServiceClient? _blobServiceClient;

        public SyncController(
            ISyncAgentTracker tracker,
            ISceneRepository sceneRepository,
            ISceneActionRepository sceneActionRepository,
            IUserRepository userRepository,
            BlobServiceClient? blobServiceClient = null)
        {
            _tracker = tracker;
            _sceneRepository = sceneRepository;
            _sceneActionRepository = sceneActionRepository;
            _userRepository = userRepository;
            _blobServiceClient = blobServiceClient;
        }

        [HttpGet("status")]
        public async Task<IActionResult> GetStatus()
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound();
            if (!user.Features.SceneSync)
                return StatusCode(403, new { error = "Scene sync feature is not enabled for your account" });

            var state = _tracker.GetAgentState(userId);
            return Ok(new
            {
                connected = state != null,
                softwareType = state?.SoftwareType,
                currentScene = state?.CurrentScene,
                connectedAt = state?.ConnectedAt,
                lastHeartbeat = state?.LastHeartbeat
            });
        }

        [HttpGet("scenes")]
        public async Task<IActionResult> GetScenes()
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound();
            if (!user.Features.SceneSync)
                return StatusCode(403, new { error = "Scene sync feature is not enabled for your account" });

            var scenes = await _sceneRepository.GetScenesAsync(userId);
            return Ok(scenes);
        }

        [HttpGet("scene-actions")]
        public async Task<IActionResult> GetSceneActions()
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound();
            if (!user.Features.SceneSync)
                return StatusCode(403, new { error = "Scene sync feature is not enabled for your account" });

            var actions = await _sceneActionRepository.GetAllAsync(userId);
            return Ok(actions);
        }

        [HttpPost("scene-actions")]
        public async Task<IActionResult> SaveSceneAction([FromBody] SceneAction sceneAction)
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound();
            if (!user.Features.SceneSync)
                return StatusCode(403, new { error = "Scene sync feature is not enabled for your account" });

            sceneAction.UserId = userId;
            await _sceneActionRepository.SaveAsync(sceneAction);
            return Ok(sceneAction);
        }

        [HttpDelete("scene-actions/{sceneName}")]
        public async Task<IActionResult> DeleteSceneAction(string sceneName)
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound();
            if (!user.Features.SceneSync)
                return StatusCode(403, new { error = "Scene sync feature is not enabled for your account" });

            await _sceneActionRepository.DeleteAsync(userId, sceneName);
            return NoContent();
        }

        [HttpGet("agent/download")]
        public async Task<IActionResult> DownloadAgent()
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound();
            if (!user.Features.SceneSync)
                return StatusCode(403, new { error = "Scene sync feature is not enabled for your account" });

            if (_blobServiceClient == null)
                return StatusCode(503, new { error = "Blob storage not configured" });

            var containerClient = _blobServiceClient.GetBlobContainerClient("sync-agent");
            var blobClient = containerClient.GetBlobClient("OmniForge.SyncAgent.exe");

            if (!await blobClient.ExistsAsync())
                return NotFound(new { error = "Agent binary not found" });

            // Generate user delegation SAS if possible, otherwise use service SAS
            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = "sync-agent",
                BlobName = "OmniForge.SyncAgent.exe",
                Resource = "b",
                ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(5)
            };
            sasBuilder.SetPermissions(BlobSasPermissions.Read);

            var sasUri = blobClient.GenerateSasUri(sasBuilder);
            return Redirect(sasUri.ToString());
        }

        [HttpGet("agent/version")]
        public async Task<IActionResult> GetAgentVersion()
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound();
            if (!user.Features.SceneSync)
                return StatusCode(403, new { error = "Scene sync feature is not enabled for your account" });

            if (_blobServiceClient == null)
                return StatusCode(503, new { error = "Blob storage not configured" });

            var containerClient = _blobServiceClient.GetBlobContainerClient("sync-agent");
            var versionBlobClient = containerClient.GetBlobClient("agent-version.txt");

            string version = "0.0.0";
            if (await versionBlobClient.ExistsAsync())
            {
                var content = await versionBlobClient.DownloadContentAsync();
                version = content.Value.Content.ToString().Trim();
            }

            var isLive = string.Equals(user.StreamStatus, "live", StringComparison.OrdinalIgnoreCase);

            return Ok(new
            {
                version,
                isLive,
                downloadUrl = "/api/sync/agent/download"
            });
        }

        private string? GetUserId()
        {
            return User.FindFirst("userId")?.Value
                ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;
        }
    }
}
