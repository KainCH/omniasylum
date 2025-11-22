using System;
using System.Collections.Generic;
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
    [Route("api/moderator")]
    public class ModeratorController : ControllerBase
    {
        private readonly IUserRepository _userRepository;
        private readonly ICounterRepository _counterRepository;

        public ModeratorController(IUserRepository userRepository, ICounterRepository counterRepository)
        {
            _userRepository = userRepository;
            _counterRepository = counterRepository;
        }

        [HttpGet("my-moderators")]
        public async Task<IActionResult> GetMyModerators()
        {
            var streamerId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(streamerId)) return Unauthorized();

            var allUsers = await _userRepository.GetAllUsersAsync();
            var myModerators = allUsers.Where(u =>
                u.Role == "mod" &&
                u.ManagedStreamers.Contains(streamerId)
            ).Select(mod => new
            {
                userId = mod.TwitchUserId,
                username = mod.Username,
                displayName = mod.DisplayName,
                profileImageUrl = mod.ProfileImageUrl,
                lastLogin = mod.LastLogin,
                isActive = mod.IsActive,
                grantedAt = mod.CreatedAt // Placeholder
            });

            return Ok(new
            {
                moderators = myModerators,
                total = myModerators.Count(),
                streamerId
            });
        }

        [HttpPost("grant-access")]
        public async Task<IActionResult> GrantAccess([FromBody] GrantAccessRequest request)
        {
            var streamerId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(streamerId)) return Unauthorized();

            if (string.IsNullOrEmpty(request.ModeratorUserId))
            {
                return BadRequest("Moderator user ID is required");
            }

            if (request.ModeratorUserId == streamerId)
            {
                return BadRequest("Cannot grant moderator access to yourself");
            }

            var moderatorUser = await _userRepository.GetUserAsync(request.ModeratorUserId);
            if (moderatorUser == null)
            {
                return NotFound("Moderator user not found");
            }

            if (moderatorUser.Role != "mod")
            {
                if (moderatorUser.Role == "streamer")
                {
                    moderatorUser.Role = "mod";
                }
                else if (moderatorUser.Role == "admin")
                {
                    return BadRequest("Cannot grant moderator access to admin users");
                }
            }

            if (!moderatorUser.ManagedStreamers.Contains(streamerId))
            {
                moderatorUser.ManagedStreamers.Add(streamerId);
                await _userRepository.SaveUserAsync(moderatorUser);
            }
            else
            {
                return Conflict("User already has moderator access to your settings");
            }

            return Ok(new
            {
                message = "Moderator access granted successfully",
                moderator = new
                {
                    userId = moderatorUser.TwitchUserId,
                    username = moderatorUser.Username,
                    displayName = moderatorUser.DisplayName,
                    role = moderatorUser.Role
                },
                streamer = new
                {
                    userId = streamerId,
                    username = User.FindFirst("username")?.Value
                }
            });
        }

        [HttpDelete("revoke-access/{moderatorUserId}")]
        public async Task<IActionResult> RevokeAccess(string moderatorUserId)
        {
            var streamerId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(streamerId)) return Unauthorized();

            var moderatorUser = await _userRepository.GetUserAsync(moderatorUserId);
            if (moderatorUser == null)
            {
                return NotFound("Moderator user not found");
            }

            if (moderatorUser.ManagedStreamers.Contains(streamerId))
            {
                moderatorUser.ManagedStreamers.Remove(streamerId);

                // Downgrade to streamer if no more managed streamers
                if (moderatorUser.ManagedStreamers.Count == 0 && moderatorUser.Role == "mod")
                {
                    moderatorUser.Role = "streamer";
                }

                await _userRepository.SaveUserAsync(moderatorUser);
            }
            else
            {
                return NotFound("User does not have moderator access to your settings");
            }

            return Ok(new
            {
                message = "Moderator access revoked successfully",
                moderator = new
                {
                    userId = moderatorUser.TwitchUserId,
                    username = moderatorUser.Username,
                    displayName = moderatorUser.DisplayName,
                    role = moderatorUser.Role
                },
                streamer = new
                {
                    userId = streamerId,
                    username = User.FindFirst("username")?.Value
                }
            });
        }

        [HttpGet("search-users")]
        public async Task<IActionResult> SearchUsers([FromQuery] string q)
        {
            var streamerId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(streamerId)) return Unauthorized();

            if (string.IsNullOrEmpty(q) || q.Length < 2)
            {
                return BadRequest("Search query must be at least 2 characters");
            }

            var allUsers = await _userRepository.GetAllUsersAsync();
            var searchResults = allUsers
                .Where(u =>
                    u.TwitchUserId != streamerId &&
                    u.Role != "admin" &&
                    u.IsActive &&
                    (u.Username.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                     u.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase))
                )
                .Take(10)
                .Select(u => new
                {
                    userId = u.TwitchUserId,
                    username = u.Username,
                    displayName = u.DisplayName,
                    profileImageUrl = u.ProfileImageUrl,
                    role = u.Role,
                    isAlreadyModerator = u.ManagedStreamers.Contains(streamerId)
                });

            return Ok(new
            {
                results = searchResults,
                query = q,
                total = searchResults.Count()
            });
        }

        [HttpGet("managed-streamers")]
        public async Task<IActionResult> GetManagedStreamers()
        {
            var moderatorId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(moderatorId)) return Unauthorized();

            var moderator = await _userRepository.GetUserAsync(moderatorId);
            if (moderator == null) return NotFound("User not found");

            // Check if user is actually a mod or admin (admins can manage everyone, but this endpoint is for specific assignments)
            // Legacy code checks requireModAccess which allows mod or admin.
            // But logic relies on managedStreamers list.

            var streamers = new List<object>();
            foreach (var streamerId in moderator.ManagedStreamers)
            {
                var streamer = await _userRepository.GetUserAsync(streamerId);
                if (streamer != null)
                {
                    var counters = await _counterRepository.GetCountersAsync(streamerId);
                    streamers.Add(new
                    {
                        userId = streamer.TwitchUserId,
                        username = streamer.Username,
                        displayName = streamer.DisplayName,
                        profileImageUrl = streamer.ProfileImageUrl,
                        isActive = streamer.IsActive,
                        lastLogin = streamer.LastLogin,
                        streamStatus = streamer.StreamStatus ?? "offline",
                        counters = new
                        {
                            deaths = counters?.Deaths ?? 0,
                            swears = counters?.Swears ?? 0,
                            screams = counters?.Screams ?? 0,
                            bits = counters?.Bits ?? 0
                        }
                    });
                }
            }

            return Ok(new
            {
                streamers,
                total = streamers.Count,
                moderatorId
            });
        }

        [HttpGet("streamers/{streamerId}")]
        public async Task<IActionResult> GetStreamerDetails(string streamerId)
        {
            var moderatorId = User.FindFirst("userId")?.Value;
            if (string.IsNullOrEmpty(moderatorId)) return Unauthorized();

            var moderator = await _userRepository.GetUserAsync(moderatorId);
            if (moderator == null) return NotFound("User not found");

            // Check permission
            // Admin can access all, Mod only managed
            var isAdmin = User.IsInRole("admin");
            if (!isAdmin && !moderator.ManagedStreamers.Contains(streamerId))
            {
                return Forbid("You do not have permission to manage this streamer");
            }

            var streamer = await _userRepository.GetUserAsync(streamerId);
            if (streamer == null) return NotFound("Streamer not found");

            var counters = await _counterRepository.GetCountersAsync(streamerId);

            return Ok(new
            {
                streamer = new
                {
                    userId = streamer.TwitchUserId,
                    username = streamer.Username,
                    displayName = streamer.DisplayName,
                    profileImageUrl = streamer.ProfileImageUrl,
                    isActive = streamer.IsActive,
                    lastLogin = streamer.LastLogin,
                    streamStatus = streamer.StreamStatus ?? "offline"
                },
                counters = new
                {
                    deaths = counters?.Deaths ?? 0,
                    swears = counters?.Swears ?? 0,
                    screams = counters?.Screams ?? 0,
                    bits = counters?.Bits ?? 0
                },
                settings = streamer.Features.StreamSettings
            });
        }
    }

    public class GrantAccessRequest
    {
        public string ModeratorUserId { get; set; } = string.Empty;
    }
}
