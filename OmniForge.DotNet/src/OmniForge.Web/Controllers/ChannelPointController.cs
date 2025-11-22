using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;

namespace OmniForge.Web.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/rewards")]
    public class ChannelPointController : ControllerBase
    {
        private readonly ITwitchApiService _twitchApiService;
        private readonly IUserRepository _userRepository;
        private readonly IChannelPointRepository _channelPointRepository;

        public ChannelPointController(
            ITwitchApiService twitchApiService,
            IUserRepository userRepository,
            IChannelPointRepository channelPointRepository)
        {
            _twitchApiService = twitchApiService;
            _userRepository = userRepository;
            _channelPointRepository = channelPointRepository;
        }

        [HttpGet]
        public async Task<IActionResult> GetRewards()
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            if (!user.Features.ChannelPoints)
            {
                return Forbid("Channel points feature not enabled");
            }

            try
            {
                var rewards = await _channelPointRepository.GetRewardsAsync(userId);
                return Ok(new { rewards, count = rewards.Count() });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to get channel point rewards", details = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateReward([FromBody] CreateRewardRequest request)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            if (!user.Features.ChannelPoints)
            {
                return Forbid("Channel points feature not enabled");
            }

            // Validate request
            if (string.IsNullOrEmpty(request.Title) || request.Cost < 1 || string.IsNullOrEmpty(request.Action))
            {
                return BadRequest("Title, valid cost, and action are required");
            }

            try
            {
                // 1. Create on Twitch
                var twitchReward = await _twitchApiService.CreateCustomRewardAsync(userId, request);

                // 2. Save to DB
                var rewardEntity = new ChannelPointReward
                {
                    UserId = userId,
                    RewardId = twitchReward.Id,
                    RewardTitle = twitchReward.Title,
                    Cost = twitchReward.Cost,
                    Action = request.Action,
                    IsEnabled = twitchReward.IsEnabled,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                await _channelPointRepository.SaveRewardAsync(rewardEntity);

                return Ok(new { message = "Channel point reward created successfully", reward = rewardEntity });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to create channel point reward", details = ex.Message });
            }
        }

        [HttpDelete("{rewardId}")]
        public async Task<IActionResult> DeleteReward(string rewardId)
        {
            var userId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userRepository.GetUserAsync(userId);
            if (user == null) return NotFound("User not found");

            if (!user.Features.ChannelPoints)
            {
                return Forbid("Channel points feature not enabled");
            }

            try
            {
                // 1. Delete from Twitch
                await _twitchApiService.DeleteCustomRewardAsync(userId, rewardId);

                // 2. Delete from DB
                await _channelPointRepository.DeleteRewardAsync(userId, rewardId);

                return Ok(new { message = "Channel point reward deleted successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to delete channel point reward", details = ex.Message });
            }
        }
    }
}
